# Fulbr Architecture Overview

**Status:** Phase 2 design baseline
**Date:** 2026-11-09
**Product name:** Fulbr
**License:** GPL-3.0-or-later
**Target:** Windows 10/11 x64

---

## 1. Design goals

1. **Real functionality only.** Every state shown in the UI is derived from a real system
   check or explicitly labeled as unknown/mock.
2. **Fail closed by default.** A tunnel failure must not silently expose protected traffic.
3. **Honest trust reporting.** Connectivity, performance, and security are separate.
4. **Deterministic routing.** The same inputs produce the same route, and the user can see
   which rule won.
5. **Small blast radius.** The UI is unprivileged; privileged operations live in a narrow
   service.
6. **Maintainability.** Modules with typed interfaces, golden tests, and no hidden global
   state.
7. **Low resource use.** No continuous polling, bounded concurrency, virtualized lists,
   efficient persistence.
8. **Bilingual by design.** English and Russian resources from day one; no string
   concatenation for UI text.

---

## 2. Component map

```
Fulbr.exe (WPF, .NET 10, x64)
├── Presentation
│   ├── Dashboard, Servers, Diagnostics, Routing, Subscriptions, Security, Settings, Logs
│   ├── Design system (light/dark, DPI, accessibility, keyboard)
│   └── Tray icon + notifications
├── Application
│   ├── Connection orchestration (UI side)
│   ├── Server list view models, filtering, sorting
│   ├── Diagnostics view models
│   └── Localization
├── Domain
│   ├── ServerConfig, SourceProvenance, HealthSample, TrustAssessment
│   ├── RoutingRule, RouteDecision
│   └── Subscription, RuleSet
├── Infrastructure
│   ├── Named-pipe client to Fulbr.Service
│   ├── SQLite repositories
│   ├── DPAPI secret store
│   ├── HTTP subscription client (conditional requests)
│   ├── Parsers (share-link, Base64, JSON proxy list)
│   └── Redacting logger
└── Updates/About

Fulbr.Service.exe (Windows service, privileged)
├── Engine supervisor (sing-box child process)
├── Network state manager (Wintun, routes, DNS)
├── WFP policy manager (kill switch, fail-closed)
├── Health probe runner (short-lived engine instances)
├── Watchdog + recovery
├── Named-pipe server (ACL, command validation)
└── Audit log (privileged actions)

sing-box.exe (pinned, unmodified)
└── TUN, outbounds, DNS, rule sets, Clash-compatible API
```

**Why a service?** TUN creation, route changes, DNS changes, and WFP filters require
administrator rights. Putting them in the UI would force the whole UI to run elevated and
would give a rendering bug the ability to break networking. The service exposes only a
small typed command set.

---

## 3. Module interfaces (contracts)

These are design contracts; implementations will live in separate projects.

### 3.1 Subscription and parsing

```csharp
public interface ISubscriptionFetcher
{
    Task<FetchResult> FetchAsync(SubscriptionSource source, FetchPolicy policy, CancellationToken ct);
}

public interface IConfigParser
{
    ParseResult Parse(string content, SourceProvenance provenance);
}

public interface IServerRepository
{
    Task<UpsertResult> UpsertAsync(IReadOnlyList<ServerCandidate> candidates, CancellationToken ct);
    Task<PagedResult<ServerRecord>> QueryAsync(ServerQuery query, CancellationToken ct);
}
```

- `FetchResult` carries HTTP status, ETag, Last-Modified, final URL, content hash,
  timestamps, and a typed error.
- `ParseResult` carries accepted candidates, rejected entries with reasons, warnings, and
  the parser version.
- Parsers never execute content; they only produce typed values.

### 3.2 Health and ranking

```csharp
public interface IHealthProbe
{
    Task<HealthSample> ProbeAsync(ServerRecord server, ProbeKind kind, CancellationToken ct);
}

public interface IRankingService
{
    IReadOnlyList<RankedServer> Rank(IEnumerable<ServerRecord> servers, RankingPreset preset);
}
```

`ProbeKind`: `Icmp`, `TcpConnect`, `TlsHandshake`, `ProtocolHandshake`,
`HttpThroughProxy`, `Download`, `Upload`.

### 3.3 Engine and routing

```csharp
public interface IEngineController
{
    Task<EngineStatus> StartAsync(EnginePlan plan, CancellationToken ct);
    Task StopAsync(StopReason reason, CancellationToken ct);
    Task<EngineStatus> GetStatusAsync(CancellationToken ct);
}

public interface IRoutingPolicy
{
    RouteDecision Decide(RouteQuery query);
}

public interface ILeakTestService
{
    Task<LeakTestReport> RunAsync(LeakTestKind kind, CancellationToken ct);
}
```

