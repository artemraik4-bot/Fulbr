# GitHub migration — 2026-11-14

**Status: COMPLETED as a PUBLIC repository.**
**Private creation was impossible on the authorized account (U.S. trade-controls
restriction). The owner explicitly approved public publication with session history
excluded. Nothing was deleted locally.**

| Item | Value |
|---|---|
| Repository | <https://github.com/artemraik4-bot/Fulbr> |
| Visibility | Public |
| Default branch | `main` |
| Latest verified code commit | `1b2db90` |
| CI | GitHub Actions `CI` — all steps passing |

---

## 1. Private-repository attempt (denied)

The original goal was a **private** repository. GitHub rejected creation for account
`artemraik4-bot` at the account level, not because of token scopes.

GraphQL:

```
Trade controls restricted owner can't create repositories.
Trade controls restricted creator can't create repositories. (createRepository)
```

REST (`POST /user/repos`, `private: true`):

```
HTTP 422
Due to U.S. trade controls law restrictions, we are unable to provide this access
to this API.
https://docs.github.com/en/site-policy/github-and-terms/github-and-trade-controls
```

The web UI offered only **public** visibility. Per GitHub policy, trade-restricted
accounts may be limited to public repository services. A trade-restricted account cannot
make the repository private again.

After explicit owner approval, the repository was published publicly. The sanitized
session export and internal handoff documents were **excluded from the public history**.

## 2. What was published

- Full source: `src/Fulbr.App`, `src/Fulbr.Core`, `src/Fulbr.Service`.
- Tests: `tests/Fulbr.Core.Tests` (25 tests).
- Installer source: `installer/`.
- Build and VM scripts: `tools/` (personal absolute paths removed).
- Documentation: `README.md`, `ARCHITECTURE.md`, `KNOWN_ISSUES.md`,
  `IMPLEMENTATION_STATUS.md`, `ROADMAP.md`, `NEXT_STEPS.md`, `CHANGELOG.md`,
  `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `THIRD-PARTY-NOTICES.md`,
  `docs/` (research, architecture, security, performance, development).
- Pinned-binary policy: `vendor/MANIFEST.json`, `vendor/README.md`,
  `tools/fetch-vendor.ps1`.
- CI: `.github/workflows/ci.yml`.
- Issue and pull-request templates.

**Excluded from public history:** `session-backup/`, `docs/handoff/`, and the local
`tools/install-toolchain.log` (contained the machine name and user account).

Excluded from Git entirely: vendor binaries, build outputs, the Windows ISO, VMs, test-VM
credentials, and the test VPN link.

## 3. Security and history rewrite

- Scanned the working tree and every commit for tokens, private keys, passwords, DPAPI
  blobs, proxy links, machine names, and personal paths.
- Removed `session-backup/` and `docs/handoff/` from all commits.
- Removed `tools/install-toolchain.log` from all commits and added `tools/*.log` to
  `.gitignore`.
- Core history is preserved: the early checkpoints are still present, with new hashes only
  where the removed files had existed.
- Latest published commit: `1b2db90` (`test: fix accept/listener race in TCP probe test`).

## 4. Verification

Performed from a fresh clone of the public repository:

| Check | Result |
|---|---|
| Clone succeeds | Yes |
| Remote HEAD equals local HEAD | Yes |
| No internal handoff/session files in tree or history | Confirmed |
| `tools/fetch-vendor.ps1` | Archives and binaries match pinned SHA-256 |
| `dotnet restore --locked-mode` | Success |
| `dotnet build -c Release` | 0 warnings, 0 errors |
| `dotnet test -c Release` | 25/25 passed (six repeated local runs) |
| `tools/build-release.ps1` | App publish + Inno Setup installer built |
| `tools/build-service.ps1` | Service + engine + Wintun payload built |
| GitHub Actions CI | All steps passing |
| Secret scan of all commits | Clean |

CI initially failed on one flaky socket test (`TcpProbeConnectsToLocalListener`); the race
was fixed in `1b2db90`, and the rerun passed.

## 5. Local state

| Item | Status |
|---|---|
| Local Git history | Intact, HEAD `1b2db90`, remote `origin` configured |
| Installed Fulbr 0.2.0 | Intact at `%LOCALAPPDATA%\Programs\Fulbr` |
| Desktop shortcut | Intact at `C:\Users\user\Desktop\Fulbr.lnk` |
| Installer | Copied to `Z:\Fulbr\Fulbr-Setup.exe` (D: is absent) |
| Test VMs | Saved, untouched |
| Local deletion | **Not started**; requires approval of the exact list |

`D:\Fulbr\Fulbr-Setup.exe` could not be created because the machine has no D: drive. The
installer is available at `Z:\Fulbr\Fulbr-Setup.exe`, SHA-256
`5BAA1180E64700C9E19A0B3966FA59B2E01FF13C368B3C727C8ED6A064E4E7E7`.

## 6. Recovery assets

- Current full-history bundle (public history):
  `Z:\Fulbr\Backups\Fulbr_git_2026-11-14.bundle`
- Project backup with internal handoff/session material (do **not** publish):
  `Z:\Fulbr\Backups\Fulbr_2026-11-14_105630`
- Previous backups: `Z:\Fulbr\Backups\Fulbr_2026-11-10_213201`,
  `Z:\Fulbr\Backups\Fulbr_2026-11-09_232639`.

Restore anywhere:

```powershell
git clone https://github.com/artemraik4-bot/Fulbr.git
```

## 7. Future workflow

1. Clone or update `https://github.com/artemraik4-bot/Fulbr.git`.
2. Use a `dev` branch for unfinished work; keep `main` buildable.
3. Run `tools/fetch-vendor.ps1`, then build and test.
4. Commit meaningful changes and push completed milestones.
5. Tags for releases; CI must pass before merging to `main`.
6. Never commit secrets, subscriptions, credentials, VMs, ISOs, or vendor binaries.

## 8. Remaining risk

- The repository is **public**, not private. This was an explicit owner decision after the
  account restriction made private creation impossible.
- The account restriction may also limit some GitHub features; CI currently works.
- Local cleanup remains blocked until the owner approves the exact deletion list.
