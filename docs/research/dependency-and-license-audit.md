# Dependency and License Audit (Initial)

**Status:** Phase 1 audit; must be re-run before every release
**Date accessed:** 2026-11-09
**Project license decision:** GPL-3.0-or-later

This is a planning audit, not legal advice. Each version must be pinned and its license
re-verified from the exact artifact that ships.

---

## 1. License decision

Fulbr will be distributed under **GNU GPL v3.0 or later** because:

- the default networking engine, sing-box, is GPL-3.0-or-later;
- bundling and distributing sing-box with Fulbr creates copyleft obligations for the
  combined distribution;
- GPL-3.0 is compatible with the MIT/BSD/Apache-2.0 components listed below;
- the project is intended to be fully open source and community-maintained.

**Non-negotiable:** Fulbr must not imply endorsement by or association with the sing-box
project. sing-box's license adds: *"no derivative work may use the name or imply
association with this application without prior consent."* Fulbr's name is independent.

We will include:

- the full GPL-3.0 text as `LICENSE`;
- the exact sing-box `LICENSE` and source-offer information;
- Wintun's prebuilt-binary license and GPL-2.0 source notice;
- all third-party notices in `THIRD-PARTY-NOTICES.md`;
- build scripts that produce a corresponding-source archive for the exact release.

---

## 2. Runtime components (planned)

| Component | Planned version | License | Redistribution notes | Status |
|-----------|-----------------|---------|----------------------|--------|
| .NET runtime (self-contained) | 10.0.14 | MIT | Include copyright/license; self-contained publish allowed. | Approved |
| WPF | part of .NET 10 | MIT | Same as .NET. | Approved |
| **H.NotifyIcon.Wpf** | 2.4.1 | MIT (GitHub API verified) | Include notice. Replaces Hardcodet (CPOL risk). | Approved |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Include notice. | Approved |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.14 | MIT | Service host. | Approved |
| **Inno Setup** | 6.7.3 | Inno Setup License (SPDX: InnoSetup) | The license permits use including commercial applications. Since 6.5.0 the project *requests* that commercial users buy a license; without a key the compiler prints "Non-commercial use only". Fulbr is non-commercial/open-source. | Approved for non-commercial use; review before any commercial distribution |
| **sing-box** | 1.14.4 (pin) | **GPL-3.0-or-later + name clause** | Ship license, source offer, unmodified release binary; no implied association. | Bundled in 0.2.0; SHA-256 pinned |
| **Wintun** (`wintun.dll`) | 0.14.1 | Prebuilt: permissive, no modification, redistribution only alongside software using the API; source GPL-2.0 | Ship unmodified signed DLL + license + SHA-256. | Bundled in 0.3.0 service payload; SHA-256 pinned |
| **WinDivert** (optional DPI module only) | 2.2.2 | LGPL-3.0 or GPL-2.0 (dual) | Separate process, dynamic link, full license/source offer, disabled by default. Not in core installer. | Deferred |
| Rule set: `runetfreedom/russia-v2ray-rules-dat` | pinned snapshot | GPL-3.0 | Compatible with Fulbr GPL-3.0; attribute and ship license. | Approved for optional preset |
| Rule set: `1andrevich/Re-filter-lists` | pinned snapshot | MIT | Include notice. | Approved for optional preset |
| Rule set: `v2fly/domain-list-community` | pinned snapshot | MIT (API verified) | Include notice. | Approved for optional preset |
| Xray-core (optional second engine) | not in v1 | MPL-2.0 | File-level copyleft; needs a separate integration audit. | Deferred |
| Happ | not used | Proprietary | No code/assets copied. Public UX ideas only. | Excluded |
| Zapret / `zapret-discord-youtube` | not bundled | MIT (LICENSE.txt verified) + third-party binaries | Optional external integration only; never auto-run BAT files. | Deferred |

---

## 3. Critical audit findings

### 3.1 Hardcodet.NotifyIcon.Wpf — avoid

The commonly recommended `Hardcodet.NotifyIcon.Wpf` package is licensed under the
**Code Project Open License (CPOL)**, which is not an OSI-approved open-source license and
has terms that conflict with GPL redistribution. **Do not use it.** Use
`H.NotifyIcon.Wpf` (MIT) instead. This is exactly the kind of trap a license audit is for.

### 3.2 sing-box name clause

GPL-3.0 is compatible with Fulbr's GPL-3.0, but the additional clause restricts use of the
sing-box name. Fulbr will:

- describe the engine factually as "includes sing-box 1.14.4 (GPL-3.0)";
- not use "sing-box" in the product name, logo, installer branding, or endorsement text;
- not modify and rebrand sing-box as Fulbr.

