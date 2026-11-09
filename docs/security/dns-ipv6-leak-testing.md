# DNS and IPv6 Leak Testing Plan

**Status:** Test plan — **not yet executed**
**Date:** 2026-11-09
**Rule:** Fulbr must not claim leak prevention until the tests below pass on the user's
machine.

---

## 1. Why this is hard on Windows

- Windows DNS Client may query multiple interfaces in parallel (multihomed behavior).
- Applications can use their own DNS (browsers with DoH, applications with embedded
  resolvers, games with custom stacks).
- IPv6 may be available on the physical adapter even when the tunnel is IPv4-only.
- DNS cache and negative cache can make a fixed route look broken or a broken route look
  fixed.
- `strict_route` on Windows blocks port 53 outside the TUN, but its documentation warns it
  may break some applications (e.g., VirtualBox).
- DNS-over-HTTPS/DoT in the browser bypasses OS DNS entirely. Fulbr cannot control it.

Source: <https://sing-box.sagernet.org/configuration/inbound/tun/>, accessed 2026-11-09.

---

## 2. DNS policy matrix

| Traffic class | DNS server | Transport | Leak risk |
|---------------|------------|-----------|-----------|
| Protected domain | Tunnel DNS (DoH/DoT through proxy) | Encrypted, inside tunnel | Low if enforced |
| Direct Russian domain | Direct DNS (user-selected, preferably DoH) | Encrypted or ISP | Expected direct exposure |
| Blocked domain | Block before resolution or return blocked | N/A | No query should leave |
| Local/LAN domain | Local resolver | Plain/DoH per policy | LAN exposure expected |
| Application with own DoH | Outside Fulbr's control | App-defined | **Unavoidable**; must be documented |

---

## 3. Test cases

| ID | Test | Expected | Method |
|----|------|----------|--------|
| DNS-01 | Protected domain resolution | Query reaches tunnel DNS only | Unique label + authoritative log |
| DNS-02 | Direct Russian domain | Query reaches direct DNS | Unique label + log |
| DNS-03 | Blocked domain | No query leaves; connection blocked | Authoritative log + connection error |
| DNS-04 | IPv6 DNS (AAAA) | Follows same policy | AAAA query + log |
| DNS-05 | DNS after tunnel drop | No leak to ISP for protected names | Drop tunnel, query, capture |
| DNS-06 | DNS after reconnect | Cache flushed; correct server | Query + engine logs |
| DNS-07 | Browser DoH enabled | Detect bypass; warn | Browser test + packet capture |
| DNS-08 | ISP blocks DoH | Fallback policy is explicit and safe | Block DoH, observe behavior |
| DNS-09 | DNS cache poisoning/stale | Correct negative/positive behavior | Controlled TTL test |
| DNS-10 | Split DNS conflict | Deterministic rule wins; shown in UI | Rule matrix test |
| IPv6-01 | IPv4-only tunnel, IPv6 destination | Blocked, not leaked | IPv6 test endpoint |
| IPv6-02 | Dual-stack tunnel | IPv6 through tunnel | IPv6 echo |
| IPv6-03 | IPv6 DNS | Follows policy | AAAA + log |
| IPv6-04 | Tunnel drop | IPv6 blocked for protected traffic | Drop + test |
| IPv6-05 | Direct Russian IPv6 | Direct per policy | Route + echo |

---

## 4. Measurement targets

Use controlled test domains/endpoints:

- An authoritative DNS server that logs resolver IP and query name.
- A dual-stack HTTP echo endpoint that reports the observed source address.
- A local packet capture (pktmon/WFP) during the test to confirm the path.
- Public services only for bounded, permitted checks; never intrusive.

Do not embed a third-party "leak test" API as the only source of truth.

---

## 5. Acceptance criteria

- Protected DNS queries never reach the ISP resolver in any tested state.
- Direct-routed DNS is explicitly direct and labeled.
- IPv6 does not bypass the tunnel for protected traffic.
- Failure states fail closed (blocked), not open (direct).
- The UI reports `Unknown` when a test cannot be completed, not `Protected`.
- Limitations for application-level DoH are displayed.

---

## 6. Reporting template

For each test record:

- date/time, Windows build, engine version,
- network adapter and DNS configuration,
- routing mode and kill-switch mode,
- test domain/endpoint,
- observed resolver/source IP,
- expected vs actual,
- pass/fail,
- sanitized evidence.

Until executed, all results are **unknown**.

---

## 7. Proxy-mode caveat (0.2.0)

The 0.2.0 proxy mode uses an authenticated local SOCKS/HTTP inbound and does **not** create
a TUN interface or enforce a system DNS policy. DNS behavior therefore depends on the
application and on how the proxy protocol handles destination names. **No DNS/IPv6 leak
prevention is claimed in proxy mode.** These tests must be re-run for TUN mode.
