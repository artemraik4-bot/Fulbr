# Windows Networking Architecture Notes

**Status:** Phase 1 research + Phase 2 design constraints
**Date accessed:** 2026-11-09
**Target:** Windows 10 22H2 / Windows 11 x64 (development machine: Windows 11 Pro build 26300)

---

## 1. What Fulbr must achieve on Windows

Fulbr needs four distinct capabilities that are often conflated:

1. **A tunnel interface** carrying IP packets for selected applications.
2. **Policy routing** deciding which packets enter the tunnel and which use the physical
   adapter (split tunneling).
3. **DNS control** so name resolution follows the same policy as traffic.
4. **Fail-closed enforcement** so protected traffic cannot silently bypass the tunnel
   during a crash, reconnect, or network change.

Windows does not provide a single "VPN API" that does all four. A real client combines a
TUN driver, the routing table, DNS configuration, and the Windows Filtering Platform
(WFP).

---

## 2. TUN adapter: Wintun

**Fact:** Wintun is a minimal layer-3 TUN driver for Windows. Source is GPL-2.0. Prebuilt
signed DLLs are distributed under a separate permissive license that permits
redistribution *only alongside software that uses it through the documented API*, and
forbids modification or reverse engineering. Current version: 0.14.1.
Source: <https://www.wintun.net/>, accessed 2026-11-09.

**Fact:** sing-box uses Wintun on Windows (`wintun.CreateAdapter`, `OpenAdapter`,
`StartSession`) and configures addresses, MTU, routes, and DNS through Windows IP Helper
APIs. Source: `sing-tun/tun_windows.go`, linked from the sing-box TUN documentation,
accessed 2026-11-09.

**Design consequences:**

- Bundle the official, unmodified, signed `wintun.dll` for amd64, with its license text.
- Verify the SHA-256 of the DLL at build time and at first run.
- Never ship a patched Wintun. Never rename it to hide its origin.
- Creating/configuring the adapter requires administrator rights. This is why Fulbr has
  a privileged service.
- Wintun is a layer-3 interface: it carries IP packets, not Ethernet frames. Application
  identity must therefore be recovered from the TCP/UDP flow owner (process), not from a
  per-app virtual adapter.

---

## 3. Routing table behavior

Windows chooses routes by longest-prefix match, then route metric, then interface metric.
A TUN client typically installs two half-default routes (`0.0.0.0/1` and `128.0.0.0/1`)
or a default route with a lower metric, so the physical default route is overridden
without deleting it. sing-box's `auto_route` does this automatically and supports
`route_address` / `route_exclude_address` for custom scopes.

**Design consequences:**

- Use `auto_route` plus explicit exclude routes, not a destructive rewrite of the user's
  routing table.
- Preserve the pre-connection route/interface state so it can be restored exactly.
- Never set the TUN interface metric to a value that fights the user's VPN or Hyper-V
  adapters without explaining the conflict.
- The engine's own connection to the proxy server must be excluded from the tunnel
  (otherwise routing loops). sing-box handles this with `auto_detect_interface` /
  `default_interface` / `bind_interface`; Fulbr must verify the chosen physical interface
  after every network change.
- Direct-routed destinations (Russian services in Smart Split mode) need a route that
  bypasses the TUN. This must be deterministic and visible in diagnostics.

---

## 4. Windows Filtering Platform (WFP)

**Fact:** WFP is the supported filtering platform in Windows Vista and later. It exposes
both user-mode and kernel-mode APIs, supports filtering and modification at several
network-stack layers, and supports application-based policy. Microsoft describes it as
the replacement for TDI/NDIS filter drivers and Winsock LSPs.
Source: <https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page>,
document updated 2025-03-11, accessed 2026-11-09.

**Fact:** sing-box's Windows `strict_route` implementation opens a WFP engine, adds a
high-weight sublayer, permits the engine process's own traffic, and permits traffic on
the TUN interface while blocking other IPv4/IPv6 traffic. This is visible in
`sing-tun/tun_windows.go`, accessed 2026-11-09.

