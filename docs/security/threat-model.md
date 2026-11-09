# Fulbr Threat Model

**Status:** Phase 2 baseline
**Date:** 2026-11-09
**Method:** asset/actor/attack-surface analysis + STRIDE-style review
**Scope:** Fulbr client on Windows 10/11 x64, its service, engine, installer, and update
channels.

---

## 1. Assets

| Asset | Why it matters |
|-------|----------------|
| User's source IP and traffic metadata | Identity/location exposure |
| VPN/proxy credentials (UUIDs, passwords, keys) | Theft enables impersonation/abuse |
| Subscription URLs and auth headers | May contain private tokens |
| Browsing destinations / DNS queries | Sensitive behavioral data |
| Routing policy and kill-switch state | A bypass can expose protected traffic |
| Local engine config | Contains secrets and proxy endpoints |
| WFP filters/routes/DNS | Corrupting them can break or expose networking |
| Installer/update channel | Code execution on the user's machine |
| Logs and crash dumps | May leak secrets or history |
| User trust | Destroyed by false security claims |

---

## 2. Actors

| Actor | Capability | Goal |
|-------|------------|------|
| Network observer / ISP / DPI | Passive/active traffic analysis, blocking | Identify, block, or degrade VPN traffic |
| Malicious public VPN operator | Controls exit server, sees metadata | Log, inject, monetize, deanonymize |
| Compromised subscription source | Controls config content | Point users to malicious endpoints, exploit parser |
| Malicious mirror / MITM | Controls download | Serve tampered configs or binaries |
| Local unprivileged process | Same-user malware | Steal secrets, use local proxy, bypass per-app policy |
| Local admin / kernel malware | Full control | Out of scope for a user-mode client |
| Malicious update | Compromised signing/build | Code execution |
| Anti-cheat / AV | Kernel-level monitoring | False positives, blocking, conflict |
| Physical attacker | Access to unlocked machine | Read secrets, change policy |
| Well-meaning user error | Misconfiguration | Leaks, broken networking |

---

## 3. Trust boundaries

```
[Internet] ── [Subscription servers] ── HTTPS ── [Fulbr UI] 
                                                   │ named pipe (ACL)
                                              [Fulbr.Service]
                                                   │ child process
                                              [sing-box.exe]
                                                   │ Wintun / WFP / routes
                                              [Windows network stack]
```

Boundaries:

1. Internet → subscription fetcher: untrusted data, TLS-authenticated transport.
2. Internet → VPN/proxy endpoint: untrusted peer, protocol-authenticated (at best).
3. UI → service: untrusted caller must be authenticated and authorized.
4. Service → engine: trusted code, untrusted config input (must be generated/validated).
5. Engine → OS: privileged operations, must be scoped and reversible.
6. Installer → OS: privileged, must be signed/verified and reversible.

---

## 4. Threats and mitigations

### T1. Malicious public VPN server

**Threat:** Operator logs metadata, injects content, redirects traffic, or serves a
malicious exit.

**Mitigations:**
- Label every public endpoint `Trust: Unknown`.
- Explain what the exit can see before first connection.
- Never claim anonymity or "no logs".
- Do not auto-select a server based only on ping.
- Provide a user-managed trusted-server list; trust is explicit, not inferred.
- Enforce TLS verification; reject `allowInsecure`/`skip-cert-verify` by default.
- Keep sensitive/banking traffic direct by default unless the user explicitly overrides.
- Never auto-route banking domains through a public proxy.

**Residual risk:** High. Only the operator's policy and the protocol's cryptography limit
exposure. Fulbr cannot audit a third-party server.

### T2. Compromised subscription source / malicious config

**Threat:** Config points to an attacker server, exploits parser bugs, embeds scripts, or
causes resource exhaustion.

**Mitigations:**
- Treat all content as data; never execute scripts/batch/PS1/JSON routing.
- Strict parser with size/depth/field limits; fuzz tests.
- Reject unknown schemes and malformed encodings.
- HTTPS-only default; verify certificates; limit redirects; strip auth on cross-origin
  redirects.
