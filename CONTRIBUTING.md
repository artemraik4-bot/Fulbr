# Contributing to Fulbr

Fulbr is an early-stage Windows VPN/proxy client. Contributions are welcome once the
project is published; this file describes the expected workflow.

## Ground rules

1. Never fake functionality, measurements, or security claims.
2. Never add telemetry, ads, or hidden network requests.
3. Never execute downloaded configuration content, scripts, or binaries.
4. Never disable TLS verification to "make it work".
5. Never add a driver or Windows service without an explicit, documented consent flow.
6. Keep changes modular, tested, and consistent with the architecture docs.

## Development setup

See [`docs/development/environment.md`](docs/development/environment.md).

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Pull requests

- One focused change per pull request.
- Include tests for parsers, ranking, routing, and security-sensitive code.
- Update documentation when behavior changes.
- Do not include real VPN credentials, subscription URLs with tokens, or personal data.
- Describe security implications and any new dependency/license.

## Commit style

Use clear imperative messages, for example:

- `parser: reject insecure Shadowsocks ciphers`
- `ui: add subscription import progress`
- `docs: record WFP kill-switch test plan`

## Reporting security issues

Do not open a public issue for a vulnerability. See [`SECURITY.md`](SECURITY.md).

## License

By contributing, you agree that your contribution is licensed under GPL-3.0-or-later.
