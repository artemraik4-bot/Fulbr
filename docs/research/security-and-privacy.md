# Security and Privacy Research Notes

**Status:** Phase 1 research; feeds the formal threat model
**Date accessed:** 2026-11-09
**Principle:** Privacy is not "connected to a VPN". Privacy is knowing where traffic goes,
who can observe it, and whether the user's policy is actually enforced.

---

## 1. What a public proxy can observe

For the protocols in scope (VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC,
WireGuard), a working connection provides confidentiality between the client and the exit
server. It does **not** hide the following from the exit operator:

- client source IP address (including IPv4/IPv6 and, often, coarse geolocation),
- connection start/end time and duration,
- bytes/packets sent and received,
- destination IP addresses and ports at the exit,
- unencrypted HTTP content,
- DNS queries if the server handles DNS,
- TLS SNI unless the transport hides it (ECH/Reality may change this),
- application-level identifiers that are not encrypted.

A public server is operated by an unknown party. It can log, rate-limit, inject content,
or disappear. It can also be compelled or compromised. A low ping proves none of these
risks are absent.

**Fulbr rule:** every imported public endpoint starts as `Trust: Unknown`. Trust is a
separate assessment from connectivity and performance, and is never inferred from
geography, name, or speed.

---

## 2. Reference-repository risks

The reference repository (`igareck/vpn-configs-for-russia`, GPL-3.0) is a useful source of
candidate endpoints, but its own README documents systemic risks:

1. **Unstable public endpoints.** Availability varies by time of day and ISP; configs
   appear and disappear quickly.
2. **Client-specific bundles.** Some exports include routing rules, health-check groups,
   and other behavior. Importing them as-is would execute third-party policy.
3. **Localhost proxy exposure.** The README cites a real analysis of local SOCKS/HTTP
   inbounds without authentication, reachable by other local applications. This can
   expose the user's exit IP and proxy credentials. Fulbr must authenticate its local
   inbound and bind to loopback.
4. **Trust is not implied by "checked".** The repo's automated tests check availability,
   latency, and speed. That is not a security audit.
5. **Mirrors and proxies.** The repo recommends third-party RAW mirrors. Each mirror is an
   additional supply-chain dependency. Fulbr should fetch from the user-selected URL only,
   validate the TLS certificate, and record the final URL and content hash.

**Fulbr rule:** do not hard-code a single mirror or automatically fall back to an
unverified mirror when TLS fails.

---

## 3. Configuration trust model

Treat every downloaded configuration as **untrusted input**:

- Parse with a strict, memory-bounded parser. Reject unknown schemes, malformed
  Base64/percent-encoding, NUL bytes, path traversal, and oversized fields.
- Never execute scripts, shell commands, batch files, `.ps1`, or embedded JSON routing.
- Never download or run binaries referenced by a configuration.
- Never accept `file://`, `javascript:`, `data:`, or local paths as subscription URLs.
- Allow only `https://` by default. `http://` requires an explicit warning and is stored
  as an insecure source. Do not send credentials over plaintext without a second warning.
- Limit redirects (e.g., 3), response size (e.g., 5 MiB), and timeouts.
- Do not forward subscription `Authorization` headers across origins after a redirect.
- Verify TLS with the system trust store. Do not offer "ignore certificate errors" in the
  normal UI; if an advanced override exists, it must be per-source, explicit, logged, and
  never global.
- Deduplicate by a canonical fingerprint of protocol + host + port + credentials hash +
  transport parameters. Do not deduplicate by display name.
- Store the raw config and its provenance (source URL, fetch time, ETag/Last-Modified,
  SHA-256, parser version) for audit.
- Keep the last known-good snapshot when a refresh fails.

### Trust indicators

Fulbr will show three independent values, never one "security score":

| Indicator | Inputs | What it does **not** mean |
|-----------|--------|---------------------------|
| **Connectivity** | Handshake/auth/HTTP success, recent failures | Does not mean safe |
| **Performance** | Median latency, jitter, optional throughput | Does not mean safe |
| **Security assessment** | TLS on/off, cert verification, cipher/protocol validity, known-insecure flags, provenance | "Unknown" when evidence is insufficient |

`Security assessment` may be `Good`, `Limited`, `Insecure`, or `Unknown`. `Unknown` is the
default for public endpoints and must be displayed honestly.

---

## 4. Secret storage

- Store subscription credentials, proxy UUIDs/passwords, and private keys with Windows
  DPAPI (`CryptProtectData`, current-user scope) or Windows Credential Manager. Never
  write them in plaintext to `%APPDATA%`.
- The service needs the engine config at run time. Generate it in a protected directory
  (e.g., `%ProgramData%\Fulbr\runtime`), ACL it to the service account and
  Administrators, and delete it on stop.
- Never log full UUIDs, passwords, private keys, `pbk`, `sid`, or full subscription URLs.
  Log a stable hash/prefix instead.
- Never send secrets to any telemetry endpoint. Fulbr has no telemetry by default.
- Exported profiles must require an explicit action and a clear warning; default to
  redacted export.

---

## 5. Local attack surface