**Design consequences:**

- WFP is the correct mechanism for a kill switch. Disabling network adapters is not.
- A **persistent** kill switch must be owned by the Fulbr service, not only by the engine
  process, because a process-scoped dynamic WFP session disappears when the engine dies.
- Filters must be **narrow and auditable**: permit loopback, DHCP, the engine's proxy
  endpoint, the TUN interface, and explicitly direct-routed destinations; block the rest
  of the protected traffic. Each filter gets a stable name/description.
- Use WFP's application-ID conditions (`ALE_APP_ID`) for per-app policy and for the
  engine's own permit rule. Do not rely only on process name strings.
- WFP filter installation requires administrator rights; the service owns it.
- Every filter must be removed on clean shutdown and on uninstall. A recovery command
  must remove orphaned Fulbr filters even if the UI cannot start.
- Do not use WFP to inspect or modify packet payloads. Fulbr is not a DPI tool.

### Kill-switch states

| State | WFP behavior | User-visible meaning |
|-------|--------------|----------------------|
| Disconnected, kill switch off | No Fulbr block filters | Normal internet |
| Connecting | Block protected traffic immediately, permit engine endpoint | "Connecting — internet blocked" |
| Connected | Permit TUN + engine endpoint + direct rules; block bypass | "Protected" |
| Reconnecting | Keep block filters; permit engine endpoint | "Reconnecting — internet blocked" |
| Engine crash | Service keeps block filters (fail closed) | "Protection lost — traffic blocked" |
| Clean disconnect | Remove filters, restore routes/DNS | "Disconnected" |
| Service crash | Persistent filters remain until watchdog recovery | "Recovery required" |

`Manual recovery` must be an explicit, clearly labeled action that removes Fulbr filters
and restores routes/DNS. It must never happen silently.

---

## 5. DNS behavior and leaks

Windows resolves names through the DNS Client service, which may query multiple
interfaces in parallel (multihomed behavior). This is the root of many "VPN connected but
DNS leaked" reports.

**Fact:** sing-box documents that `strict_route` on Windows blocks port 53 traffic going
through interfaces other than the TUN, specifically to prevent DNS leaks caused by
Windows' ordinary multihomed DNS resolution behavior. It also warns that this may break
some applications such as VirtualBox.
Source: <https://sing-box.sagernet.org/configuration/inbound/tun/>, accessed 2026-11-09.

**Design consequences:**

- Run DNS **inside** the tunnel for protected traffic: sing-box DNS module with
  `hijack-dns`, DoH/DoT upstreams, and cache.
- For direct-routed Russian services, DNS must follow the direct policy. Do not resolve
  a direct-routed domain through a proxy DNS server and then send the connection direct,
  unless that is the user's explicit policy.
- IPv4 and IPv6 must be handled consistently. If the tunnel does not carry IPv6, block
  IPv6 for protected traffic rather than letting it leak.
- Windows 11 per-adapter DoH can override application DNS expectations; Fulbr must detect
  and explain this, not silently fight it.
- Browsers and applications with their own DoH/DoT can bypass OS DNS. Fulbr cannot
  guarantee DNS privacy for those apps. This limitation must be stated in the UI and in
  `docs/security/dns-ipv6-leak-testing.md`.
- DNS cache must be flushed after route/DNS changes (`FlushResolverCache`), and stale
  negative entries must not make a recovered server look dead.

---

## 6. Application identity and split tunneling

**Fact:** sing-box route rules on Windows support `process_name`, `process_path`, and
`process_path_regex`; process information includes the full executable path and user
identity. Source: <https://sing-box.sagernet.org/configuration/route/rule/>, accessed
2026-11-09.

**Design consequences:**

- Per-application routing is feasible for Win32 processes whose executable path is
  visible to the engine.
