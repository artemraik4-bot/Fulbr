# Implementation Status

**Last updated:** 2026-11-09
**Honesty rule:** if it is not built and tested, it is not marked done.

---

## Current build (0.2.0)

| Item | Result |
|------|--------|
| Solution | C#/.NET 10 WPF app, core library, service skeleton, test project |
| Build | `dotnet build -c Release` — 0 warnings, 0 errors |
| Tests | **25/25 passing** |
| Service | `Fulbr.Service` built; named-pipe `ping`/`status`/validation smoke-tested |
| TUN config | Generated and validated by pinned `sing-box check` |
| TUN host test | **Partial:** adapter/routes/DNS/cleanup passed; payload traffic and DNS through TUN timed out |
| Wintun | 0.14.1 downloaded, SHA-256 verified, bundled under `engine-service/` |
| TUN runtime | **Not fully verified**; no payload/DNS claim. See `docs/development/tun-host-test-2026-11-10.md` |
| Kill switch | **Not implemented**; `strict_route` only, engine-scoped |
| UI | Runnable, bilingual (EN/RU), light/dark, tray, original logo |
| Subscription import | Real HTTPS fetch + parse |
| Imported sample | 67 servers (47 VLESS, 18 VMess, 2 Trojan), 0 rejected |
| Reachability probes | 67 checked, 50 reachable (TCP/TLS handshake only) |
| **Proxy mode** | **Verified with real VLESS and VMess candidates** |
| Engine | sing-box 1.14.4, official asset SHA-256 verified, bundled under `engine/` |
| HTTP-through-proxy check | Passed before "Connected" was shown |
| Engine RAM | ~39.6 MB working set (one session, observed) |
| Disconnect cleanup | sing-box stopped; runtime config deleted; state logs persisted |
| Secret storage | DPAPI for server credentials and subscription URLs (verified) |
| Installer | 0.2.0 built, installed, and verified (engine bundled) |
| Installer size | 68,526,779 bytes |
| Installer SHA-256 | `5BAA1180E64700C9E19A0B3966FA59B2E01FF13C368B3C727C8ED6A064E4E7E7` |
| Local installation | 0.2.0 per-user install verified; sidebar shows `v0.2.0` |

Screenshots: `docs/images/dashboard.png`, `docs/images/proxy-connected.png`,
`docs/images/servers.png`.

---

## What was verified in the proxy-mode run

- Selected server: **Germany, Nuremberg | [BL]**, VLESS, 259 ms.
- Local endpoint: `http://127.0.0.1:60602` with a random per-session username/password.
- `Connect best (proxy)` tried candidates in order:
  1. Anycast-IP — proxy HTTP check failed,
  2. United States, Chicago — proxy HTTP check failed,
  3. Germany, Nuremberg — **HTTP-through-proxy check passed**.
- Only after the check passed did the UI show "Proxy mode connected".
- Disconnect stopped the sing-box process and removed the runtime config file.
- The UI states clearly: no TUN, no kill switch, no system-wide VPN, DNS not protected.

This is a **proxy**, not a system-wide VPN. Applications must be pointed at the local
endpoint.

---

## Service/TUN work in progress (0.3.0)

- `Fulbr.Service` Windows service host implemented (WindowsServices package).
- Named-pipe IPC with ACL, strict JSON command allow-list, and share-link validation.
- IPC `ping`, `status`, and invalid-link rejection tested in console mode.
- TUN config builder generates `auto_route`, `strict_route`, DoH via proxy, local
  diagnostic inbound, Clash API, and a local bootstrap resolver.
- The generated TUN config passes `sing-box check` with the pinned engine.
- Wintun 0.14.1 is verified and copied beside the service engine.
- **TUN creation/routes/DNS are partially verified on the host**:
  adapter `Fulbr`, default IPv4/IPv6 routes, DNS assignment, and cleanup all passed; payload
  DNS/HTTPS timed out and is **not** verified. See
  `docs/development/tun-host-test-2026-11-10.md`.
- Two clean Windows 11 26H2 VMs stalled at ~42% with no disk I/O and were saved; no Windows
  VM test is currently available. Try a different Windows image/hypervisor before another
  VM run.
- **No persistent WFP kill switch yet**; `strict_route` is engine-scoped.

Do not describe TUN mode as verified or fail-closed until the VM test is complete.

---

## Completed

