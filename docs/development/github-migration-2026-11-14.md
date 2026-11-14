# GitHub migration — 2026-11-14

**Status: BLOCKED by GitHub account restriction. Nothing was pushed. Nothing was deleted.**

---

## 1. Goal

Move the complete Fulbr project and Git history to a **private** GitHub repository named
`Fulbr`, verify the migration, then prepare an approval-gated local cleanup.

## 2. What was done

- Prepared the repository for publishing:
  - `tools/fetch-vendor.ps1` — downloads sing-box 1.14.4 and Wintun 0.14.1, verifies
    archive and binary SHA-256, places them in `artifacts/`.
  - `vendor/README.md` — pinned-binary policy and licensing note.
  - `ARCHITECTURE.md` — current-state architecture at the repository root.
  - `docs/handoff/` — sanitized 2026-11-10 handoff snapshot plus `README.md`.
  - `session-backup/` — sanitized session export plus `README.md`.
  - `.github/workflows/ci.yml` — vendor fetch, app + service publish, payload checks,
    installer packaging when Inno Setup is present.
  - Removed hard-coded personal paths from four development scripts.
- Ran a secret scan over the working tree and full Git history:
  - no tokens, private keys, passwords, DPAPI blobs, or real proxy links;
  - only parser code and synthetic test fixtures contain `vless://`/`vmess://` strings.
- Verified build/tests after the changes:
  - `dotnet test -c Release` — **25/25 passed**;
  - `dotnet build -c Release` — **0 warnings, 0 errors**;
  - `tools/fetch-vendor.ps1` — archives and binaries match pinned hashes.
- Committed: `473507579bf5ccc1120ac744eef097f9d863bc49`
  (`chore: prepare GitHub migration (vendor fetch, architecture, handoff, CI)`).
- Authenticated official GitHub CLI 2.104.0 as **`artemraik4-bot`** via device flow.
- Created a verified full-history Git bundle and a new project backup.

## 3. Blocker

Repository creation is rejected by GitHub for this account.

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

This is an account-level trade-controls restriction, not a token, scope, or network
problem. The token has `repo` and `workflow` scopes and valid API access; only repository
creation is denied.

Per GitHub's published policy, trade-restricted accounts may be limited to public
repository services for personal communications, and private repository services are
suspended. Creating a **public** repository was **not** attempted because the owner
requires the repository to remain private until a security and licensing review.

## 4. Current state

| Item | Status |
|---|---|
| Local Git history | Complete, HEAD `4735075` |
| Git remote | None (`origin` was not added) |
| GitHub repository | Not created |
| Push | Not performed |
| Local project files | Intact |
| Installed Fulbr 0.2.0 | Intact |
| Installer | Intact at `artifacts\installer\Fulbr-Setup.exe` |
| Test VMs | Saved, untouched |
| Local cleanup | Not started; requires verified migration + approval |

## 5. Recovery assets

- Full-history bundle:
  `Z:\Fulbr\Backups\Fulbr_git_2026-11-14.bundle`
  - SHA-256 `4DE88508FBEAD041433D0051523A17FF5725F0C93795E943F2909E1407F841FD`
  - `git bundle verify` reports a complete history.
- Project backup: `Z:\Fulbr\Backups\Fulbr_2026-11-14_105630`
  - 650 files, 603,997,634 bytes; Git HEAD `4735075...`.
- Previous backup: `Z:\Fulbr\Backups\Fulbr_2026-11-10_213201`.

To restore the repository anywhere:

```powershell
git clone Z:\Fulbr\Backups\Fulbr_git_2026-11-14.bundle Fulbr
```

## 6. Options to unblock

1. **Appeal / verify with GitHub Support** (recommended if the account is legitimately
   outside a sanctioned region). Ask for review of the trade-controls restriction.
   <https://support.github.com/contact>
2. **Use a different GitHub account** that is not restricted, and authorize `gh` for it.
3. **Approve a public repository** instead of private (requires explicit approval and a
   completed security/licensing review; changes the original requirement).
4. **Use another host** (GitLab, Codeberg, self-hosted Gitea) while keeping GitHub as a
   later mirror.

Until one of these is chosen, the local project remains the only copy. No cleanup will be
performed.
