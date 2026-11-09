# Third-Party Notices

Fulbr is licensed under GPL-3.0-or-later. This file records the third-party components
that are currently used by the prototype. It must be regenerated from the exact shipped
artifacts before every release.

## Runtime components

| Component | Version | License | Use |
|-----------|---------|---------|-----|
| .NET runtime (self-contained) | 10.0.14 | MIT | Runtime for Fulbr |
| WPF | .NET 10 | MIT | Desktop UI |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM helpers |
| H.NotifyIcon.Wpf | 2.4.1 | MIT | System tray icon |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.14 | MIT | Windows service host |
| System.IO.Pipes.AccessControl | .NET 10 built-in | MIT | Named-pipe ACL (built-in) |

## Not yet bundled

| Component | License | Status |
|-----------|---------|--------|
| WinDivert | LGPL-3.0 / GPL-2.0 dual | Optional future DPI module only |
| Xray-core | MPL-2.0 | Optional future engine |
| Russian rule sets | GPL-3.0 / MIT depending on source | Not bundled yet |

## Bundled engine

| Component | Version | License | Notes |
|-----------|---------|---------|-------|
| sing-box | 1.14.4 | GPL-3.0-or-later + name clause | `engine/sing-box.exe` and `engine-service/sing-box.exe`, unmodified official release; SHA-256 pinned |
| Wintun | 0.14.1 | Prebuilt binary license; source GPL-2.0 | `engine/wintun.dll` and `engine-service/wintun.dll`, official signed DLL, SHA-256 `e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce` |

## Build tools (not redistributed)

Git 2.56.0.4, .NET SDK 10.0.403, Inno Setup 6.7.3, PowerShell, Node.js, Python.

## Attribution

Fulbr is not affiliated with, endorsed by, or sponsored by the sing-box, Xray, WireGuard,
Wintun, Happ, or Zapret projects. Their names and trademarks belong to their respective
owners.

## Source offer

- Fulbr source: this repository, GPL-3.0-or-later.
- sing-box 1.14.4 source: <https://github.com/SagerNet/sing-box/tree/v1.14.4>, GPL-3.0-or-later with the upstream name/association clause.
- The exact bundled engine hash is recorded in `vendor/MANIFEST.json` and `engine/LICENSE-sing-box.txt` is shipped beside the binary.
