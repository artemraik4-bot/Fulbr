# Protocol Comparison and Measurement Semantics

**Status:** Phase 1 research
**Date accessed:** 2026-11-09
**Scope:** Protocols that appear in public subscriptions and can realistically be used by Fulbr.
**Rule:** A protocol name is not a security guarantee. Encryption, obfuscation, exit-node trust,
and anonymity are four different properties.

---

## 1. Vocabulary (must be used consistently in the UI and docs)

| Term | Meaning in Fulbr |
|------|------------------|
| **VPN** | A tunnel that carries IP packets for applications, usually via a virtual interface (WireGuard, OpenVPN, TUN mode). |
| **Proxy protocol** | A client-to-server protocol that carries TCP/UDP streams (VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC, SOCKS5). It becomes VPN-like when combined with a TUN interface. |
| **Subscription** | A URL or file containing one or more server configurations. It is data, never code. |
| **Configuration** | One server endpoint plus credentials and transport parameters. |
| **Routing rule** | A local policy mapping traffic to direct/proxy/block. |
| **Transport** | How bytes reach the proxy server: TCP, TLS, WebSocket, gRPC, QUIC, HTTP/2, etc. |
| **Encryption** | Confidentiality/integrity between the client and the proxy server. |
| **Obfuscation** | Making traffic look like something else to a network observer (Reality, ShadowTLS, Hysteria obfs). |
| **Trust** | What the exit server can observe and whether the operator is accountable. |
| **Anonymity** | The user's identity cannot be linked to the traffic. Public proxies generally do **not** provide this. |

---

## 2. Protocol matrix

| Protocol | Typical transport | Encryption / integrity | Censorship resistance | Exit-node visibility | Fulbr default posture |
|----------|-------------------|------------------------|-----------------------|----------------------|-----------------------|
| **VLESS + REALITY** | TCP + TLS-like handshake (REALITY) | TLS 1.3 to a borrowed/real target; REALITY authenticates the server | High (no own domain, mimics real TLS) | Sees source IP, timing, volume, destinations at exit | Supported; `Trust: Unknown` for public endpoints |
| **VLESS + TLS** | TCP + TLS, often WS/gRPC | TLS 1.3 | Medium–high | Same as above | Supported |
| **VMess** | TCP/WS/gRPC + TLS | AEAD + optional TLS | Medium | Same as above | Supported; warn if TLS is absent |
| **Trojan** | TCP + TLS | TLS 1.3 | Medium–high (looks like HTTPS) | Same as above | Supported |
| **Shadowsocks** | TCP/UDP, AEAD | AEAD cipher; **no TLS** unless wrapped | Low–medium (traffic pattern detectable) | Same as above | Supported; **reject** `none`/stream ciphers and malformed keys |
| **Hysteria2** | QUIC/UDP + TLS | TLS 1.3 | Medium (UDP may be blocked/throttled) | Same as above | Supported; label UDP path |
| **TUIC** | QUIC/UDP + TLS | TLS 1.3 | Medium | Same as above | Supported; label UDP path |
| **WireGuard** | UDP, NoiseIK | Modern AEAD | Low (handshake fingerprint; no domain fronting) | Same as above | Supported as a protocol; not a censorship tool |
| **OpenVPN** | UDP/TCP + TLS | TLS | Low–medium | Same as above | **Not in initial scope** (no sing-box outbound); document limitation |
| **SOCKS5 / HTTP proxy** | TCP (SOCKS5 UDP assoc optional) | **None** unless wrapped | N/A | Sees all traffic | Local/optional only; never present as private |
| **Tor** | Onion routing | Layered encryption | High | Exit node sees destination; entry sees source | Not a VPN; separate optional integration, not a server type |

Sources: sing-box outbound list <https://sing-box.sagernet.org/configuration/outbound/>
(accessed 2026-11-09); protocol docs linked from that page; Xray-core README and
documentation <https://github.com/XTLS/Xray-core> (accessed 2026-11-09).

**Important:** The reference config repository includes VLESS, VLESS Reality, VMess,
Trojan, Shadowsocks, Hysteria2, TUIC, and other formats. Not every client supports every
protocol. Fulbr's parser must map each link to a supported engine outbound or reject it
with a clear reason.

---

## 3. Security semantics that the UI must not blur

### 3.1 Encryption ≠ privacy

TLS protects content between the client and the proxy server. The proxy operator still
sees:

- the client's source IP (unless the protocol hides it, which these do not),
- connection times and duration,
- traffic volume and timing,
- destination IP addresses at the exit,
- unencrypted HTTP content and DNS queries handled by the server,
- SNI unless ECH/Reality hides it.

Therefore a public VLESS/Trojan endpoint provides **transport confidentiality** and
**blocking circumvention**, not anonymity and not a trusted exit.

### 3.2 Obfuscation ≠ trust

Reality and ShadowTLS make the connection harder to classify. They do not make the
operator honest. A malicious server can still log metadata and serve malicious content.

### 3.3 "It connected" ≠ "it is safe"

A successful handshake proves reachability and credential validity. It says nothing about
the operator, the server's patch level, logging policy, or whether the exit is monitored.

### 3.4 No universal DNS guarantee

A proxy can carry DNS inside the tunnel, but an application with its own DoH/DoT or a
browser with ECH can bypass OS DNS. Fulbr must report what it can and cannot enforce.

