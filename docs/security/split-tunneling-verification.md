# Split-Tunneling Verification Plan

**Status:** Test plan — **not yet executed**
**Date:** 2026-11-09
**Rule:** No result is reported here until it has been measured on the test machine/VM.

---

## 1. What must be proven

For each routing mode, Fulbr must demonstrate:

1. Protected traffic actually uses the VPN tunnel.
2. Direct-routed Russian services actually use the physical interface.
3. Proxy-routed destinations actually use the configured proxy.
4. DNS resolution follows the same policy as the connection.
5. IPv6 does not silently bypass the policy.
6. A tunnel disconnect does not create an unexpected direct path.
7. Switching servers does not bypass rules.
8. Wi-Fi/Ethernet transitions preserve the policy.
9. An application crash does not leave unsafe or broken routing.
10. `Why This Route?` reflects reality, not a stored guess.

---

## 2. Test environment

- Windows 10/11 x64 VM (VirtualBox or Hyper-V), snapshots before each risky test.
- A local test server with known IPv4/IPv6 addresses and a controlled HTTP endpoint.
- A controlled SOCKS/HTTP proxy that logs which client/route was used.
- A DNS test domain with an authoritative server that records the resolver.
- Public test targets only for bounded, permitted checks; never intrusive scans.
- No real credentials in fixtures; use generated test UUIDs/passwords.

**Important:** sing-box documents that `strict_route` may break VirtualBox. Test TUN
behavior on a VM with a host-only/NAT adapter carefully, and record conflicts.

---

## 3. Test matrix

| ID | Scenario | Mode | Expected | Measurement |
|----|----------|------|----------|-------------|
| ST-01 | Protected domain | Full Tunnel | Uses VPN exit IP | Public IP check + engine API outbound |
| ST-02 | Russian service (category-ru) | Smart Split | Uses physical IP | IP check + route lookup + `Why This Route?` |
| ST-03 | Non-Russian domain | Smart Split | Uses VPN exit IP | IP check |
| ST-04 | User direct rule | Custom | Direct | Rule ID + interface |
| ST-05 | User block rule | Any | Blocked | Connection error + no packet |
| ST-06 | App path rule | Custom | App uses specified path | Process path + interface |
| ST-07 | Proxy-routed domain | Proxy Mode | Uses proxy | Proxy access log + exit IP |
| ST-08 | DNS for direct domain | Smart Split | Direct resolver | DNS server log |
| ST-09 | DNS for proxied domain | Smart Split | Tunnel resolver | DNS server log |
| ST-10 | IPv6 destination | Full Tunnel | Tunneled or blocked | IPv6 test + route lookup |
| ST-11 | IPv6 destination | Smart Split direct | Direct or blocked per policy | IPv6 test |
| ST-12 | Tunnel drop during transfer | Full Tunnel + Strict | Transfer stops; no direct fallback | Packet capture + IP check |
| ST-13 | Tunnel drop | Smart Split | RU direct continues; protected blocked | IP check |
| ST-14 | Server switch | Full Tunnel | New exit; rules unchanged | IP check |
| ST-15 | Wi-Fi → Ethernet | Smart Split | Policy preserved | IP check + interface |
| ST-16 | Sleep/resume | Full Tunnel | Reconnect; fail closed during gap | IP check + logs |
| ST-17 | Engine crash | Full Tunnel + Strict | Fail closed; watchdog restores or stays blocked | IP check + WFP state |
| ST-18 | UI crash | Full Tunnel | Engine policy remains; service unaffected | IP check |
| ST-19 | Service crash | Full Tunnel + Strict | Persistent filters remain or recovery works | IP check + recovery tool |
| ST-20 | Uninstall | Any | Routes/DNS/WFP/service cleaned | Route/DNS/WFP inspection |

---

## 4. Verification methods

### 4.1 Actual interface check

Use the Windows IP Helper API (`GetBestInterfaceEx` / `GetIpForwardTable2`) to determine
the interface a destination would use, and compare with the expected interface LUID. Do
not infer from a stored setting.

### 4.2 Actual exit check

Request a controlled echo endpoint through the connection and record the observed source
IP. Use two independent endpoints to avoid a single-service failure. Do not use a public
"what is my IP" service as the only source.

### 4.3 DNS check

- Query a unique random label under a test domain.
- Read the authoritative server log to see which resolver/IP queried it.
- Verify the query path matches the routing policy.
- Test both IPv4 and IPv6 resolvers.

### 4.4 WFP check

- Dump Fulbr filters with `netsh wfp show filters` or the WFP API.
- Confirm filters are present/absent in each state.
- Confirm fail-closed behavior without relying on the UI.
- Confirm cleanup after crash/recovery/uninstall.

### 4.5 Process check

- Use a signed test executable with a known path.
- Verify `process_path` matching, not just `process_name`.
- Include a short-lived process and a UWP app to document limitations.

---

## 5. Acceptance criteria

- No scenario sends protected traffic direct when the policy says tunnel/block.
- Direct-routed services use the physical interface and are labeled direct.
- DNS and IPv6 follow the same policy as the connection.
- A tunnel failure never silently falls back to direct for protected traffic.
- `Why This Route?` matches the measured interface in 100% of tested cases.
- Recovery restores normal networking without a reboot.
- Uninstall leaves no Fulbr routes, DNS settings, WFP filters, or service.

---

## 6. Known risks / open questions

- `strict_route` vs VirtualBox/Hyper-V.
- Process detection races for short-lived processes (upstream sing-box issue #2823).
- UWP/MSIX process identity.
- CDN/ECH classification.
- WFP filter ownership across service restart.
- DNS behavior when the user's ISP blocks foreign DoH.
- IPv6-only networks and NAT64.

---

## 7. Reporting

Results will be recorded in this file with:

- date/time,
- Windows build,
- engine version,
- network adapter,
- test command/target,
- observed result,
- pass/fail,
- artifacts (sanitized logs, no credentials).

Until then, the status remains **not executed**.