- No `file://`, `data:`, `javascript:`, local paths.
- Canonical fingerprint + dedup; preserve raw for audit.
- Show provenance and parser warnings.
- No auto-connect after import; user selects.
- Sandboxed/limited parser process (future hardening) if fuzzing finds issues.

**Residual risk:** Medium. A syntactically valid config can still point to a malicious
server; trust labeling is the defense.

### T3. Tampered update / supply chain

**Threat:** Malicious `Fulbr.exe`, `sing-box.exe`, `wintun.dll`, installer, or rule set.

**Mitigations:**
- Pin exact versions and SHA-256.
- Download binaries only from official release URLs over HTTPS.
- Verify checksums against a separately pinned manifest.
- Sign Fulbr artifacts when a certificate is available; publish hashes.
- Separate update channels; config refresh never updates binaries.
- Reproducible build instructions; SBOM; dependency scanning.
- Do not auto-download executables from config mirrors.
- Optional DPI module never bundled by default; explicit consent; checksum.

**Residual risk:** Medium until code signing is available. Unsigned builds must be labeled.

### T4. Parser exploitation / DoS

**Threat:** Huge Base64, deeply nested JSON, regex DoS, unicode/IDN confusion, path
traversal in rule-set paths.

**Mitigations:**
- Bounded input size (e.g., 5 MiB), entry count, field length, JSON depth.
- No user-controlled regex execution in the hot path; compile with timeouts.
- Normalize IDN with a well-tested library; display punycode when ambiguous.
- Reject path traversal; never derive file paths from config strings.
- Fuzz and property-based tests in CI.

### T5. Local proxy exposure

**Threat:** Any local process uses Fulbr's SOCKS/HTTP inbound to bypass per-app policy or
learn the exit IP.

**Mitigations:**
- Require authentication (random per-session credentials).
- Bind loopback only by default.
- Random port; no discovery via fixed port.
- Document that same-user malware can still read memory/steal tokens; this is not a
  sandbox.
- Separate "Allow LAN" feature, off by default, explicit warning.

### T6. Named-pipe / IPC attack

**Threat:** Unprivileged process impersonates the UI, sends malformed commands, or
tricks the service into arbitrary file/command execution.

**Mitigations:**
- Explicit security descriptor; allow only the interactive user + Administrators.
- Verify client PID/user; reject remote pipes.
- Strict message schema, length limits, command allow-list.
- No arbitrary paths, no raw configs, no shell execution.
- Idempotent commands; rate limiting; audit log.
- Service runs with least privilege that still permits TUN/WFP.

### T7. Kill-switch bypass

**Threat:** Traffic escapes direct during connect/reconnect/crash/sleep; WFP filters fail
open; user is misled.

**Mitigations:**
- Service-owned WFP filters, not only engine-process filters.
- Fail closed while connecting/reconnecting.
- Permit only loopback, DHCP, engine endpoint, TUN, explicit direct rules.
- Watchdog restores filters if the engine dies.
- Verify actual outbound interface for sample destinations.
- Never show "Protected" without verification.
- Recovery command removes orphaned filters and restores state.

**Residual risk:** Medium; must be tested in a VM across sleep/resume and adapter changes.

### T8. DNS leak

**Threat:** Queries go to ISP DNS despite a VPN; split-tunnel DNS follows the wrong path.

**Mitigations:**
- DNS inside the tunnel for protected traffic.
- Domain-aware DNS rules consistent with routing.
- Windows `strict_route` block of port 53 outside TUN (documented).
- IPv6 DNS handling; block IPv6 if not tunneled.
- Detect browser/application DoH that bypasses OS DNS; explain limitation.
- Leak tests are user-initiated and report actual results, not promises.

### T9. IPv6 leak

**Threat:** IPv4 is tunneled, IPv6 goes direct.

**Mitigations:**
- Explicit IPv6 policy: tunnel it, or block it for protected traffic.
- Verify with a user-initiated IPv6 test.
- Never claim IPv6 protection without testing.

### T10. Split-tunnel misclassification

