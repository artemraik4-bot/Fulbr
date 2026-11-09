# Persistent WFP Kill-Switch Design

**Status:** design only — **not implemented**
**Date:** 2026-11-09
**Scope:** service-owned Windows Filtering Platform filters for TUN mode.

---

## 1. Why `strict_route` is not enough

sing-box's Windows `strict_route` adds WFP filters for the **engine process** to block
traffic outside the TUN and prevent multihomed DNS leaks. Those filters are tied to the
running engine. If the engine crashes, the filters can disappear, creating a leak window
until the service restarts it.

A fail-closed kill switch must be owned by the **service**, survive an engine crash, and be
removed only on a clean disconnect, explicit recovery, or uninstall.

---

## 2. Policy modes

| Mode | Behavior |
|------|----------|
| **Strict** | Protected traffic may only use the TUN. Direct-routed destinations are also blocked. |
| **Smart Split** | Protected traffic may only use the TUN; explicitly direct-routed destinations may use the physical interface. |
| **Manual Recovery** | User must approve any policy change after a failure. |
| **Disconnected** | No Fulbr filters. Normal networking. |

The service stores the selected mode; it never changes it silently.

---

## 3. Filter model

At the ALE connect layers (`FWPM_LAYER_ALE_AUTH_CONNECT_V4` and `_V6`), the service
installs a high-weight sublayer with:

1. **Permit loopback** — 127.0.0.0/8, ::1.
2. **Permit DHCP** — UDP 68→67 and 546→547, if required for the physical network.
3. **Permit the engine process** — by `ALE_APP_ID` for `sing-box.exe` (proxy server
   connection and TUN I/O).
4. **Permit traffic whose local interface is the TUN LUID** — this is the key rule that
   allows applications routed into the TUN while blocking the physical path. It must match
   the actual interface LUID, not a name.
5. **Permit explicit direct-routed destinations** only in Smart Split mode (IP/CIDR + port
   rules, derived from the routing policy).
6. **Block all other outbound IPv4/IPv6** connections.

Additional considerations:

- Use `FWPM_SUBLAYER0.Weight = 0xFFFF` (maximum) and stable provider/sublayer GUIDs.
- Add filters in a transaction (`FwpmTransactionBegin0` / `Commit`) so a partial policy is
  never active.
- Give every filter a display name/description (`Fulbr: ...`) and a stable filter ID/key
  where possible, for audit and cleanup.
- Do not filter inbound traffic beyond what is necessary; the kill switch is about
  preventing outbound leaks.
- Do not inspect or modify payloads. Fulbr is not a DPI tool.
- Re-check the TUN LUID after every network change, engine restart, sleep/resume, and
  service restart. If the LUID changes, update the permit filter atomically.
- On clean disconnect: remove all Fulbr filters, then restore routes/DNS.
- On service crash: filters remain installed (fail closed). The service recovery tool must
  remove them without relying on the UI.
- On uninstall: remove filters, TUN adapter, routes, DNS, service, and files.

---

## 4. State machine

```
Disconnected
  └─ user connect ─> InstallingFilters (block protected traffic)
        ├─ success ─> ConnectingEngine
        │                ├─ TUN + end-to-end check pass ─> Protected
        │                └─ fail ─> ProtectionLost (filters remain)
        └─ failure ─> Disconnected (filters removed)
Protected
  ├─ engine crash ─> ProtectionLost (filters remain; watchdog restarts engine)
  ├─ network change ─> Reconnecting (filters remain)
  ├─ user disconnect ─> RemovingFilters ─> Disconnected
  └─ service crash ─> filters remain; restart service → recover/reinstall
```

`Protected` is shown only after the TUN interface, routes, DNS, filters, and an end-to-end
check are verified. Never show `Protected` based on a process being alive.

---

## 5. Recovery

The service exposes a `recover` command that:

1. Stops the engine.
2. Removes all Fulbr WFP filters by provider/sublayer GUID.
3. Removes the Fulbr TUN adapter if present.
4. Restores saved routes/DNS if a snapshot exists.
5. Flushes the DNS cache.
6. Returns a report of what was changed.

A standalone `FulbrRecovery.exe` (or the service in a recovery mode) must work without the
UI and without the engine. It must not run `netsh winsock reset` or `netsh int ip reset`
as a first-line action.

---

## 6. Test matrix (VM)

| ID | Test | Expected |
|----|------|----------|
| KS-01 | Connect in Strict | Protected traffic uses TUN; direct attempts blocked |
| KS-02 | Kill engine | Filters remain; protected traffic blocked; service reports ProtectionLost |
| KS-03 | Kill service | Filters remain; networking not open; restart recovers |
| KS-04 | Reboot with filters present | Service restarts and reconciles policy; no orphan leak |
| KS-05 | Sleep/resume | TUN LUID revalidated; filters updated; protected state verified |
| KS-06 | Wi-Fi → Ethernet | LUID/interface change handled; filters updated |
| KS-07 | Smart Split direct destination | Direct traffic allowed only for the explicit rule |
| KS-08 | DNS test | No protected DNS query escapes to ISP |
| KS-09 | IPv6 test | IPv6 is tunneled or blocked, never direct |
| KS-10 | Clean disconnect | All filters removed; routes/DNS restored |
| KS-11 | Uninstall | No filters, adapter, service, routes, or DNS left |
| KS-12 | Recovery tool | Removes orphan filters and restores networking without reboot |

Results must be recorded. Until then, do not claim fail-closed protection.

---

## 7. Risks

- WFP filter conflicts with other VPN/firewall/anti-cheat products.
- Interface LUID changes and stale filters.
- Boot-time/DHCP traffic blocked before the engine starts.
- IPv6 dual-stack edge cases.
- Direct-rule mistakes exposing protected traffic.
- Service crash leaving filters installed (intended fail-closed, but recovery must work).
- Driver/anti-cheat interaction with Wintun/WFP.

Each risk needs a VM test and a documented recovery path.
