# Technology Selection

**Status:** Phase 1 decision record
**Date accessed:** 2026-11-09
**Decision owner:** Fulbr project owner (approval pending for toolchain installation)

---

## 1. Constraints discovered on the development machine

Local inspection (2026-11-09):

| Item | Result |
|------|--------|
| OS | Windows 11 Pro, build 26300, x64 |
| CPU / RAM | Intel i5-11400F (6C/12T), 15.9 GB |
| GPU | NVIDIA (driver 610.88) |
| Installed runtimes | Node.js 26.13.0, Python 3.14.10, WebView2 Runtime 157.0.4341.42 |
| Missing | Git, Rust, Cargo, Go, .NET SDK, MSVC/clang, CMake, MSBuild, Inno Setup, WiX |
| Package manager | winget 1.29.380 |
| Relevant apps | Happ 4.3.0 (bundles Xray + sing-box + Wintun + tun2proxy), Hiddify, OpenVPN Connect, VirtualBox 7.2.20 |
| Anti-cheat present | FACEIT Anti-Cheat 2.1 (relevant to any packet-interception module) |

Installing a toolchain is a system-level change and requires explicit owner approval.
This document selects the target stack; it does not install it.

---

## 2. Evaluation criteria and weights

Weights reflect the project brief's priorities (security, correctness, Windows fit,
maintainability), not marketing convenience.

| Criterion | Weight | Why |
|-----------|--------|-----|
| Security and privilege isolation | 20 | The UI handles secrets and the service touches routes/WFP. |
| Windows integration | 15 | Tray, service, DPI scaling, shortcuts, installer, network notifications. |
| Networking engine and protocol access | 15 | Must cover VLESS/Reality, VMess, Trojan, SS, Hysteria2, TUIC, TUN, process routing. |
| Runtime performance and resource use | 15 | Idle RAM/CPU, startup, long sessions, throughput. |
| Maintainability and contributor accessibility | 15 | Open-source community, testability, onboarding. |
| Packaging and reproducible deployment | 10 | Native installer, self-contained runtime, clean uninstall. |
| Time to a real, testable MVP | 10 | Must produce a working app, not a mockup. |

Scores: 1 = poor, 5 = excellent. Weighted total = Σ(weight × score), max 500.

---

## 3. Candidate comparison

All candidates assume **sing-box** as the networking engine unless noted, so the engine
score mostly reflects integration quality, not protocol coverage.

| Candidate | Security (20) | Windows (15) | Engine (15) | Perf (15) | Maintain (15) | Packaging (10) | Velocity (10) | Weighted |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **A. C# .NET 10 WPF + service + sing-box sidecar** | 4 | 5 | 5 | 4 | 5 | 5 | 5 | **465 / 500 (93%)** |
| B. Rust + Tauri + sing-box sidecar | 5 | 4 | 5 | 5 | 3 | 4 | 3 | 425 / 500 (85%) |
| C. C++ Win32/WinUI + sing-box | 2 | 5 | 4 | 5 | 2 | 4 | 2 | 340 / 500 (68%) |
| D. Go + Fyne/Wails + sing-box embedded | 4 | 3 | 5 | 4 | 3 | 4 | 3 | 375 / 500 (75%) |
| E. C# WinUI 3 + sing-box | 4 | 5 | 5 | 4 | 4 | 3 | 3 | 410 / 500 (82%) |
| F. Electron + Node + sing-box | 2 | 3 | 5 | 2 | 4 | 4 | 5 | 340 / 500 (68%) |

The scores are engineering judgments, not measured benchmarks. They will be revised after
the Phase 10 measurements.

---

## 4. Selected stack

### 4.1 UI: C# / .NET 10 LTS / WPF

- **Why .NET 10:** active LTS until 2028-11-14. `.NET 8` and `.NET 9` reach end of support
  on 2026-11-10 — one day after this research — so starting a new 2026 product on them
  would be negligent. Source: .NET release metadata, accessed 2026-11-09.
- **Why WPF:** mature Windows desktop stack with first-class DPI scaling, accessibility,
  keyboard navigation, tray integration, and a large contributor pool. It is old but
  stable; the Fluent look is provided by an original design system, not by fake native
  controls.
- **Why not WinUI 3:** WinUI 3 is the newest native stack, but its packaging is tied to
  Windows App SDK / MSIX-style deployment, runtime servicing is more fragile, and the
  installer requirements conflict with a simple per-machine Inno Setup install. WPF gives
  a more predictable self-contained deployment for a community project.
- **Why not Tauri/Rust:** Tauri is excellent for small binaries, but it requires Rust +
  MSVC build tools, a JS frontend, and glue for a Windows service, tray, and WFP. That is
  three skill sets for a Windows-only product. Rust is a strong second choice if the
  project later needs a cross-platform core.
- **Why not C++:** memory-safety risk in the component that parses untrusted configs and
  handles credentials. Not justified when the networking engine is already a separate Go
  process.
- **Why not Go UI:** excellent for the engine, weak for a polished Windows tray/DPI/
  accessibility experience; desktop UI ecosystem is comparatively immature.
