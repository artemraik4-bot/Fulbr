# Changelog

All notable changes to Fulbr are documented here. The project is pre-1.0; interfaces and
storage may change.

## [Unreleased] — 0.3.0 service/TUN

### 2026-11-14

- Published the project publicly at <https://github.com/artemraik4-bot/Fulbr>; CI is green.
  Private creation was blocked by the account's U.S. trade-controls restriction, so the
  owner approved public publication with session history excluded.
- Removed internal handoff/session files and the local toolchain log from all Git history.
- Fixed a flaky TCP probe test (accept/listener race) found by CI.
- Added `tools/fetch-vendor.ps1` for reproducible, SHA-256-verified download of the pinned
  sing-box and Wintun binaries (no large binaries in Git).
- Added root `ARCHITECTURE.md`, `KNOWN_ISSUES.md`, and `tools/fetch-vendor.ps1`; internal
  handoff and session-history copies are intentionally kept out of the public repository.
- CI now fetches pinned vendor binaries, builds and verifies the service payload, and
  packages the installer when Inno Setup is available.
- Removed hard-coded personal paths from development scripts.

### 2026-11-10

- Verified TUN adapter creation, IPv4/IPv6 default routes, DNS assignment, and cleanup in a
  temporary elevated host test (no service/WFP installation).
- Fixed a real TUN subnet collision: `172.19.0.1/30` clashed with Happ/xray; Fulbr now uses
  `10.255.255.1/30` and `fdfe:dcba:9876:1::1/126`.
- Added bounded sing-box debug logging (`%ProgramData%\Fulbr\logs\engine.log`) for TUN
  diagnostics.
- Parameterized the VM test watcher and added `tools/host-tun-test.ps1`.
- TUN payload DNS/HTTPS still times out; **not verified**. See
  `docs/development/tun-host-test-2026-11-10.md`.
- Two clean Windows 11 26H2 VM installs stalled at ~42% with no disk I/O; VMs saved.
- Tests: 25/25 passing; build 0 warnings/0 errors.

### Added

- `Fulbr.Service` Windows service host with ACL'd named-pipe IPC.
- Strict IPC command allow-list: `ping`, `status`, `start`, `stop`, `recover`.
- TUN config builder (`auto_route`, `strict_route`, DoH via proxy, diagnostic inbound,
  Clash API, local bootstrap resolver).
- TUN config validated with the pinned `sing-box check`.
- Wintun 0.14.1 downloaded, SHA-256 verified, and bundled under `engine-service/`.
- Service install/uninstall scripts and VM deployment/test scripts.
- Persistent WFP kill-switch design document.

### Verified

- IPC `ping`, `status`, and invalid-link rejection in console mode.
- Full solution build with 0 warnings; 25/25 tests pass.

### Not yet verified

- TUN adapter creation, routes, DNS, sleep/resume, crash recovery in a Windows VM.
- Persistent service-owned WFP kill switch (design only).
- Service installation on the host (intentionally not performed).

## [0.2.0] — 2026-11-09 — Proxy-mode prototype

### Added

- Pinned sing-box 1.14.4 engine (SHA-256 verified) bundled under `engine/`.
- Real proxy-mode connection: mixed SOCKS/HTTP inbound with per-session authentication,
  no TUN, no routes, no system proxy changes.
- `SingBoxConfigBuilder` for VLESS/REALITY, Trojan, Shadowsocks, and VMess (WS/gRPC/HTTP
  transports).
- Real HTTP-through-proxy verification before the UI reports "Connected".
- "Connect best (proxy)" tries up to five ranked candidates and reports each failure.
- Local proxy endpoint + per-session username + masked password + copy URL.
- Engine hash verification, `sing-box check` validation, orphan cleanup on startup.
- Connection/disconnect logs persisted; runtime config deleted on stop.
- 22 automated tests.

### Security notes

- Proxy mode refuses insecure TLS configurations and VLESS without TLS.
- No kill switch, no DNS leak protection, and no system-wide tunnel in this mode.
- The local proxy credentials are per-session; do not share them.

## [0.1.0] — 2026-11-09 — Prototype

### Added

- Original Fulbr logo (SVG source, PNG sizes, multi-size ICO).
- WPF/.NET 10 desktop shell with light/dark themes, custom chrome, tray icon, and
  English/Russian UI.
- Dashboard, Servers, Diagnostics, Routing, Subscriptions, Security, Settings, and Logs
  pages.
- Strict share-link parser for VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC, and
  SOCKS5; Base64 subscription support; duplicate detection; insecure-cipher rejection.
- Real HTTPS subscription fetching with ETag/Last-Modified, redirect limits, size limits,
  and redacted logging.
- Real TCP-connect and TLS-handshake probes with method labels and explicit
  "not testable without engine" results for QUIC/UDP protocols.
- Server ranking with Wilson lower bound, latency, stability, freshness, and failure
  penalty.
- DPAPI-protected server credentials and subscription URLs.
- Inno Setup installer (`Fulbr-Setup.exe`) with Start Menu, optional desktop shortcut,
  uninstaller, and Installed Apps registration.
- 15 automated core tests; build with zero warnings.
- Research, architecture, threat model, license audit, test plans, and performance
  measurements.

### Known limitations

- No VPN tunnel, TUN adapter, Windows service, kill switch, split tunneling, DNS leak
  test, or engine integration yet.
- Connect button is disabled and labeled as the next phase.
- Routing and Security pages describe the design; they do not enforce it.
- The installer was compiled but not yet installed on the owner's PC.
- No code signing certificate; artifacts are unsigned.