### 3.3 Wintun prebuilt license

The prebuilt signed DLL is **not** GPL-2.0; the source is GPL-2.0, but the binary has a
separate restrictive-but-redistributable license. Key obligations:

- do not modify or reverse engineer the DLL;
- distribute it only alongside software that uses the documented API;
- keep proprietary notices;
- do not use WireGuard/Wintun names to endorse Fulbr.

We will ship the official signed `wintun.dll` unmodified and record its SHA-256.

### 3.4 Mihomo/Clash Meta license inconsistency

GitHub API reports `MIT` for `MetaCubeX/mihomo`, but the repository `LICENSE` file on the
`Alpha` branch is GPL-3.0. **Do not rely on GitHub's license badge.** Mihomo is not in v1.
If it is ever considered, audit the exact release tag.

### 3.5 Flowseal/Zapret

`LICENSE.txt` is MIT and includes third-party binary notices. GitHub API reports
`NOASSERTION`. The project bundles WinDivert and upstream Zapret binaries. Fulbr will not
redistribute those binaries without a file-by-file audit and explicit user consent.

### 3.6 Rule-set licensing

Russian routing lists are data, but they are still copyrighted compilations. Some are
GPL-3.0. Distributing a pinned snapshot inside a GPL-3.0 app is compatible; distributing
it under a permissive app would not be. This reinforces the GPL-3.0 choice.

---

## 4. Build-time tools (not redistributed with the app)

| Tool | Version | License | Notes |
|------|---------|---------|-------|
| .NET SDK | 10.0.403 | MIT | Build only. |
| Git for Windows | 2.56.0.4 | GPL-2.0 | Build only; not bundled. |
| Inno Setup | 6.7.3 | Inno Setup License | Produces the installer; see the note above. |
| PowerShell | built-in | MIT | Build scripts. |
| Python | 3.14.10 (installed) | PSF-2.0 | Optional helper scripts; not bundled. |
| Node.js | 26.13.0 (installed) | MIT | Optional tooling; not bundled. |

No Rust, Go, MSVC, or CMake is required for v1. If the engine is built from source later,
the Go toolchain and its notices become build-time dependencies.

---

## 5. Reproducibility and verification

Before release:

1. Pin every NuGet package with `PackageReference` + `packages.lock.json`.
2. Pin the sing-box asset by version and SHA-256.
3. Pin `wintun.dll` by version and SHA-256.
4. Record the exact .NET SDK band and runtime version.
5. Build with `--no-restore` after a locked restore.
6. Produce an SBOM (CycloneDX or SPDX) for the installer payload.
7. Run `dotnet list package --vulnerable --include-transitive` and a license scan in CI.
8. Sign `Fulbr.exe` and `Fulbr-Setup.exe` when a certificate is available; document the
   unsigned-build caveat until then.
9. Publish the corresponding source archive for the GPL components.

---

## 6. Open license questions

- Exact transitive notices for the final published payload.
- Whether any UI icon/font asset requires attribution.
- Inno Setup redistribution wording for a bundled third-party GPL application (review the
  Inno Setup license text at release time).
- Distribution of rule-set snapshots: confirm the upstream license of each pinned file,
  not just the repository README.
- If code signing is added, the certificate vendor's terms and timestamping requirements.

---

## 7. Sources

- <https://github.com/SagerNet/sing-box/blob/testing/LICENSE> — GPL-3.0 + name clause, accessed 2026-11-09.
- <https://www.wintun.net/> and bundled `LICENSE-Wintun` — prebuilt license, accessed 2026-11-09.
- <https://reqrypt.org/windivert.html> and `basil00/Divert` `LICENSE` — LGPL-3.0/GPL-2.0 dual, accessed 2026-11-09.
- <https://github.com/lepoco/wpfui> — MIT, accessed 2026-11-09.
- <https://github.com/HavenDV/H.NotifyIcon> — MIT, accessed 2026-11-09.
- <https://github.com/XTLS/Xray-core> — MPL-2.0, accessed 2026-11-09.
- <https://github.com/runetfreedom/russia-v2ray-rules-dat> — GPL-3.0, accessed 2026-11-09.
- <https://github.com/1andrevich/Re-filter-lists> — MIT, accessed 2026-11-09.
- <https://github.com/v2fly/domain-list-community> — MIT, accessed 2026-11-09.
- <https://github.com/Flowseal/zapret-discord-youtube/blob/main/LICENSE.txt> — MIT text, accessed 2026-11-09.
- NuGet flat-container API — exact package versions, accessed 2026-11-09.
