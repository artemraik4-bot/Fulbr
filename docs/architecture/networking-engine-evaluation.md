# Networking Engine Evaluation and Integration Design

**Status:** Phase 1/2 decision record
**Date accessed:** 2026-11-09
**Decision:** sing-box v1.14.4 as the default engine, invoked as an isolated child process.

---

## 1. Options evaluated

| Engine | Protocols | Windows TUN | Routing/DNS | Local API | License | Verdict |
|--------|-----------|-------------|-------------|-----------|---------|---------|
| **sing-box** | VLESS/Reality, VMess, Trojan, SS, Hysteria2, TUIC, WireGuard, AnyTLS, ShadowTLS, SOCKS/HTTP | Yes, Wintun | TUN, route rules, rule sets, DNS, process/path matching, `strict_route` | Clash-compatible HTTP API | GPL-3.0-or-later + name clause | **Selected** |
| **Xray-core** | VLESS/Reality (origin), VMess, Trojan, SS, WireGuard, SOCKS/HTTP | No built-in TUN | Needs an external TUN/proxy layer | gRPC API | MPL-2.0 | Optional later |
| **WireGuard for Windows** | WireGuard only | Yes (Wintun + service) | Minimal; not a censorship-circumvention engine | None comparable | MIT | Not sufficient alone |
| **Mihomo/Clash Meta** | Broad | Yes | Clash-style rules | Clash API | GPL-3.0 per `LICENSE`; GitHub badge inconsistent | Viable alternative; auditing required |
| **OpenVPN** | OpenVPN | Yes (TAP/Wintun + service) | Limited | Management interface | GPL-2.0 | Not in reference sources; out of v1 |

Primary sources: <https://sing-box.sagernet.org/configuration/outbound/>,
<https://sing-box.sagernet.org/configuration/inbound/tun/>,
<https://github.com/XTLS/Xray-core>, <https://github.com/MetaCubeX/mihomo>,
<https://github.com/WireGuard/wireguard-windows>, accessed 2026-11-09.

### Why sing-box wins

1. **Protocol breadth**: one engine covers every protocol in the required source set.
2. **Native Windows TUN**: Wintun integration, `auto_route`, `strict_route`, route
   include/exclude, DNS handling.
3. **Process-aware routing**: `process_name`, `process_path`, `process_path_regex` are
   documented as supported on Windows. This is the foundation for per-app split tunneling.
4. **DNS as a first-class module**: rules, DoH/DoT, fake-IP optional, cache control.
5. **Operational API**: the Clash-compatible API exposes proxy state, traffic, and real
   delay measurements, which Fulbr needs for honest diagnostics.
6. **Maintenance**: actively developed, huge user base, frequent releases.

### Why not Xray-only

Xray is the origin of Reality and is excellent at the protocols it covers, but it does not
ship a built-in cross-platform TUN. Combining Xray with a separate TUN layer would mean
two moving parts, two config models, and two failure modes. It also lacks the
Hysteria2/TUIC coverage that the reference sources use. Xray remains a candidate as an
optional engine only if a specific Reality edge case cannot be handled by sing-box.

### Why not WireGuard-only

WireGuard is a superb VPN, but its handshake is fingerprintable and it does not cover the
proxy protocols in the required sources. It is supported *through* sing-box for users who
have WireGuard configs, not as the sole engine.

---

## 2. Process model

```
┌──────────────────────────────┐        named pipe (ACL)        ┌──────────────────────────────┐
│ Fulbr.exe (unelevated UI)    │ <────────────────────────────> │ Fulbr.Service (privileged)   │
│ WPF, tray, settings, logs    │   small typed command set      │ routes, DNS, WFP, watchdog   │
└──────────────────────────────┘                                └──────────────┬───────────────┘
                                                                                │ child process
                                                                                ▼
                                                                 ┌──────────────────────────────┐
                                                                 │ sing-box.exe (pinned)        │
                                                                 │ TUN + outbounds + DNS + API  │
                                                                 └──────────────────────────────┘
```

Rules:

- The UI never runs as administrator and never calls WFP/route APIs directly.
- The service validates every request: command name, schema, bounds, caller identity.
- The service generates the sing-box config; the UI never supplies a raw config.
- The engine runs with the least privilege the service can grant while still creating
  the TUN adapter (normally LocalSystem or a dedicated service account).
- The engine config contains secrets; it is written to a service-only directory and is
  deleted on stop.
- The engine's local API binds `127.0.0.1` with a random per-session secret.

---

## 3. Engine configuration generation

Fulbr generates a minimal config per connection:

