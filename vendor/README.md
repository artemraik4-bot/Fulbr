# Vendor binaries

Fulbr depends on two pinned third-party runtime files. They are **not committed** to Git
because of their size and licensing; they are downloaded and verified on demand.

| File | Version | License |
|---|---|---|
| `sing-box.exe` | 1.14.4 | GPL-3.0-or-later with name/association clause |
| `wintun.dll` | 0.14.1 (amd64) | Prebuilt binary license; source GPL-2.0 |

`MANIFEST.json` is the source of truth for download URLs and SHA-256 hashes for both the
archives and the extracted binaries.

## Fetch

```powershell
powershell -ExecutionPolicy Bypass -File tools/fetch-vendor.ps1
```

The script:

1. Downloads the exact archive versions listed in `MANIFEST.json`.
2. Verifies the archive SHA-256 before extracting.
3. Verifies the extracted `sing-box.exe` and `wintun.dll` SHA-256.
4. Copies them to:
   - `artifacts/app/engine/sing-box.exe`
   - `artifacts/service/engine-service/sing-box.exe`
   - `artifacts/service/engine-service/wintun.dll`

Use `-VerifyOnly` to check existing files without network access, and `-Force` to
re-download.

Downloaded archives and extracted trees are ignored by Git (`vendor/*.zip`,
`vendor/extract/`, `vendor/wintun-extract/`).

## Licensing

See [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) and
[`docs/research/dependency-and-license-audit.md`](../docs/research/dependency-and-license-audit.md).
Do not redistribute modified copies of these binaries without reviewing their licenses.