| Item | Evidence |
|------|----------|
| Environment inspection | `docs/development/environment.md` |
| Toolchain installed and verified | Git 2.56.0.4, .NET SDK 10.0.403, Inno Setup 6.7.3 |
| Research on reference config repository | `docs/research/overview.md` |
| Research on Zapret repository | `docs/research/overview.md` |
| Windows networking architecture | `docs/research/windows-networking.md` |
| Protocol comparison and measurement semantics | `docs/research/protocol-comparison.md` |
| Security/privacy research | `docs/research/security-and-privacy.md` |
| Technology selection | `docs/research/technology-selection.md` |
| Dependency/license audit | `docs/research/dependency-and-license-audit.md` |
| Engine evaluation | `docs/architecture/networking-engine-evaluation.md` |
| Architecture overview | `docs/architecture/overview.md` |
| Threat model | `docs/security/threat-model.md` |
| Public-server risk assessment | `docs/security/public-server-risk-assessment.md` |
| Split-tunnel test plan | `docs/security/split-tunneling-verification.md` |
| DNS/IPv6 leak test plan | `docs/security/dns-ipv6-leak-testing.md` |
| Failure policy | `docs/security/connection-failure-policy.md` |
| Real STUN-free TCP/TLS probes | `EndpointProbe`, method labels, 67-server run |
| Strict share-link parser | VLESS/VMess/Trojan/SS/Hysteria2/TUIC/SOCKS5, tests |
| Subscription fetcher | HTTPS-only, ETag/Last-Modified, redirects, size limits |
| Server ranking | Wilson lower bound + latency/stability/freshness/failure |
| DPAPI storage | Server credentials + subscription URLs encrypted |
| sing-box config builder | VLESS/Reality, Trojan, Shadowsocks, VMess; insecure TLS rejected |
| Engine supervisor | hash check, `sing-box check`, process, port wait, cleanup |
| Proxy mode | real connection + HTTP verification + credential copy |
| WPF UI + tray + themes + i18n | Original design; screenshots |
| Installer (0.1.0) | Inno Setup, per-user, Start Menu/desktop/uninstaller; installed and verified |
| Performance baseline | `docs/performance/benchmarks.md` |

---

## Not implemented (do not claim)

| Item | Status |
|------|--------|
| TUN/Wintun adapter | Not implemented |
| Windows service / privileged boundary | Skeleton only |
| System-wide VPN routing | Not implemented |
| Kill switch / WFP filters | Not implemented |
| Split tunneling enforcement | Not implemented |
| Russian direct routing | Design only |
| DNS leak test / IPv6 leak test | Not implemented |
| Speed test / HTTP-through-tunnel | Not implemented (proxy HTTP check only) |
| Server lifecycle (quarantine/backoff) | Not implemented |
| Scheduled subscription refresh | Not implemented |
| SQLite storage | Not implemented (JSON + DPAPI prototype) |
| Code signing | Not available |
| GitHub publication | Not performed |

---

## Decisions locked

| Decision | Value |
|----------|-------|
| Product name | **Fulbr** |
| License | GPL-3.0-or-later |
| UI | C# / .NET 10 LTS / WPF |
| Engine | sing-box 1.14.4, pinned, separate process |
| TUN (planned) | Wintun 0.14.1, official signed DLL |
| Kill switch (planned) | Service-owned WFP filters, fail closed |
| Privilege model | Unelevated UI + narrow Windows service |
| Installer | Inno Setup 6.7.3 |
| Default subscription interval | 6 hours + jitter (not yet scheduled) |
| Public server trust default | `Unknown` |
| Default split mode (planned) | Smart Split, banking/government direct |
| Telemetry | None by default |

---

## Open approvals required from the owner

1. ~~Upgrade the installed 0.1.0 to the 0.2.0 proxy-mode build.~~ Done and verified.
2. Download Wintun 0.14.1 when TUN work begins.
3. Register the Fulbr Windows service and create a TUN adapter (later, separate approval).
4. Publish the GitHub repository (later, separate approval).
5. Provide a Windows 10/11 VM or ISO for privileged TUN/service/WFP testing (blocker).

---

## Known risks currently unverified

- Proxy-mode DNS behavior is **not** leak-tested. Do not assume DNS privacy.
- `strict_route` interaction with VirtualBox/Hyper-V (TUN not implemented yet).
- WFP filter persistence across service crash.
- Process-identity accuracy for UWP/short-lived processes.
- Real tunnel throughput/latency overhead.
- Availability of Hysteria2/TUIC on the user's ISP.
- DNS behavior under Russian "white list" restrictions.
- Anti-cheat/AV reaction to Wintun/WFP.
- Installer 0.2.0 behavior on a clean VM (not yet run).