---

## 4. Configuration risk flags

The parser and security assessor should recognize (non-exhaustive):

| Flag / condition | Meaning | Default action |
|------------------|---------|----------------|
| `allowInsecure=1`, `skip-cert-verify=true`, `insecure=true` | TLS certificate verification disabled | **Reject** by default; require an explicit advanced override with a warning |
| `security=none` on a TLS-capable protocol | No transport encryption | Reject or mark `Insecure`; never silently connect |
| Missing `tls` on Trojan/VLESS-TLS expectations | Plaintext or mislabeled transport | Warn + mark `Unknown`/`Insecure` |
| `ss` method `none`, `rc4-md5`, `table`, `des-cfb` | Broken/legacy cipher | Reject |
| Unknown/extra URL parameters | Parser may misinterpret | Preserve raw config; do not guess; show warning |
| Credentials in query string | Common for share links | Never log; redact; store with DPAPI |
| Server name ≠ certificate/SNI host | Possible misconfiguration or interception | Warn; do not auto-trust |
| REALITY `pbk`/`sid` present | Normal for Reality | Verify structure; do not treat as a security guarantee |
| `flow=xtls-rprx-vision` | XTLS Vision flow | Accept only if engine supports it |
| Huge config / deeply nested JSON | Parser DoS risk | Enforce size/depth limits |
| Embedded routing/scripts in JSON | Executable behavior in data | **Ignore**; never execute |

---

## 5. Measurement semantics (Ping Test design)

Fulbr must never show one number labeled simply "ping". Each result stores its method.

| Method | What it measures | Requires tunnel? | Caveats |
|--------|------------------|------------------|---------|
| **ICMP echo** | Network RTT to the server IP | No | Often blocked; may measure a CDN/anycast front, not the real service |
| **TCP connect** | Time to complete a TCP handshake to host:port | No | Measures reachability of the listener, not protocol/auth |
| **TLS handshake** | TCP + TLS handshake time | No (can be direct) | Still does not prove proxy auth |
| **Protocol handshake** | Full proxy protocol negotiation/auth | Usually no TUN needed; engine can run a test outbound | Proves the server accepted the configuration |
| **HTTP through proxy** | Request through the tunnel to a controlled test URL | Yes (proxy path) | Most representative; includes DNS/TLS/exit latency |
| **Download throughput** | Bytes/second through the tunnel | Yes | Bounded, opt-in; consumes data; not comparable across test URLs |
| **Upload throughput** | Bytes/second through the tunnel | Yes | Same |
| **Stability** | Success rate and jitter over a window | Depends | Needs repeated samples; do not infer from one ping |

### Recommended implementation

- Use a short-lived sing-box instance with a loopback SOCKS inbound and the candidate
  outbound. For "protocol handshake", perform a SOCKS5 CONNECT to a controlled endpoint
  and require a valid HTTP response. Record the phase timings separately.
- For "HTTP through proxy", use sing-box's Clash-compatible API delay endpoint against a
  small, reputable test target, with a strict timeout and bounded concurrency.
- For ICMP, use the Windows ICMP helper API (`IcmpSendEcho`) rather than raw sockets, and
  label it "ICMP (may be blocked)".
- Never use a single public "what is my IP" service as the only test target. Make the
  target configurable and disclose it.
- Rate-limit: default 4 concurrent checks, 100–250 ms spacing, exponential backoff on
  failure. No continuous polling.

The exact API/target set will be validated in Phase 7. Until then the Diagnostics page
must show "not measured", not invented values.

---

## 6. Engine coverage decision

| Requirement | sing-box 1.14.4 | Xray-core (current) | Notes |
|-------------|-----------------|---------------------|-------|
| VLESS / Reality | Yes | Yes (origin) | Reality originates in Xray; sing-box implements it |
| VMess | Yes | Yes | |
| Trojan | Yes | Yes | |
| Shadowsocks | Yes | Yes | |
| Hysteria2 | Yes | Not the focus | sing-box advantage for this source |
| TUIC | Yes | Not the focus | sing-box advantage |
| WireGuard | Yes | Yes | |
| TUN on Windows | Yes (Wintun) | No built-in TUN | sing-box advantage |
| Process-based routing | Yes (`process_name`/`process_path`) | Partial/depends on client | sing-box advantage |
| DNS module | Yes | Yes | |
| Local API | Clash-compatible API | gRPC API | sing-box easier for UI integration |
| License | GPL-3.0-or-later + name clause | MPL-2.0 | Determines Fulbr's license |

**Decision:** sing-box is the default engine. Xray remains a possible optional engine for
Reality-specific edge cases, but adding a second engine multiplies test and security work
and will not be done without evidence that sing-box fails a required case.

---

## 7. Open verification items

- Which protocols in the reference source actually parse cleanly with a strict parser, and
  what percentage are rejected? (Measure on a sample; do not guess.)
- Do Hysteria2/TUIC work on the user's ISP, or are UDP/QUIC blocked? (Test with explicit
  consent.)
- Does Reality work through the user's current DPI environment? (Test in a VM first.)
- Does sing-box's `flow=xtls-rprx-vision` path behave correctly with the chosen build?
- What is the real handshake and HTTP-through-proxy latency overhead? (Benchmark later.)