```jsonc
{
  "log": { "level": "warn", "timestamp": true },
  "dns": {
    "servers": [
      { "tag": "proxy-dns", "address": "https://dns.example/dns-query", "detour": "proxy" },
      { "tag": "direct-dns", "address": "https://dns.example/dns-query", "detour": "direct" }
    ],
    "rules": [
      { "domain_suffix": [".ru"], "server": "direct-dns" },
      { "rule_set": ["ru-direct"], "server": "direct-dns" }
    ],
    "final": "proxy-dns"
  },
  "inbounds": [
    {
      "type": "tun",
      "tag": "tun-in",
      "interface_name": "Fulbr",
      "address": ["172.19.0.1/30", "fdfe:dcba:9876::1/126"],
      "mtu": 1500,
      "auto_route": true,
      "strict_route": true,
      "stack": "gvisor"
    },
    {
      "type": "mixed",
      "tag": "local-in",
      "listen": "127.0.0.1",
      "listen_port": 0,
      "users": [{ "username": "fulbr", "password": "<random-per-session>" }]
    }
  ],
  "outbounds": [
    { "type": "vless", "tag": "proxy", "...": "generated from one validated server" },
    { "type": "direct", "tag": "direct" },
    { "type": "block", "tag": "block" }
  ],
  "route": {
    "auto_detect_interface": true,
    "rules": [
      { "action": "sniff" },
      { "protocol": "dns", "action": "hijack-dns" },
      { "rule_set": ["ru-direct"], "outbound": "direct" },
      { "process_path": ["C:\\Program Files\\Fulbr\\Fulbr.exe"], "outbound": "direct" }
    ],
    "rule_set": [
      {
        "tag": "ru-direct",
        "type": "local",
        "format": "binary",
        "path": "C:\\ProgramData\\Fulbr\\rulesets\\ru-direct.srs"
      }
    ],
    "final": "proxy"
  },
  "experimental": {
    "clash_api": {
      "external_controller": "127.0.0.1:0",
      "secret": "<random-per-session>"
    },
    "cache_file": { "enabled": true, "path": "C:\\ProgramData\\Fulbr\\runtime\\cache.db" }
  }
}
```

Notes:

- The exact schema is validated against the pinned sing-box version. Fields differ
  between versions; never assume.
- `strict_route` is a safety feature but can break VirtualBox and some applications.
  It must be a user-visible setting with a tested fallback.
- The local mixed inbound is for opt-in proxy mode and diagnostics. It must require
  authentication and bind loopback only.
- No fake-IP by default; fake-IP changes application-visible DNS behavior and complicates
  "Why This Route?".

---

## 4. Health checks using the engine

Two complementary modes:

### 4.1 Short-lived probe engine

For a candidate server, start a minimal sing-box instance with:

- a loopback SOCKS inbound (random port + credentials),
- the candidate outbound,
- no TUN, no system routes.

Then perform a SOCKS5 CONNECT to a controlled test endpoint and require a valid HTTP
response. Record:

- TCP connect time,
- TLS handshake time (if applicable),
- protocol/auth completion,
- first byte / full response time,
- failure class (DNS, TCP, TLS, auth, timeout, reset).

This measures the real protocol handshake, not just an open port.

### 4.2 Long-lived engine with Clash API

For bulk "check all" and for the active connection, use the running engine's
Clash-compatible API:

- `GET /proxies` — state and current selection,
- `GET /proxies/{name}/delay?url=...&timeout=...` — real request through the outbound,
- traffic counters for the dashboard.

Security:

- bind `127.0.0.1` only,
- random secret per session,
- no CORS origins,
- API port chosen dynamically,
- never expose the API on LAN unless the user explicitly enables LAN sharing (a separate,
  clearly labeled feature).

**Do not** use a single ping as the health signal. A server can accept TCP and fail auth,
or authenticate and fail to pass traffic (the reference repo documents this exact failure
mode).

---

## 5. Engine lifecycle

1. Service validates the selected server and generated config (`sing-box check`).
2. Service enters `Connecting`, installs fail-closed WFP filters, saves route/DNS state.
3. Service starts sing-box with a restricted working directory and a per-session API
   secret.
4. Service waits for the API to become ready, then performs an end-to-end HTTP-through-
   tunnel check.
5. Only after that check succeeds does the service report `Connected` to the UI and open
   the traffic path for protected apps.
6. On stop: stop engine, remove WFP filters, restore routes/DNS, flush DNS cache, delete
   runtime config.
7. On crash: watchdog keeps filters, attempts bounded restart with backoff, reports
   `Reconnecting` or `Protection lost`.
8. On sleep/resume or network change: re-detect interface, re-bind engine outbound,
   re-verify routes/DNS/WFP, and re-run the end-to-end check.

---

## 6. What Fulbr will not do with the engine

- No hot-patching of sing-box.
- No custom cryptographic protocols.
- No disabling TLS verification to "make it work".
- No passing raw user JSON directly to sing-box.
- No importing client-specific JSON with embedded routing/scripts.
- No claim that a successful delay test proves the server trustworthy.

---

## 7. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Engine CVE or regression | Pin version, watch advisories, separate update channel, keep last known-good, test in VM. |
| Engine crash leaks traffic | Service-owned WFP filters, fail closed, watchdog, recovery command. |
| Config schema drift | Version-specific generator + `sing-box check` + golden tests. |
| Clash API exposure | Loopback + random secret + no CORS. |
| Process matching races | Prefer path allow-lists; test short-lived processes; document limitations. |
| UDP/QUIC blocked by ISP | Detect and label; fall back to TCP-based protocols only with user consent. |
| GPL obligations | Source offer, notices, no implied endorsement. |

---

## 8. Benchmark plan (not results)

Measure, do not invent:

- startup to first paint,
- idle RAM/CPU (disconnected and connected),
- connect time per protocol,
- handshake latency vs TCP latency vs HTTP-through-proxy latency,
- throughput (download/upload) with a bounded test,
- server switching time,
- health-check CPU/network overhead with 10/100/1000 servers,
- 8-hour stability and memory growth,
- sleep/resume and Wi-Fi/Ethernet transition behavior.

Results will live in `docs/performance/benchmarks.md` after actual measurement.
