# Architecture

**Fulbr** is a Windows 10/11 x64 VPN/proxy client. This is the short, current-state
architecture. Detailed design documents are linked below.

**Status:** 0.2.0 proxy mode is implemented and installed. 0.3.0 service/TUN code is
built; TUN adapter/routes/DNS/cleanup are verified, TUN **payload** traffic is not.
See [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) and
[`KNOWN_ISSUES.md`](docs/handoff/KNOWN_ISSUES.md).

---

## 1. Components

```
Fulbr.exe            WPF UI, .NET 10, x64, unprivileged
  |  named pipe "Fulbr.Service" (typed commands only)
Fulbr.Service.exe    Windows service, LocalSystem (service mode; not yet in installer)
  |  child process
sing-box.exe         pinned engine 1.14.4, unmodified
  +-- Wintun.dll     pinned 0.14.1, TUN adapter "Fulbr"
```

| Project | Role |
|---|---|
| `src/Fulbr.App` | WPF UI, view models, local proxy orchestration, DPAPI storage |
| `src/Fulbr.Core` | Parsers, models, config builders, probes, ranking, logging |
| `src/Fulbr.Service` | Privileged host, named-pipe IPC, engine supervision, TUN lifecycle |
| `tests/Fulbr.Core.Tests` | Parser/config/ranking/probe unit tests (25 tests) |
| `installer/Fulbr.iss` | Inno Setup per-user installer (proxy mode) |

The UI never edits routes, DNS, adapters, or WFP directly. Privileged work belongs to
`Fulbr.Service`; the UI only sends the validated commands `ping`, `status`, `start`,
`stop`, and `recover` over the pipe.

## 2. Modes

- **Proxy mode (implemented, 0.2.0).** The UI starts an authenticated local SOCKS/HTTP
  proxy (`sing-box`) and verifies HTTP through it before showing "Connected". The system
  network stack is untouched.
- **TUN mode (0.3.0, partial).** The service starts a second engine with a Wintun adapter,
  default routes, and DNS. Adapter, routes, DNS assignment, stop, and cleanup are verified.
  **Payload DNS/HTTPS through the tunnel timed out and is not verified.**
- **Split tunneling / kill switch / leak protection (planned).** Design only; no claim of
  working functionality.

## 3. Data and secrets

- Server credentials and subscription URLs are stored with Windows DPAPI.
- Logs are redacted; secrets are never logged.
- No telemetry and no analytics.
- Settings live under `%LOCALAPPDATA%\Fulbr`; engine diagnostics under
  `%ProgramData%\Fulbr\logs\engine.log` (local only, bounded).

## 4. Engine handling

- `sing-box` is pinned by version and SHA-256 in [`vendor/MANIFEST.json`](vendor/MANIFEST.json).
- Two isolated copies exist so service cleanup cannot kill the UI proxy engine:
  `artifacts/app/engine/sing-box.exe` and
  `artifacts/service/engine-service/sing-box.exe`.
- Binaries are not committed. `tools/fetch-vendor.ps1` downloads and verifies them.
- Every generated TUN config is validated with `sing-box check` before it is started.

## 5. Service IPC

- Pipe name: `Fulbr.Service`.
- ACL: SYSTEM/Administrators full control, Builtin Users read/write.
- 32 KiB maximum request; one command per connection; no arbitrary paths, commands, or
  raw config accepted.
- `start` accepts only a share link; the service re-parses it, rejects insecure TLS, and
  builds the config itself.

Details: [`docs/architecture/service-ipc.md`](docs/architecture/service-ipc.md).

## 6. Build and release

```powershell
powershell -File tools/fetch-vendor.ps1          # pinned engine + Wintun
dotnet restore --locked-mode
dotnet build -c Release
dotnet test  -c Release
powershell -File tools/build-release.ps1         # UI publish + Inno Setup installer
powershell -File tools/build-service.ps1         # service payload
```

CI (`.github/workflows/ci.yml`) restores, builds, tests, checks vulnerable packages,
publishes the self-contained app, and uploads build artifacts.

## 7. Security boundaries and honest limits

- UI is unprivileged; the service is narrow and allow-listed.
- Same-user malware and kernel compromise are out of scope and documented.
- `strict_route` is engine-scoped; there is **no persistent WFP kill switch** yet.
- No DNS/IPv6 leak test results exist yet.
- TUN payload is not verified; do not describe Fulbr as a working system-wide VPN.

Details: [`docs/security/threat-model.md`](docs/security/threat-model.md),
[`docs/security/wfp-killswitch-design.md`](docs/security/wfp-killswitch-design.md),
[`docs/architecture/overview.md`](docs/architecture/overview.md).