- It is **not** a universal guarantee: UWP/MSIX processes, services, short-lived
  processes, and processes that proxy traffic for others (browsers, Discord, game
  launchers) may not behave as the user expects. The UI must say this.
- `process_name` alone is spoofable/ambiguous; prefer full path allow-lists, with the
  process name shown only as a convenience.
- Domain-based routing requires DNS sniffing or SNI sniffing. With encrypted ECH/QUIC,
  the domain may not be visible. The fallback is IP-based routing, which is less precise.
- Shared CDN addresses break naive IP lists. A domain that resolves to a CDN IP shared
  with a blocked service cannot be routed correctly by IP alone. Fulbr must show the
  applied rule and admit when classification is uncertain.

---

## 7. Network changes, sleep, and multiple adapters

Required notifications/behaviors (design; to be validated on this machine):

- `NotifyIpInterfaceChange` / `NotifyUnicastIpAddressChange` — interface and address changes.
- `NotifyNetworkConnectivityHintChange` — connectivity/cost changes.
- `WM_POWERBROADCAST` / `PowerRegisterSuspendResumeNotification` — sleep/resume.
- Service control handler — stop/shutdown.
- Wi-Fi/Ethernet transitions must trigger: re-detect the physical interface, re-bind the
  engine's outbound, verify routes, verify DNS, and re-verify WFP permits.
- On resume, do not assume the tunnel survived. Enter `Reconnecting`, keep fail-closed
  filters, and re-test the tunnel end-to-end.
- If the local internet is down, do **not** mark every server as dead. Health checks must
  distinguish "no local connectivity" from "server failed".

---

## 8. Proxy modes and Windows settings

Two distinct modes:

1. **TUN mode** — system-wide IP routing. Requires the service and Wintun.
2. **Local proxy mode** — sing-box exposes an authenticated SOCKS5/HTTP inbound on
   loopback. Applications opt in. No TUN, no admin for the proxy itself.

Fulbr must not silently change the Windows system proxy. If the user enables
"set system proxy", Fulbr must:

- Read and persist the previous WinINET/Internet Settings values.
- Change only what is needed, with explicit consent.
- Restore the previous values on disconnect, crash recovery, and uninstall.
- Detect conflicting proxy managers and warn instead of overwriting them.

The local inbound must require authentication (username/password or an unguessable token)
and bind only to `127.0.0.1`/`::1` by default. This directly addresses the localhost-proxy
risk documented in the reference repository's README.

---

## 9. Privilege boundary

**UI process (unelevated):**
- Rendering, settings, logs, subscription editing, diagnostics display.
- Talks to the service over a named pipe with an explicit security descriptor.
- Never holds the engine admin token, never edits routes/WFP directly.

**Fulbr service (LocalSystem or a dedicated low-privilege service account):**
- Starts/stops the engine.
- Owns Wintun, routes, DNS, WFP filters.
- Validates every command from the UI (schema, bounds, authorization).
- Writes an audit log of privileged actions.
- Exposes only a small command set; no arbitrary command execution, no arbitrary file
  paths, no "run this config" endpoint.

**Engine process (sing-box):**
- Runs as a child of the service, with a restricted token where possible.
- Reads a generated config from a protected directory.
- Listens on loopback for its API with a random per-session secret.
- Is killed and its config deleted on stop.

This boundary is the main defense against privilege escalation and against a compromised
UI process.

---

## 10. What is still unverified

- Whether `strict_route` + WFP filters coexist with the installed VirtualBox 7.2.20 and
  Hyper-V on this machine.
- Whether the WFP filters survive a forced service kill and are recovered by the watchdog.
- Whether per-app routing works for the user's actual applications (Steam, Discord,
  browsers, games with anti-cheat).
- Whether Windows 11 build 26300 changes any DNS/route behavior compared with 22H2/23H2.
- Actual throughput/latency overhead of sing-box + Wintun on this hardware.

These are tracked in `IMPLEMENTATION_STATUS.md` and must be tested in a VM before any
risky change is applied to the host.