The UI never sends a raw engine config. It sends a validated `EnginePlan` (server ID +
routing profile + safety mode), and the service constructs the config.

### 3.4 Secrets

```csharp
public interface ISecretStore
{
    Task<SecretHandle> ProtectAsync(ReadOnlyMemory<byte> secret, SecretScope scope, CancellationToken ct);
    Task<ReadOnlyMemory<byte>> UnprotectAsync(SecretHandle handle, CancellationToken ct);
    Task DeleteAsync(SecretHandle handle, CancellationToken ct);
}
```

DPAPI current-user scope for UI-held secrets; service-scope protection for runtime
engine secrets. Secrets never enter logs, crash dumps, or telemetry.

---

## 4. Data model

**Prototype note (2026-11-09):** the current UI prototype stores settings, sources, and
servers in `%LOCALAPPDATA%\Fulbr\state.json`, with server credentials and subscription URLs
protected by Windows DPAPI. The SQLite schema below is the target for the next storage
iteration; it is not implemented yet.

SQLite (WAL, foreign keys, bounded retention):

| Table | Purpose |
|-------|---------|
| `sources` | Subscription URL, display name, auth handle, enabled, interval, ETag, Last-Modified, last fetch, last success, content hash, trust label |
| `servers` | Canonical ID, protocol, host, port, display name, source ID, raw config handle, fingerprint, first seen, last seen, favorite, user-created flag, lifecycle state |
| `server_secrets` | Secret handles (never plaintext) |
| `health_samples` | Server ID, probe kind, timestamp, outcome, latency ms, error class, test target, engine version |
| `server_stats` | Rolling median/MAD, success counts, consecutive failures, backoff next-at, reliability |
| `route_rules` | User rules, precedence, match type, action, enabled, source, created/updated |
| `rulesets` | Rule-set name, version/hash, source URL, fetched time, path |
| `subscription_history` | Provenance and change audit per refresh |
| `audit_log` | Privileged actions, config changes, quarantine/purge decisions |
| `settings` | Versioned key/value JSON with schema migrations |
| `connection_history` | Start/end, server, protocol, duration, bytes, disconnect reason (no destinations) |

Retention defaults:

- Health samples: 30 days (aggregate after 7 days).
- Audit log: 180 days.
- Connection history: 90 days, user-clearable.
- Quarantined configs: 30 days, then hidden (not deleted if user-created).
- Expired source snapshots: 7 days, keep last known-good.

---

## 5. Server lifecycle

```
New
 └─> PendingVerification
       ├─ success ─> Healthy
       └─ failure ─> Degraded
             ├─ repeated success ─> Healthy
             ├─ repeated failure ─> TemporarilyUnavailable
             │                        ├─ recovery ─> Healthy
             │                        └─ threshold ─> Quarantined
             └─ invalid config ─> Quarantined (immediately, with reason)
Quarantined
 ├─ manual retry ─> PendingVerification
 ├─ source update with changed fingerprint ─> PendingVerification
 └─ retention elapsed ─> Expired (hidden; purge only if allowed and not user-created)
```

Rules:

- **Never delete after one failure.**
- **Never delete user-created configs automatically.**
- Preserve favorites; if a favorite is quarantined, show it with a warning rather than
  hiding it silently.
- Exponential backoff: 1 min, 5 min, 15 min, 1 h, 6 h, 24 h (capped), with jitter.
- Classify failures:

| Class | Meaning | Action |
|-------|---------|--------|
| `LocalNetworkDown` | No local connectivity | Pause checks; do not penalize servers |
| `DnsFailure` | Hostname did not resolve | Retry with backoff; distinguish NXDOMAIN vs timeout |
| `TcpTimeout` / `TcpRefused` | Endpoint unreachable | Degraded, retry |
| `TlsFailure` | Certificate/handshake failure | Security warning; do not auto-trust |
| `AuthFailure` | Credentials rejected | Quarantine after threshold; likely dead config |
| `ProtocolError` | Malformed response/unsupported feature | Quarantine; record parser/engine version |
| `RateLimited` | Upstream/API limit | Backoff longer; never hammer |
| `InvalidConfig` | Parser/validator rejected | Quarantine immediately, keep raw for audit |

- **Widespread outage detection:** if >80% of servers fail with `LocalNetworkDown` or the
  same network error within a short window, suspend penalty and show "Your internet
  connection appears to be down" instead of "all servers dead".
- Recovery: a quarantined server is retried on manual request, source change, or the
  backoff schedule; success returns it to `Healthy`.

---

## 6. Server ranking (transparent, deterministic)

Inputs:

- `successRate` over the recent window, using the **Wilson lower bound** (not raw
  percentage) to avoid 1/1 = 100% misleading scores.
- `medianLatency` from the most representative probe (HTTP-through-proxy preferred; fall
  back to protocol handshake; ICMP only if explicitly chosen).
- `jitter` (median absolute deviation).
- `handshakeSuccessRate`.
- `freshness` (time since last successful check, exponential decay).
- `recentFailurePenalty` (consecutive failures, capped).
- Optional `throughput` only if the user ran a speed test.

Formula (weights are user-selectable presets):

```
score = 100 * (
    w_success * wilsonLowerBound
  + w_latency * normalizedLatency
  + w_stability * stability
  + w_freshness * freshness
  + w_handshake * handshakeSuccess
  - w_failure * failurePenalty
)
```

Presets:

- **Balanced** (default): success 0.35, latency 0.25, stability 0.20, freshness 0.10,
  handshake 0.10.
- **Lowest latency**: latency 0.45, success 0.25, stability 0.15, freshness 0.10,
  handshake 0.05.
- **Highest stability**: stability 0.35, success 0.35, latency 0.15, freshness 0.10,
  handshake 0.05.
- **Highest speed**: throughput 0.40 (only when measured), success 0.25, latency 0.20,
  stability 0.15.

Rules:

- A server with no measurements ranks below a measured healthy server.
- A single low ping cannot beat a stable server with a slightly higher median.
- Never rank by display name, country flag, or "premium" label.
- Show the score components in the UI ("Why this ranking?").
- Tests use the same method per comparison; never compare ICMP to HTTP-through-proxy.

---

## 7. Routing precedence

Highest priority first:

1. **Explicit user block rules** — always win. A block cannot be overridden by a lower rule.
2. **Explicit user application/domain/IP rules** — user intent.
3. **Security and leak-prevention rules** — e.g., block IPv6 when the tunnel is IPv4-only,
   block unprotected DNS, block traffic during a failed tunnel.
4. **Trusted service-specific rules** — user-marked trusted services/proxies.
5. **Regional routing lists** — Russian `category-ru` / direct list.
6. **Default connection policy** — Full Tunnel / Smart Split / Custom / Proxy / Direct.

Conflict resolution:

- First match wins within a priority tier.
- More specific match wins within the same tier: exact domain/path > suffix > keyword >
  CIDR > regional list > default.
- If two rules in the same tier conflict, the UI shows a conflict warning and requires the
  user to resolve it; the engine uses a stable, documented tie-breaker (rule ID ascending)
  so behavior is deterministic.
- A direct rule never overrides a block rule.
- A proxy rule never overrides a security block unless the user explicitly acknowledges
  the risk.

`Why This Route?` output must include:

- requested domain / process / destination,
- resolved IP(s) and DNS server used,
- matched rule ID and tier,
- chosen outbound (direct/proxy/block),
- actual interface/route selected (verified, not assumed),
- whether the VPN tunnel was used,
- timestamp and data source.

Do not log browsing history by default. The inspection is user-initiated and short-lived.

---

## 8. Subscription synchronization

1. User adds a source (opt-in). Default preset is disabled until the user enables it.
2. Fetch with `If-None-Match` / `If-Modified-Since` when available.
3. Enforce HTTPS, redirect limit, timeout, and max size.
4. Parse into candidates; reject malformed entries with reasons.
5. Canonicalize and deduplicate by fingerprint.
6. Validate protocol fields and security flags; mark trust `Unknown`.
7. Upsert; preserve user edits/favorites; record provenance and content hash.
8. On failure, keep the previous snapshot; show "last successful fetch" and the error.
9. Default interval: **6 hours with jitter** (upstream updates every 2–4 hours; polling
   faster adds no value and abuses GitHub). Minimum configurable interval: 1 hour.
10. Rule sets: daily refresh, pinned and checksummed; manual refresh available.
11. Never poll GitHub continuously. Never bypass rate limits. Respect `Retry-After`.

Formats supported initially:

- Plain text share links, one per line.
- Base64-encoded share-link lists.
- JSON array of proxy objects matching a strict allow-list (no routing/scripts).
- Reject Clash YAML in v1 unless a safe, schema-validated parser is added.

---

## 9. Connection state machine

```
Disconnected
  └─ connect ─> Connecting
        ├─ validation/engine/route check fails ─> Error (safe state)
        └─ all checks pass ─> Connected
Connected
  ├─ network change / sleep / engine error ─> Reconnecting
  ├─ user disconnect ─> Disconnecting ─> Disconnected
  └─ unrecoverable ─> ProtectionLost (blocked)
Reconnecting
  ├─ success + checks ─> Connected
  └─ failure threshold ─> ProtectionLost (blocked) or Error
```

