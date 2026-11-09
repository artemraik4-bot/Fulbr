# Fulbr — Research Overview

**Status:** Phase 1 research (initial pass)
**Date accessed:** 2026-11-09
**Author:** Fulbr engineering (research notes)
**Project name:** Fulbr (fixed by project owner)
**Target platform:** Windows 10/11 x64

---

## 1. Purpose and scope

This document records the evidence behind the first architectural decisions for Fulbr:
a privacy-focused Windows VPN/proxy client with real connectivity diagnostics, subscription
management, smart split tunneling for Russian services, and honest security reporting.

This is **research, not a product claim**. No Fulbr application code has been written yet.
No benchmark in this repository has been measured yet. No connection test has been run yet.

### Explicit limitations

- Internet research was available. Sources are cited with access date.
- We did **not** execute any downloaded VPN configuration, script, or binary.
- We did **not** reverse engineer Happ or extract its configs, credentials, or tokens.
  We inspected only installed file names, sizes, and public version metadata.
- We did **not** run throughput, latency, or leak tests. Any numbers in the UI later must
  come from the user's machine, not from this document.
- GitHub metadata changes continuously; values are snapshots from 2026-11-09.
- A VPN server that accepts a connection is **not** thereby proven private or trustworthy.

---

## 2. Headline findings

| # | Finding | Evidence | Engineering implication |
|---|---------|----------|--------------------------|
| F1 | The reference config repository is GPL-3.0 and updates every 2–4 hours. | GitHub API + README, accessed 2026-11-09: <https://github.com/igareck/vpn-configs-for-russia> | Treat it as an optional external source. Do not make Fulbr depend on it. Keep attribution and license notices. Do not poll faster than upstream updates. |
| F2 | The repository publishes several incompatible formats: plain share-links, Base64, Clash/Mihomo YAML, and client-specific JSON bundles with embedded routing/health-check logic. | README, accessed 2026-11-09 | Fulbr must parse **proxy entries only** from a small allow-list of formats. Never execute embedded scripts or import client-specific routing as code. |
| F3 | `sing-box` supports all protocols required here (VLESS/Reality, VMess, Trojan, Shadowsocks, Hysteria2, TUIC) plus TUN, process-based routing, DNS rules, and a Windows `strict_route` mode. | <https://sing-box.sagernet.org/configuration/outbound/> and TUN docs, accessed 2026-11-09 | `sing-box` is the strongest default networking engine. |
| F4 | `sing-box` is GPL-3.0-or-later **with an additional name/association clause**. | <https://raw.githubusercontent.com/SagerNet/sing-box/testing/LICENSE>, accessed 2026-11-09 | Fulbr must be GPL-3.0-compatible and must not imply endorsement by or association with sing-box. |
| F5 | Xray-core is MPL-2.0 and has no built-in cross-platform TUN; Happ officially describes itself as built on Xray, but its installed bundle also contains sing-box, tun2proxy, and a Mullvad split-tunnel driver. | <https://api.github.com/repos/XTLS/Xray-core>; <https://www.happ.su/main>; local file metadata, accessed 2026-11-09 | Xray is a viable optional engine, not the best sole engine for Fulbr. Do not copy Happ assets or code. |
| F6 | Wintun prebuilt DLLs may be redistributed only alongside software that uses the documented API, and may not be modified or reverse engineered. Source is GPL-2.0. | <https://www.wintun.net/> and bundled `LICENSE-Wintun`, accessed 2026-11-09 | Bundle the official signed `wintun.dll` unmodified with its license. Verify SHA-256. |
| F7 | WinDivert is LGPL, requires administrator rights, and is a kernel-level packet interception driver. | <https://reqrypt.org/windivert.html>, accessed 2026-11-09 | Optional DPI module only, isolated process, explicit consent, never bundled silently. |
| F8 | Windows Filtering Platform (WFP) is the supported user-mode/kernel-mode filtering API and supports application-based policy. | <https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page> (doc updated 2025-03-11), accessed 2026-11-09 | Use WFP for a real kill switch and for verifying/controlling routing. Do not disable adapters as a kill switch. |
| F9 | Windows 11 `.NET 8` and `.NET 9` reach end of support on 2026-11-10; `.NET 10` is active LTS until 2028-11-14. | <https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json>, accessed 2026-11-09 | Build on .NET 10 LTS. Do not start a new 2026 product on .NET 8/9. |
| F10 | The Windows machine used for development has **no Git, Rust, Go, .NET, C++ build tools, or installer tooling** installed; only Node.js 26.13 and Python 3.14 are present. `winget` is available. | Local inspection, 2026-11-09 | A toolchain install requires explicit owner approval (see `docs/development/environment.md`). |

---

## 3. The two reference repositories

### 3.1 `igareck/vpn-configs-for-russia`

Snapshot (GitHub API, 2026-11-09):

- Stars: 9,512; forks: 468; default branch: `main`
- License: **GPL-3.0**
- Pushed: 2026-11-09T17:12:29Z
- Repository size: ~258 MB
- Advertised update cadence: every 2–4 hours, with automated availability/latency/speed
  checks on a server located in Russia; slow/dead configs removed.