- **Why not Electron:** RAM/CPU and attack surface are unacceptable for an always-on
  privacy tool.

### 4.2 Networking engine: sing-box

- Covers every required protocol, including Hysteria2 and TUIC, plus Wintun TUN,
  `auto_route`, `strict_route`, DNS, rule sets, process matching, and a Clash-compatible
  local API.
- Stable v1.14.4 at the time of research; Windows amd64 builds published.
- License: GPL-3.0-or-later with a name/association clause. Fulbr will be GPL-3.0 and
  will not imply endorsement.
- Run as a **separate child process**, not linked as a library. This keeps the privileged
  engine isolated, makes version pinning possible, and limits the blast radius of an engine
  crash.

### 4.3 Privileged component: Fulbr service

- A narrowly scoped Windows service owns Wintun, routes, DNS, WFP, and the engine process.
- The UI runs unelevated and talks over a named pipe with an explicit ACL.
- The service exposes a small command set only. No arbitrary command execution, no
  arbitrary file paths, no "import and run this config" endpoint.
- Installer registers the service; uninstaller stops and removes it.

### 4.4 Installer: Inno Setup 6

- Free, mature, produces a single signed-capable `Fulbr-Setup.exe`.
- Supports per-machine install, Start Menu, optional desktop shortcut, uninstall entry,
  clean upgrades, and custom pages for license/readme.
- MSIX was rejected for v1: signing requirements, container/virtualization quirks, and
  service/driver integration are a poor fit for a VPN client with a kernel-adjacent
  service.
- WiX was rejected for v1: more complex authoring for no user-visible benefit at this
  stage; can be revisited for enterprise MSI needs.

### 4.5 Local data

- SQLite for server inventory, health history, provenance, and audit trail. Use
  `Microsoft.Data.Sqlite` (MIT) with WAL mode and bounded retention.
- JSON for settings and engine config generation; no arbitrary JSON pass-through.
- DPAPI for secrets at rest.

**Prototype note (2026-11-09):** the first runnable build uses a JSON state file with
DPAPI-protected credentials and source URLs. SQLite migration is scheduled for Phase 6/9
once the data model stabilizes.

---

## 5. Rejected combinations and why

| Combination | Why rejected |
|-------------|--------------|
| Xray-core only | No built-in TUN; Hysteria2/TUIC not its focus; would require another TUN layer. |
| Mihomo/Clash Meta only | Strong engine, but its routing/config model is Clash-centric and its license metadata is inconsistent (GitHub API says MIT; repository `LICENSE` is GPL-3.0). Requires a full audit before use. |
| WireGuard only | Excellent VPN, but not a censorship-circumvention protocol and does not cover the required proxy protocols. |
| OpenVPN only | Mature, but not the protocol set in the reference sources and not supported by sing-box. |
| Happ code reuse | Proprietary; explicitly out of scope. Only public UX ideas are used. |
| Zapret integration in core | Kernel packet interception, admin, antivirus/anti-cheat conflicts. Optional separate module only. |

---

## 6. Language policy

Fulbr uses **two languages**, with a clear boundary:

1. **C#** — UI, service, configuration parsing, persistence, diagnostics, installer glue.
2. **Go** — provided by the pinned sing-box binary (not written by Fulbr).

We do **not** introduce Rust or C++ in v1. A second native language would add build,
security-review, and contributor burden without a demonstrated need. If profiling later
shows a hotspot that C# cannot handle, a narrowly scoped native helper can be added with
a written justification and a security review.

---

## 7. Risks of this choice

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| WPF looks dated if not carefully styled | Medium | Medium | Original design system, Fluent resources, DPI tests, light/dark themes, accessibility review. |
| .NET self-contained bundle is large (~70–100 MB) | High | Low | Acceptable for a desktop VPN; trim where safe; document size. |
| P/Invoke bugs in WFP/route code | Medium | High | Isolate in service, unit-test with mocks, integration tests in a VM, no direct P/Invoke from UI. |
| sing-box GPL obligations | Certain | Medium | GPL-3.0 for Fulbr, source availability, license notices, no implied endorsement. |
| sing-box behavior changes across versions | Medium | High | Pin exact version; test before upgrade; separate engine update channel; keep last known-good. |
| Windows service install/UAC friction | Medium | Medium | Clear installer explanation, per-machine install, no admin for normal UI use. |
| Anti-cheat/AV flags on WFP/TUN | Medium | High | No hidden drivers; document; test with FACEIT/Defender; optional module separation. |

---

## 8. Decision summary

**Build Fulbr as a GPL-3.0-or-later Windows application using:**

- C# / .NET 10 LTS / WPF for the UI and service,
- sing-box v1.14.x as a pinned, separately versioned engine process,
- Wintun for the TUN adapter,
- WFP for kill-switch enforcement,
- SQLite for local state,
- Inno Setup 6 for the installer,
- x64 self-contained deployment for Windows 10/11.

**Approval required before:** installing Git, .NET SDK 10, Inno Setup, downloading engine
binaries, registering a Windows service, or creating a TUN adapter. See
`docs/development/environment.md` for the exact proposed commands and rollback.
