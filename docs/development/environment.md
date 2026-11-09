# Development Environment Inspection and Setup Plan

**Status:** Inspection complete; installation **not performed**
**Date:** 2026-11-09
**Host:** Windows 11 Pro, build 26300, x64, i5-11400F, 15.9 GB RAM

---

## 1. Current state (verified)

| Component | Status |
|-----------|--------|
| Windows | Windows 11 Pro, 10.0.26300, x64 |
| WebView2 Runtime | 157.0.4341.42 (not needed for WPF) |
| Node.js | 26.13.0 |
| Python | 3.14.10 |
| winget | 1.29.380 |
| Git | **not installed** |
| .NET SDK / runtime | **not installed** |
| Rust / Cargo | **not installed** |
| Go | **not installed** |
| MSVC / clang / CMake / MSBuild | **not installed** |
| Inno Setup / WiX | **not installed** |
| 7-Zip | 26.03 (installed) |
| VirtualBox | 7.2.20 (useful for isolated testing) |
| FACEIT Anti-Cheat | 2.1 (conflict testing target) |
| Happ | 4.3.0 (UX reference; bundles Xray/sing-box/Wintun) |
| OpenVPN Connect | 3.9.0 |
| Hiddify | 4.1.1 |

The machine is not currently set up to build a Windows desktop application. Only Node,
Python, and 7-Zip are available.

---

## 2. Proposed toolchain (minimum necessary)

The selected stack (C#/.NET 10 WPF + sing-box sidecar + Inno Setup) needs only three
global tools. Rust, Go, MSVC, CMake, and WiX are **not** required.

| Tool | Version | Purpose | Approx. size | Privilege |
|------|---------|---------|--------------|-----------|
| Git for Windows | 2.56.0.4 | version control, build scripts | ~350 MB | per-machine (UAC) |
| .NET SDK 10 | 10.0.403 | build WPF/service, run tests | ~1 GB | per-machine (UAC) |
| Inno Setup 6 | 6.7.3 | produce `Fulbr-Setup.exe` | ~10 MB | per-machine (UAC) |

Optional later (not now): Go toolchain (to build sing-box from source), WiX (enterprise
MSI), Inkscape or ImageMagick (asset conversion).

### Why these exact versions

- .NET 10 is the active LTS (support until 2028-11-14). .NET 8/9 reach end of support on
  2026-11-10. Source: .NET release metadata, accessed 2026-11-09.
- Git 2.56.0.4 and Inno Setup 6.7.3 are the current winget manifest versions at the time
  of inspection. If winget offers a newer patch, prefer the newest patch and record it.
- Inno Setup 7 exists (7.1.1) but 6.x is the mature, widely documented line. We can move
  to 7 after validating the installer script.

---

## 3. Exact proposed commands (require owner approval)

Run in an elevated PowerShell **only after approval**:

```powershell
# 1. Git
winget install --id Git.Git --exact --version 2.56.0.4 `
  --source winget --accept-package-agreements --accept-source-agreements

# 2. .NET SDK 10
winget install --id Microsoft.DotNet.SDK.10 --exact --version 10.0.403 `
  --source winget --accept-package-agreements --accept-source-agreements

# 3. Inno Setup 6
winget install --id JRSoftware.InnoSetup --exact --version 6.7.3 `
  --source winget --accept-package-agreements --accept-source-agreements
```

If a pinned version is no longer available, do **not** silently install an arbitrary
version. Check the winget manifest, record the new version and its publisher, and ask for
approval again.

### Verification after install

```powershell
git --version
dotnet --info
dotnet --list-sdks
# Inno Setup is not on PATH by default; check:
Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

Expected:

- Git 2.56.x
- .NET SDK 10.0.403 (or a newer 10.0.4xx patch)
- `ISCC.exe` present

---

## 4. Engine and driver artifacts (not global installs)

These are downloaded into the repository's `vendor/` directory and verified by SHA-256.
They are not installed system-wide until Fulbr itself is installed.

| Artifact | Version | Source | SHA-256 |
|----------|---------|--------|---------|
| `sing-box-1.14.4-windows-amd64.zip` | 1.14.4 | GitHub release `SagerNet/sing-box` | `99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df` (GitHub asset digest, 2026-11-09) |
| `wintun-0.14.1.zip` | 0.14.1 | <https://www.wintun.net/builds/wintun-0.14.1.zip> | `07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51` (published on wintun.net, 2026-11-09) |

Verification procedure:

1. Download over HTTPS from the official URL.
2. Compute `Get-FileHash -Algorithm SHA256`.
3. Compare with the pinned value from two independent records (release page + manifest in
   this repo).
4. Extract only the required files (`sing-box.exe`, `wintun.dll`, licenses).
5. Record the hash and URL in `vendor/MANIFEST.json`.
6. Never execute a downloaded binary before verification.

The GitHub asset digest is a server-provided value; it is not a signature. Until Fulbr has
its own signed manifest, verify against a pinned hash committed in the repository and
cross-check the official release page.

---

## 5. What will **not** be installed now

- No Rust toolchain, no Go toolchain, no Visual Studio Build Tools.
- No Windows service.
- No TUN adapter.
- No WFP filters.
- No antivirus exclusions.
- No changes to Windows proxy settings.
- No GitHub repository publication.

Those steps happen only after the code builds and tests pass, and after explicit approval.

---

## 6. Rollback plan

If the toolchain causes problems:

```powershell
winget uninstall --id JRSoftware.InnoSetup --exact
winget uninstall --id Microsoft.DotNet.SDK.10 --exact
winget uninstall --id Git.Git --exact
```

Before installing, create a Windows System Restore point (optional but recommended).
Installers may add entries to the system `PATH`; uninstalling normally removes them. If
not, remove the added entries manually and document the change.

No service, driver, route, or DNS change is made by the toolchain installation.

---

## Test environment inventory (2026-11-09)

- VirtualBox 7.2.20 installed.
- Available VM: **Astra Linux only**. No Windows guest.
- No Windows `.iso` found in `C:\` or `Z:\` (depth 2).
- Windows Sandbox binary not present.
- Hyper-V management tools/VMs not available to the current user.

**Consequence:** privileged TUN/service/WFP code can be written and unit-tested, but real
TUN, route, DNS, service, and kill-switch behavior cannot be verified until a Windows 10/11
VM (or explicit host-test approval with safeguards) is available. Do not install the
service, Wintun driver, or WFP filters on the host without a separate decision.

---

## 7. Build/test commands (once installed)

```powershell
# restore with lock file
dotnet restore --locked-mode

# build
dotnet build -c Release --no-restore

# unit tests
dotnet test -c Release --no-build

# publish self-contained x64
dotnet publish src/Fulbr.App/Fulbr.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=false -o artifacts/app

# installer (after publishing)
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' installer\Fulbr.iss
```

Exact project paths will be created in Phase 3/4.

---

## 8. Approval checklist

Please approve **only** the items you are comfortable with:

- [ ] Install Git 2.56.0.4 (winget, UAC)
- [ ] Install .NET SDK 10.0.403 (winget, UAC)
- [ ] Install Inno Setup 6.7.3 (winget, UAC)
- [ ] Download and verify sing-box 1.14.4 + Wintun 0.14.1 into `vendor/`
- [ ] Create a System Restore point first (recommended)

Not requested yet (separate approval later):

- [ ] Register the Fulbr Windows service
- [ ] Create a TUN adapter / install Wintun driver
- [ ] Install and run `Fulbr-Setup.exe` on this PC
- [ ] Publish the repository to GitHub

**No installation has been performed.** The next step waits for explicit approval.