What it actually contains (README):

- Standard share-link subscriptions (VLESS, Trojan, Shadowsocks, Hysteria2, VMess, TUIC, etc.)
- Base64-encoded proxy lists
- Clash/Mihomo YAML exports with built-in health-check groups
- Client-specific JSON exports for v2rayN, v2rayNG, Streisand, Happ, V2Box, v2RayTun
- Separate "black list" (blocked in Russia) and "white list" categories
- Tor bridge collections
- Mirrors on GitLab, Codeberg, Gitea, SourceHut, Bitbucket, GitHack, and a
  Yandex+Bitbucket RAW proxy

Important, non-obvious facts:

1. The repo's authors explicitly warn that public configs are unstable and that
   availability varies by time of day and ISP.
2. The repo's client-specific JSON may include routing and health-check directives.
   That is executable behavior in a config-shaped file. Fulbr must not import it as-is.
3. The README documents a real localhost-proxy risk: unauthenticated local SOCKS/HTTP
   inbounds can be reached by other local applications. Fulbr's local inbound must
   require authentication or be inaccessible to other users/processes.
4. The repo is a *source of candidate endpoints*, not a trust authority. Popularity is
   not a security signal.

**Decision:** ship it as a **disabled-by-default, user-addable subscription preset**.
Parse only standard/Base64 proxy lists. Mark every entry `Trust: Unknown` unless the
user explicitly marks a source as personally trusted.

### 3.2 `Flowseal/zapret-discord-youtube`

Snapshot (GitHub API, 2026-11-09):

- Stars: 35,923; forks: 2,727; default branch: `main`
- Pushed: 2026-11-04T08:56:59Z
- README states MIT; `LICENSE.txt` is MIT (verified). GitHub API reports
  `NOASSERTION` because the repo also carries third-party binaries and notices.
- Windows integration is a set of `.bat` scripts plus `winws.exe` and WinDivert.
- Requires administrator rights; installs a Windows service for autostart.
- Antivirus false positives are explicitly documented; the project recommends
  exclusions. **Fulbr will never add antivirus exclusions automatically.**

**Decision:** do not bundle or auto-run Zapret. If a DPI compatibility module is ever
shipped, it must be a separate optional component, disabled by default, with explicit
consent, checksum verification, full removal, and a clear statement that it is not a
VPN and does not provide confidentiality.

---

## 4. Protocol and engine findings

`sing-box` stable v1.14.4 (published 2026-10-24) provides Windows amd64 builds.
Its outbound types include:

`direct`, `block`, `socks`, `http`, `shadowsocks`, `vmess`, `trojan`, `wireguard`,
`hysteria`, `vless`, `shadowtls`, `tuic`, `hysteria2`, `anytls`, `snell`, `ssh`, `tor`,
`dns`, `selector`, `urltest`, `naive`.

This covers every protocol family named in the project brief. It also supports:

- TUN inbound on Windows via Wintun
- `auto_route`, `strict_route`, `route_address` / `route_exclude_address`
- Domain, domain-suffix, keyword, regex, geosite, geoip, CIDR, port, process-name,
  process-path, and process-path-regex matching
- DNS hijacking and DNS routing
- Rule sets (`.srs`) and remote rule-set downloads
- A local Clash-compatible API for status, traffic, and real per-outbound delay tests

Critical Windows caveat from the sing-box TUN documentation:

- `strict_route` on Windows blocks port 53 traffic going through interfaces other than
  the TUN, which helps prevent DNS leaks caused by Windows multihomed DNS behavior.
- It "may prevent some Windows applications (such as VirtualBox) from working properly."
- `strict_route` is not a persistent crash-proof kill switch by itself: it is tied to
  the running engine process. A separate privileged watchdog is required for fail-closed
  behavior after an engine crash.

---

## 5. Russian routing sources

| Source | License | Cadence | Useful categories | Caveat |
|--------|---------|---------|-------------------|--------|
| `runetfreedom/russia-v2ray-rules-dat` | GPL-3.0 | ~6 h | `category-ru`, `ru-blocked`, `re-filter`, `ru-whitelist`, ASN groups | `ru-blocked-all` is huge (~700k domains); not a default. |
| `1andrevich/Re-filter-lists` | MIT | continuous releases | blocked domains/IPs, community lists, `.srs` for sing-box | Built from RKN/community data; not a statement of legality or safety. |
| `v2fly/domain-list-community` | MIT (verify before redistribution) | periodic | `category-ru`, service categories | Domain lists cannot capture every CDN or IP-only service. |
| `Yularzhi/geosite_russia` | verify | daily | `category-ru`, `ru-blocked` | Community-maintained; smaller ecosystem. |

**Decision:** start with a **pinned, checksummed** copy of a small `category-ru` rule set
plus a user-editable direct list. Do not default to `ru-blocked-all`. Refresh rule sets
on a daily schedule, not continuously. Always display rule-set provenance and version.