| Surface | Risk | Mitigation |
|---------|------|------------|
| Local SOCKS/HTTP inbound | Any local process can use the tunnel and learn the exit IP; malware can bypass per-app policy | Require auth, bind loopback, random port/secret, document that same-user malware is out of scope |
| Engine API (Clash API) | Unauthorized status/control | Bind `127.0.0.1`, random secret, no CORS, short-lived |
| Named pipe UI↔service | Impersonation/privilege escalation | Explicit SDDL, verify client PID/user, validate every message, no arbitrary paths/commands |
| WFP filters | Leftover filters can break networking | Named filters, cleanup on stop/uninstall, recovery tool |
| Config file | Secrets on disk | Protected directory, ACL, delete on stop, DPAPI at rest |
| Logs | Browsing history / credentials | Redaction, rotation, user deletion, no destinations by default |
| Update channel | Malicious update | Signed artifacts, SHA-256, separate channels, no config-triggered updates |

**Out of scope (must be stated):** a compromised same-user process, a compromised kernel,
a malicious browser extension with broad permissions, and a hostile operating system can
defeat any user-mode VPN client. Fulbr does not claim to resist them.

---

## 6. Kill switch and failure policy

A kill switch is a **policy enforcement** mechanism, not a promise of anonymity.

Recommended states:

- **Strict:** protected traffic never uses the physical interface while Fulbr is
  connecting, connected, or reconnecting. Direct-routed services may be blocked too.
- **Smart Split:** protected traffic stays blocked; explicitly direct-routed services may
  continue direct, because that is the user's chosen policy.
- **Manual Recovery:** after a failure, the user must approve any change to the routing
  policy. No silent fallback.

Implementation constraints:

- WFP filters must be owned by the service and survive an engine crash (fail closed).
- Filters must permit the engine's connection to the proxy server, DHCP, loopback, and
  explicitly direct-routed destinations.
- A recovery command must remove orphaned filters and restore routes/DNS.
- The UI must show "Protected", "Blocked", "Direct", or "Unknown" based on actual system
  state, not on a timer or a stored boolean.

---

## 7. Split-tunneling risks

- Routing is only as good as the classifier. Domain lists can be stale; CDN IPs are
  shared; ECH/QUIC can hide the domain; `.ru` does not mean "hosted in Russia".
- Direct-routed traffic exposes the user's real IP to that destination and to the local
  network. This is inherent, not a bug. The UI must explain it before enabling Smart Split.
- A routing bug can silently send protected traffic direct. Fulbr must verify the actual
  outbound interface for a sample of destinations and show the applied rule ("Why This
  Route?").
- DNS must follow the same decision as the connection. A direct-routed domain resolved via
  a proxy DNS server can still leak the query; a proxied domain resolved via the ISP DNS
  leaks the destination.
- IPv6 must be explicitly handled. If the tunnel is IPv4-only, block IPv6 for protected
  traffic rather than letting it bypass.

---

## 8. Supply chain and updates

Separate update channels (never mix):

1. **Fulbr application** — signed installer, SHA-256, release notes, rollback.
2. **sing-box engine** — pinned version, official release asset, SHA-256, independent
   update decision, changelog review.
3. **Wintun** — official signed DLL only, SHA-256, license notice.
4. **Rule sets** (Russian domains/IPs) — checksummed, versioned, separately refreshable.
5. **Subscription configs** — data only; refreshing them must never trigger a binary
   update.
6. **Optional DPI module** — separate, disabled by default, explicit consent, full
   removal. Never bundled with the core installer without opt-in.

Do not download executables from config mirrors. Do not auto-update the engine because a
subscription changed. Do not silently add antivirus exclusions.

---

## 9. Privacy defaults

- No ads, no analytics, no third-party tracking, no telemetry by default.
- No browsing-history collection by default. Diagnostics store only aggregate timings and
  error codes; destinations are redacted unless the user opens "Why This Route?" for a
  specific test.
- Network requests are limited to: user-configured subscriptions, explicit update checks,
  rule-set refreshes (daily), and user-initiated diagnostics.
- A visible "Network activity" log lists every outbound request Fulbr makes, with
  destination and purpose. Users can disable automatic checks.
- Crash reports are local-only by default; uploading requires explicit consent.

---

## 10. Claims Fulbr must never make

- "Anonymous" because a proxy is connected.
- "No logs" about a third-party server.
- "DNS leak proof" without a test on the user's machine.
- "Military-grade encryption" or similar marketing.
- "100% secure" or "safe public servers".
- "Per-app tunneling works for every app" without qualification.
- "Verified server" when only reachability was tested.
- "Kill switch active" when only the engine's in-process strict route is running.

---

## 11. Sources

- <https://github.com/igareck/vpn-configs-for-russia> — README warnings, localhost proxy issue, update/test claims, accessed 2026-11-09.
- <https://sing-box.sagernet.org/configuration/inbound/tun/> — `strict_route` DNS-leak note, accessed 2026-11-09.
- <https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page> — WFP, doc updated 2025-03-11, accessed 2026-11-09.
- <https://reqrypt.org/windivert.html> — LGPL/admin/driver risk, accessed 2026-11-09.
- <https://www.happ.su/main/dev-docs/ping> — ICMP/TCP/proxy ping semantics, accessed 2026-11-09.
- <https://github.com/Flowseal/zapret-discord-youtube> — antivirus/WinDivert/service caveats, accessed 2026-11-09.
