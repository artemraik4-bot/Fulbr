Fulbr 0.1.0 — Prototype

Fulbr is a privacy-focused Windows VPN/proxy client.

This prototype build:
  - imports HTTPS subscription sources,
  - parses share links strictly,
  - performs real TCP/TLS reachability checks,
  - stores credentials with Windows DPAPI,
  - provides English and Russian UI.

It does NOT yet create a VPN tunnel, install a Windows service, enforce a kill switch,
or run DNS/IPv6 leak tests. No protection is claimed.

License: GPL-3.0-or-later.
Third-party notices are included in THIRD-PARTY-NOTICES.md.

Public VPN configurations are untrusted. A working connection does not prove that a
server is private or safe. Do not use public proxies for banking or identity services.