Key correctness warning from the sources themselves: a Russian website can be hosted on
a foreign CDN, and a foreign service can use Russian infrastructure. `.ru` is not a
routing proof. Fulbr must show *why* a route was chosen and never claim perfect
classification.

---

## 6. Happ as a UX reference (not a code source)

Public facts:

- Installed locally: Happ 4.3.0(610), publisher FlyFrog LLC.
- Official site says Happ is built on the Xray core and supports VLESS (Reality), VMess,
  Trojan, Shadowsocks, Socks, Hysteria2.
- Official ping documentation distinguishes **ICMP**, **TCP**, and **via proxy** ping,
  and explains that TCP ping can terminate at a CDN and therefore understate the real
  path. This is exactly the distinction Fulbr must preserve.
- Local file metadata (no content inspection) shows bundled `xray.exe`, `sing-box.exe`,
  `wintun.dll`, `tun2proxy`, a Mullvad split-tunnel driver, and Qt 6/QML UI libraries.

What Fulbr will borrow conceptually: clear server list, subscription management,
per-server ping, best-server selection, routing profiles, tray-first operation.

What Fulbr will **not** do: copy Happ's QML/assets/branding, read its private configs,
extract credentials, or reverse engineer proprietary code.

---

## 7. Decisions made in Phase 1

1. **Product name:** Fulbr.
2. **Legal identity:** GPL-3.0-or-later, because the default engine is sing-box and the
   project must be redistributable as a whole. Dependency licenses will be re-audited
   before any release.
3. **Engine:** sing-box v1.14.x, bundled as a separately versioned component, invoked as
   a child process, never linked as a library into the UI process.
4. **UI:** C#/.NET 10 LTS + WPF, self-contained x64 build, original Fluent-style design.
5. **Privilege model:** unprivileged UI + narrowly scoped Windows service for TUN,
   routes, WFP and engine lifecycle. No admin requirement for browsing settings/logs.
6. **Installer:** Inno Setup 6 (per-machine install, Start Menu + optional desktop
   shortcut, proper uninstaller, clean update path).
7. **Config sources:** opt-in subscriptions only; parse a strict allow-list; validate,
   deduplicate, cache, and show provenance. Never execute config-embedded logic.
8. **Routing:** sing-box route rules + rule sets; deterministic precedence documented in
   `docs/architecture/overview.md`. Split tunneling is a first-class, tested feature.
9. **Security posture:** trust is never inferred from latency or reachability. Three
   separate indicators: connectivity, performance, security assessment.
10. **No fake features:** any UI state shown before the tunnel and routing checks pass is
    explicitly labeled as "connecting/unknown", never "connected".

---

## 8. Open questions / required verification

These must be answered with experiments on this machine or a VM before release:

- Does `strict_route` on this Windows build coexist with VirtualBox, Hyper-V, Docker,
  and the installed antivirus? (sing-box docs warn about VirtualBox.)
- Does WFP-based fail-closed filtering behave correctly across sleep/resume and
  Wi-Fi/Ethernet transitions?
- Which process-detection races exist on Windows for short-lived processes
  (see upstream sing-box issue #2823)?
- Can per-app routing reliably identify UWP/system processes, or only Win32 images?
- How much does a 700k-domain rule set cost in RAM and startup time? (Likely too much;
  measure before enabling.)
- Which DoH/DoT resolvers are reachable from the user's ISP, and what is the fallback
  policy when none are?
- How should Fulbr behave in Russian "white list" environments where foreign DNS is
  blocked and only provider/Yandex DNS works? This is a policy decision that must be
  explicit, not silent.

---

## 9. Source log (accessed 2026-11-09 unless noted)

- <https://github.com/igareck/vpn-configs-for-russia> — README, GitHub API metadata.
- <https://github.com/Flowseal/zapret-discord-youtube> — README, `LICENSE.txt`, API metadata.
- <https://github.com/SagerNet/sing-box> — repository, `LICENSE`, release metadata.
- <https://sing-box.sagernet.org/configuration/inbound/tun/> — TUN, `strict_route`, Windows notes.
- <https://sing-box.sagernet.org/configuration/route/rule/> — process matching fields.
- <https://sing-box.sagernet.org/configuration/outbound/> — outbound protocol list.
- <https://github.com/XTLS/Xray-core> — README, MPL-2.0 license, release metadata.
- <https://github.com/WireGuard/wireguard-windows> — MIT license, release metadata.
- <https://www.wintun.net/> — Wintun 0.14.1, GPL-2.0 source, prebuilt license.
- <https://reqrypt.org/windivert.html> — WinDivert 2.2.2, LGPL, admin requirement.
- <https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page> — WFP overview, doc updated 2025-03-11.
- <https://github.com/runetfreedom/russia-v2ray-rules-dat> — Russian geo rules, 6-hour cadence.
- <https://github.com/1andrevich/Re-filter-lists> — MIT, `.srs` rule sets.
- <https://github.com/v2fly/domain-list-community> — domain-list format and categories.
- <https://www.happ.su/main> and `/main/dev-docs/ping` — public product and ping documentation.
- <https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json> — .NET support dates.
- Local machine inspection — Windows build, installed tools, Happ file metadata.
