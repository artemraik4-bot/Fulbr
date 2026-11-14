# Known Issues and Limitations

**Updated:** 2026-11-14

This file records what does **not** work. Do not convert these items into marketing claims.

## Blockers

| ID | Issue | Impact | Next action |
|---|---|---|---|
| K-31 | TUN adapter, routes, DNS assignment, and cleanup pass, but DNS/HTTPS **through** the TUN timed out. | TUN payload is **not verified**. Fulbr must not claim TUN mode carries traffic. | Repeat in an isolated Windows VM with Happ/xray disabled; inspect `%ProgramData%\Fulbr\logs\engine.log`; test `strict_route=false` only as a diagnostic. |
| K-32 | Two clean Windows 11 26H2 VirtualBox installations stalled at ~42% with zero disk I/O. | No working Windows VM test environment. | Try Windows 10 22H2, Windows 11 24H2/LTSC, or another hypervisor. |
| K-33 | Happ/xray TUN was active during the host TUN test. | Confounds payload/DNS results. | Retest with Happ disabled or in an isolated VM. |

## TUN and security

- Persistent, service-owned **WFP kill switch is not implemented**.
- `strict_route` is engine-process-scoped; it is not fail-closed across an engine crash.
- No engine-crash or service-crash fail-closed test exists.
- No DNS/IPv6 leak test results exist.
- The TUN subnet collision was fixed (`172.19.0.1/30` → `10.255.255.1/30`), but other
  adapter conflicts have not been tested in a clean environment.
- Engine diagnostics may contain connection metadata; do not share logs blindly.

## Service and IPC

- Caller identity falls back to ACL validation in console mode.
- The pipe name is fixed (`Fulbr.Service`); a random per-boot name/token is pending.
- Builtin Users have read/write access; this should be narrowed to the interactive user
  and administrators.
- `start` accepts a share link over local IPC; the service re-parses and validates it.
- No persistent WFP recovery tool exists.

## Product

- Hysteria2/TUIC are rejected in proxy mode (QUIC/UDP probe not implemented).
- The UI has no TUN mode selector or service client yet.
- No scheduled subscription refresh; storage is JSON + DPAPI, not SQLite.
- No lifecycle states, backoff, or quarantine.
- Routing, Smart Split, and Russian direct lists are design-only.
- No speed test, HTTP-through-tunnel check, or DNS/IPv6 leak test.
- The installer is unsigned; an Inno Setup commercial-license question is pending for
  commercial use.
- No code signing, SBOM, or source archive yet.

## Release status

- Proxy mode (0.2.0) is implemented and tested: 25/25 unit tests pass.
- Service/TUN code (0.3.0) is built but **payload-verified only partially**.
- Do not describe Fulbr as a working system-wide VPN or as leak-protected.