Requirements:

- Never show `Connected` before the tunnel, DNS, and route verification pass.
- `Connected` requires an end-to-end HTTP-through-tunnel check, not just an engine process
  being alive.
- On `ProtectionLost`, protected traffic stays blocked if the safety mode says so.
- Every transition is timestamped and visible in Logs (sanitized).

---

## 10. Security assessment model

For each server, compute:

- **Connectivity:** `Unknown` / `Unreachable` / `Reachable` / `Authenticated` /
  `TrafficVerified`.
- **Performance:** `Unknown` / sample count + median/jitter.
- **Security assessment:** `Unknown` / `Limited` / `Insecure` / `Good`, based on:
  - TLS enabled and certificate verified,
  - known-insecure flags (`allowInsecure`, `skip-cert-verify`, `security=none`),
  - legacy/weak ciphers,
  - protocol/transport consistency,
  - source provenance (user-created vs public subscription),
  - config changes since last successful check.

Never combine them into a single "trust score". Show `Trust: Unknown` when evidence is
insufficient.

---

## 11. UI structure

- **Dashboard** — connection state, primary button, current server/protocol, duration,
  rates, totals, public IP (only when verified), routing mode, kill-switch/DNS status.
- **Servers** — virtualized list, search/filter/sort, favorites, bulk checks, best server,
  trust badges, provenance, "Why this server?".
- **Diagnostics** — ping all/selected, connectivity, speed (opt-in), DNS leak, IPv6, public
  IP, routing verification, stability.
- **Routing** — modes, app/domain/IP rules, Russian service groups, precedence editor,
  conflict warnings, "Why This Route?".
- **Subscriptions** — sources, auth, interval, last fetch, conditional-request status,
  parse results, rejected entries, provenance.
- **Security** — kill-switch mode, DNS policy, IPv6 policy, leak-test results, public-server
  warnings.
- **Settings** — language, theme, startup, tray, updates, logging/retention, advanced.
- **Logs** — sanitized events, filters, export with redaction, clear.

Design system: original, minimalist, Fluent-inspired but not a copy of Windows/Happ.
Light/dark, high contrast, 100–200% DPI, keyboard navigation, screen-reader labels.

---

## 12. Error handling and logging

- Typed error codes (e.g., `FUL-DNS-001`, `FUL-TLS-003`, `FUL-AUTH-001`) with a
  user-readable message and a technical detail section.
- No secrets in errors. Redact UUIDs, passwords, tokens, full URLs, and query strings.
- Log levels: Error, Warn, Info, Debug. Debug is opt-in and time-bounded.
- Log rotation and user-controlled deletion.
- Crash reports local-only by default; upload requires explicit consent and redaction.

---

## 13. Packaging and updates

- `Fulbr.exe` — self-contained x64 WPF app.
- `Fulbr.Service.exe` — Windows service.
- `sing-box.exe` + `wintun.dll` — pinned, checksummed.
- `Fulbr-Setup.exe` — Inno Setup installer, per-machine, Start Menu, optional desktop
  shortcut, uninstall entry.
- Separate update channels: app, engine, rule sets, subscriptions. A subscription refresh
  never triggers a binary update.
- Signed artifacts when a certificate is available; unsigned builds are labeled.

---

## 14. Testing strategy

- Unit: parsers, canonicalization, ranking, rule precedence, redaction, state machine.
- Property-based: malformed configs, duplicate fingerprints, unicode/IDN, huge inputs.
- Integration: local mock SOCKS/HTTP servers, mock engine, named-pipe auth, WFP filter
  add/remove in a VM.
- Windows VM: TUN, routes, DNS, kill switch, sleep/resume, Wi-Fi/Ethernet switch, uninstall.
- Regression: golden engine configs per supported protocol and version.
- Security: fuzz parsers, test path traversal, SSRF/redirect handling, secret leakage.
- Performance: startup, idle RAM/CPU, 1k/10k server lists, throughput, 8-hour soak.

No intrusive testing against public infrastructure. Real endpoints only for bounded,
user-approved checks.

---

## 15. Known limitations (to be shown honestly)

- Public VPN servers are untrusted; Fulbr cannot audit their operators.
- DNS privacy is not guaranteed for applications with their own DoH/DoT/ECH.
- Per-app routing may not cover UWP/services/short-lived processes.
- Domain classification cannot be perfect with CDNs and ECH.
- `strict_route` may break VirtualBox/some apps; a tested fallback is required.
- Kill-switch recovery after a service crash needs a watchdog and a recovery command.
- No benchmark or leak-test claim is valid until measured on the user's machine.
