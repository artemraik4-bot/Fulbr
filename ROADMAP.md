# Fulbr Roadmap

**Version:** 0.1 (planning)
**Date:** 2026-11-09
**Rule:** A phase is complete only when its acceptance criteria are met and evidenced.
Roadmap items are not claims of completion.

---

## Phase 1 — Research

**Status:** Initial pass complete; ongoing verification.

Deliverables:

- [x] `docs/research/overview.md`
- [x] `docs/research/windows-networking.md`
- [x] `docs/research/protocol-comparison.md`
- [x] `docs/research/security-and-privacy.md`
- [x] `docs/research/technology-selection.md`
- [x] `docs/research/dependency-and-license-audit.md`
- [x] `docs/architecture/networking-engine-evaluation.md`
- [x] `docs/security/threat-model.md`
- [ ] Validate `strict_route` coexistence with VirtualBox/Hyper-V (VM)
- [ ] Sample-parse the reference repository and record rejection reasons
- [ ] Confirm current sing-box/Windows behavior with a real probe

**Acceptance:** decisions documented with sources; open questions listed; no fabricated
data.

---

## Phase 2 — Architecture

**Status:** Baseline written.

Deliverables:

- [x] `docs/architecture/overview.md`
- [x] component interfaces and process model
- [x] state machine and failure policy
- [x] routing precedence
- [x] subscription and health-check design
- [x] threat model
- [ ] ADRs for: engine choice, UI stack, service boundary, licensing
- [ ] Data schema migrations plan
- [ ] UI wireframes and design system spec

**Acceptance:** a new contributor can understand the modules, boundaries, and data flow
without reading code; security review of the design is recorded.

---

## Phase 3 — Environment setup

**Status:** Complete (2026-11-09).

Deliverables:

- [x] `docs/development/environment.md`
- [x] Install Git 2.56.0.4, .NET 10 SDK 10.0.403, Inno Setup 6.7.3
- [x] Verify versions and build a WPF application
- [ ] Download/verify sing-box and Wintun into `vendor/` (separate approval)
- [x] Reproducible build script (`tools/build-release.ps1`)
- [ ] CI skeleton (build + tests + license scan)

**Acceptance:** `dotnet build` and `dotnet test` pass on a clean checkout; hashes pinned.

---

## Phase 4 — UI prototype

**Status:** Complete for the prototype (2026-11-09). The tunnel UI states are honest.

Deliverables:

- [x] Original Fulbr logo + icon set (SVG, PNG, ICO)
- [x] WPF design system (light/dark, DPI, accessibility basics)
- [x] Dashboard, Servers, Diagnostics, Routing, Subscriptions, Security, Settings, Logs
- [x] Tray icon and menu
- [x] Clearly labeled prototype data (Connect disabled, no fake tunnel state)
- [x] English/Russian resources
- [x] Keyboard navigation basics
- [x] Real subscription import and TCP/TLS probe data

**Acceptance:** runnable prototype; every mock value visibly labeled; no network claims
presented as real.

---

## Phase 5 — Basic connectivity

**Status:** Proxy mode complete; service/TUN code built; TUN runtime pending VM verification.

Deliverables:

- [x] sing-box supervisor with pinned binary + SHA-256 verification
- [x] `sing-box check` config validation
- [x] Authenticated local SOCKS/HTTP inbound
- [x] Real HTTP-through-proxy verification before "Connected"
- [x] Connect/disconnect + runtime cleanup
- [x] Fulbr service host + ACL'd named-pipe IPC
- [x] TUN config builder + Wintun 0.14.1 bundling
- [ ] Windows VM test of TUN adapter/route/DNS
- [ ] Route/DNS state save/restore verification

**Acceptance:** real traffic traverses the tunnel; disconnect restores networking;
`Connected` appears only after verification.

---

## Phase 6 — Subscription management

**Status:** Partial. Fetching, parsing, dedup, and protected caching work; scheduling is not implemented.

Deliverables:

- [x] HTTPS fetcher with ETag/Last-Modified, timeouts, size limits
- [x] Share-link + Base64 + strict parser
- [x] Rejection reasons and provenance
- [x] Canonical fingerprint + dedup
- [x] Local cache, last-known-good snapshot
- [ ] Manual refresh + 6-hour default schedule with jitter
- [x] Source trust labels and warnings

**Acceptance:** real repository sample imports; malformed entries rejected; no execution
of embedded content; tests cover redirects/oversize/encoding.

---

## Phase 7 — Server verification

**Status:** Partial. TCP/TLS probes and ranking work; protocol handshake and lifecycle do not.

Deliverables:

- [x] TCP and TLS probes with distinct method labels
- [x] Bounded concurrency and timeouts
- [x] Basic failure recording (no classification yet)
- [ ] Protocol handshake / HTTP-through-proxy probes
- [ ] Lifecycle state machine + backoff + quarantine
- [ ] Widespread-outage detection
- [x] Ranking algorithm + explanation data
- [x] Bulk "check all" with cancellation on busy state

**Acceptance:** probes reflect real results; no single-ping decisions; servers recover;
user configs are never auto-deleted.

---

## Phase 8 — Intelligent routing

**Status:** Not started.

Deliverables:

- [ ] Full Tunnel, Smart Split, Custom, Proxy, Direct modes
- [ ] Rule precedence engine with conflict detection
- [ ] Russian category rule set + user overrides
- [ ] Per-app (path) and per-domain rules
- [ ] DNS rules aligned with routing
- [ ] IPv6 policy
- [ ] "Why This Route?" with verified interface
- [ ] Local authenticated proxy mode

**Acceptance:** split-tunnel test matrix passes in a VM; direct traffic is labeled;
protected traffic never silently escapes.

---

## Phase 9 — Security and privacy

**Status:** Not started for TUN. WFP kill-switch design documented; no persistent filters.

Deliverables:

- [ ] DPAPI secret storage + protected runtime config
- [ ] Service-owned WFP kill switch (Strict/Smart/Manual)
- [ ] Watchdog + emergency recovery tool
- [ ] DNS/IPv6 leak tests with real results
- [ ] Redacting logger + retention controls
- [ ] Trust/security assessment UI
- [ ] Threat-model review + parser fuzzing
- [x] WFP kill-switch design (`docs/security/wfp-killswitch-design.md`)

**Acceptance:** failure tests show fail-closed behavior; no secrets in logs; leak tests
recorded; recovery works without a reboot.

---

## Phase 10 — Optimization

**Status:** Not started.

Deliverables:

- [ ] Startup, idle RAM/CPU, connected CPU, throughput, switching time
- [ ] Large-list (1k/10k) performance
- [ ] 8-hour soak test
- [ ] Battery/polling trade-off notes
- [ ] `docs/performance/benchmarks.md`
- [ ] `docs/performance/optimization-report.md`

**Acceptance:** measurements are reproducible and documented; no invented numbers.

---

## Phase 11 — Release preparation

**Status:** Partial. Installer and docs exist; local install and signing are pending.

Deliverables:

- [x] `Fulbr.exe` (unsigned, self-contained)
- [x] `Fulbr-Setup.exe` with shortcuts, uninstaller, Installed Apps entry
- [x] Icon in exe/installer/shortcuts/tray
- [x] README, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, LICENSE, CHANGELOG, ROADMAP
- [ ] CI: build, tests, dependency and license scan, SBOM
- [ ] Issue/PR templates
- [x] Known-limitations document
- [ ] Local install (only after explicit approval) and verification

**Acceptance:** clean VM install/uninstall; no leftover WFP/routes/DNS/service; install
verified on the owner's PC after approval.

---

## Post-1.0 candidates (not commitments)

- Optional Xray engine for specific Reality cases.
- Connection profiles and quick-connect hotkeys.
- Automatic failover with user-defined policy.
- Trusted-network detection (home/work) with per-network policy.
- Portable mode.
- Signed update feed with rollback.
- Advanced DNS policies and per-app DNS.
- Streaming/game latency profiles.
- LAN sharing with authentication.
- Optional DPI module (separate, consent-based, never bundled by default).