**Threat:** A domain/IP is classified direct when it should be proxied (or vice versa),
exposing traffic or breaking access.

**Mitigations:**
- Deterministic precedence and specificity rules.
- Show rule provenance and conflicts.
- `Why This Route?` verifies actual interface.
- Conservative defaults: only well-known Russian service categories direct; banking
  direct; everything else tunneled.
- User-editable exceptions; export/import for review.
- Warn when ECH/CDN makes classification uncertain.

### T11. Secrets at rest / in logs

**Threat:** Credentials and URLs leak via config files, logs, crash dumps, screenshots.

**Mitigations:**
- DPAPI/Credential Manager; no plaintext.
- Protected runtime directory + ACL + delete on stop.
- Redaction in logs/errors/UI; no full URLs or UUIDs.
- Disable full memory dumps by default? Document OS-level risk.
- Export is redacted by default.

### T12. Privilege escalation via service

**Threat:** Service bug lets a local user gain admin.

**Mitigations:**
- Minimal command surface, strict validation, no arbitrary file/command.
- Do not run the engine as the interactive user; use a service account.
- Audit named-pipe ACL and impersonation logic.
- Keep service code small and heavily tested.
- Security review before release; fuzz IPC.

### T13. Uninstall/update leaves unsafe state

**Threat:** Orphaned WFP filters break networking; routes/DNS not restored; service remains.

**Mitigations:**
- Uninstaller stops service, removes filters, restores routes/DNS, deletes runtime files.
- Recovery tool works without the UI.
- Installer detects existing install and upgrades cleanly.
- Preserve user settings only with explicit choice; document what is removed.

### T14. Anti-cheat / antivirus conflict

**Threat:** Wintun/WFP/WinDivert triggers anti-cheat or AV; user is banned or app is
quarantined.

**Mitigations:**
- No hidden drivers; no auto AV exclusions.
- Core uses signed Wintun only; optional DPI module is separate and off by default.
- Detect and warn about known conflicts (FACEIT, etc.).
- Provide clean removal.
- Do not claim compatibility that has not been tested.

### T15. False security claims

**Threat:** UI says "secure"/"anonymous"/"leak-proof" without evidence.

**Mitigations:**
- Separate connectivity/performance/security indicators.
- `Unknown` when evidence is missing.
- Security copy reviewed against this threat model.
- No marketing language in the UI.

---

## 5. Out of scope

- Compromised Windows kernel, hypervisor, or firmware.
- A local administrator or same-user malware with debug privileges.
- Physical coercion or device seizure.
- Traffic analysis by a global adversary that can correlate ingress/egress.
- Guaranteed anonymity.
- Protecting traffic from applications that ignore OS routing (e.g., raw sockets with
  bindings to a physical adapter, some games/anti-cheat).
- Third-party server logging policy.

---

## 6. Security requirements checklist

- [ ] No telemetry by default; visible network activity log.
- [ ] DPAPI secret storage; no plaintext credentials.
- [ ] Strict parser with fuzzing and size limits.
- [ ] HTTPS-only subscriptions by default; verified TLS.
- [ ] No `allowInsecure` by default.
- [ ] Authenticated loopback inbound; no LAN by default.
- [ ] Named-pipe ACL + command allow-list.
- [ ] Service-owned WFP kill switch; fail closed.
- [ ] Route/DNS restoration + recovery tool.
- [ ] IPv6 policy tested.
- [ ] DNS leak test with real results.
- [ ] Per-server trust `Unknown` for public endpoints.
- [ ] No auto-connect after subscription import.
- [ ] Separate update channels; pinned checksums.
- [ ] Uninstaller cleans WFP/routes/DNS/service.
- [ ] AV/anti-cheat conflict warnings.
- [ ] Honest limitations in UI and docs.

---

## 7. Review triggers

Re-run this threat model when:

- a new protocol or engine is added,
- the local API/IPC surface changes,
- a kernel driver or packet interception module is proposed,
- telemetry or cloud features are proposed,
- a security vulnerability is reported,
- Windows changes WFP/TUN/DNS behavior,
- a release changes the installer or update mechanism.
