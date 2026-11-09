# Security Policy

Fulbr handles network routing and sensitive proxy credentials. Please report security
issues responsibly.

## Reporting

Do **not** open a public issue. Send a private report to the maintainers once a contact
channel is published. Until then, keep the report local and include:

- affected version/commit,
- Windows build,
- steps to reproduce,
- impact (credential exposure, traffic leak, privilege escalation, DoS, etc.),
- whether the issue is already public.

## Scope

In scope:

- configuration parser vulnerabilities,
- secret leakage in logs/state/UI,
- named-pipe/IPC authorization flaws,
- WFP/route/DNS fail-open behavior,
- installer/uninstaller security,
- dependency vulnerabilities.

Out of scope:

- a compromised Windows kernel, hypervisor, or firmware,
- a local administrator or same-user malware with debug privileges,
- third-party VPN server logging or operator behavior,
- guarantees of anonymity.

## Disclosure

We aim to acknowledge reports promptly, provide a fix timeline, and credit reporters
unless they prefer to remain anonymous. No bounty program is promised.

## Current status

Fulbr is a prototype. It does not yet create a tunnel, install a service, or claim
kill-switch/DNS-leak protection. Do not rely on it for protection.
