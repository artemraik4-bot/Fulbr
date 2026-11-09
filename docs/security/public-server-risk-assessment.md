# Public Server Risk Assessment

**Status:** Phase 2 design + initial risk taxonomy
**Date:** 2026-11-09
**Applies to:** VPN/proxy endpoints imported from public GitHub repositories, subscription
URLs, QR codes, or manually pasted share links.

---

## 1. The core problem

Public VPN configurations are **untrusted endpoints operated by unknown parties**.
A successful connection proves only that:

- the hostname resolves,
- the endpoint accepts TCP/UDP,
- the protocol handshake and credentials are valid,
- traffic can traverse the tunnel at that moment.

It does **not** prove that the operator is honest, that the server is patched, that logs
are not kept, that the exit is not monitored, or that the user is anonymous.

Fulbr must make this distinction visible in the UI and must never convert "fast" or
"working" into "safe".

---

## 2. Risk taxonomy

| ID | Risk | Likelihood for public configs | Impact | Detectable by Fulbr? |
|----|------|-------------------------------|--------|----------------------|
| R1 | Operator logs source IP/timestamps/volumes | High | High | No |
| R2 | Operator logs destination IPs/DNS | Medium–High | High | No |
| R3 | Malicious content injection / TLS stripping at exit | Low–Medium | High | Partially (TLS verification, HSTS not enforced by Fulbr) |
| R4 | Server is a honeypot / research trap | Unknown | High | No |
| R5 | Credentials are shared/public and abused | High | Medium | No |
| R6 | Server disappears or changes ownership | High | Medium | Yes (availability checks) |
| R7 | Config points to an insecure transport (`allowInsecure`, plaintext) | Medium | High | Yes (parser flags) |
| R8 | Config uses a weak/broken cipher | Low–Medium | High | Yes (method allow-list) |
| R9 | Malicious config exploits the client parser | Low | Critical | Partially (fuzzing, strict parser) |
| R10 | Config includes malicious routing/scripts | Medium | High | Yes (ignore non-proxy content) |
| R11 | Server is in a hostile jurisdiction | Unknown | High | No (geography is not trust) |
| R12 | Exit IP is shared with abusive users, causing blocks | High | Medium | No |
| R13 | Server operator can correlate user identity | High | High | No |
| R14 | DNS handled by the server is logged/altered | Medium–High | High | Partially (DNS leak tests) |
| R15 | Server is a proxy to malware/phishing infrastructure | Unknown | High | No |

**Conclusion:** public configs are suitable for **reachability/censorship circumvention**,
not for **anonymity or high-trust privacy**. Fulbr must say so.

---

## 3. Trust classification

Fulbr uses four values, with `Unknown` as the default:

| Value | Criteria | UI meaning |
|-------|----------|------------|
| **Unknown** | No independent evidence; public source; not user-verified | "We cannot verify this server's operator or logging policy." |
| **Limited** | User-imported, structurally valid, TLS verified, but operator unknown | "Transport is encrypted; operator is not independently trusted." |
| **Insecure** | TLS disabled, certificate verification disabled, broken cipher, or plaintext proxy | "Do not use for sensitive traffic." |
| **Trusted (user)** | User explicitly marks a server/source as personally trusted | "You marked this source as trusted. Fulbr has not audited it." |

There is **no** "Secure" badge based on connectivity or speed. A user-marked "Trusted"
label is a user assertion, not an audit.

---

## 4. Server information panel

Display, for each server:

- Display name (sanitized; do not render HTML/markdown from the source).
- Protocol and transport.
- Host and port (redact credentials).
- Source name + URL domain (not full secret URL).
- First seen / last seen / last successful check / last failure.
- Connectivity state (`Unknown`/`Unreachable`/`Reachable`/`Authenticated`/`TrafficVerified`).
- Performance (median latency, method, sample count, jitter).
- Security assessment (`Unknown`/`Limited`/`Insecure`/`Good`) with reasons.
- TLS details when observable: enabled, certificate verification, SNI/Reality presence
  (do not claim certificate identity unless verified).
- Known risk flags (`allowInsecure`, no TLS, weak cipher, unknown transport).
- Trust classification + warning text.
- "Why this server?" explanation of its ranking.

Never show UUIDs, passwords, private keys, `pbk`, `sid`, or full subscription URLs.

---

## 5. What Fulbr can verify (and what it cannot)

### Can verify

- Config parses and passes schema validation.
- Host resolves (DNS), TCP/UDP reachable.
- TLS handshake succeeds with certificate verification (where applicable).
- Protocol authentication succeeds.
- A bounded HTTP request through the tunnel succeeds.
- Latency/jitter/throughput (with stated method).
- Presence of known-insecure flags.
- Whether the actual outbound interface matches policy (for tested destinations).

### Cannot verify

- Operator identity, logging policy, jurisdiction, ownership.
- Whether the server is a honeypot or is monitored.
- Whether the exit is honest or injects content.
- Whether the server is patched.
- Whether the exit IP is shared/abused.
- Anonymity.
- That "no logs" claims are true.
- That a domain classified as Russian is hosted in Russia.

The UI must use "verified" only for the first list.

---

## 6. Default policy for sensitive services

- Banking, government, payment, and healthcare services are **direct by default** in Smart
  Split mode, and the user must explicitly allow proxying them.
- Fulbr must not automatically add banking domains to a public proxy route.
- If a user explicitly routes a sensitive service through an untrusted public proxy, show
  a clear warning and require confirmation.
- Do not infer sensitivity solely from `.ru`; use a curated list plus user rules, and show
  the rule source.

---

## 7. Recommendations shown to users

1. Prefer your own server or a provider you pay and trust.
2. Treat public configs as temporary and untrusted.
3. Do not use public proxies for banking, government, or identity services.
4. Do not assume "no logs" from a public server.
5. Use HTTPS everywhere; Fulbr cannot protect plaintext HTTP at the exit.
6. Enable the kill switch for protected traffic.
7. Run the DNS/IPv6 leak tests after changing routing.
8. If a server passes a ping test but traffic fails, do not assume the server is safe or
   stable; check protocol/handshake results.

---

## 8. Open work

- Build a machine-readable risk-flag registry and map it to UI copy.
- Add a per-source reputation field that is user-controlled, not automatically inferred.
- Implement TLS certificate inspection for supported transports and record findings.
- Add tests for insecure flags across all parsers.
- Review all UI copy against this document before release.

---

## 9. Sources

- <https://github.com/igareck/vpn-configs-for-russia> — public config warnings, update/test
  claims, localhost proxy issue, accessed 2026-11-09.
- <https://sing-box.sagernet.org/configuration/outbound/> — protocol/transport options,
  accessed 2026-11-09.
- <https://www.happ.su/main/dev-docs/ping> — ping-method limitations, accessed 2026-11-09.
- <https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page>
  — WFP, accessed 2026-11-09.
