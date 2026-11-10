# TUN Host Test — 2026-11-10

**Status:** Partial verification — **payload traffic through the TUN was not verified**
**Machine:** Windows 11 Pro build 26300, i5-11400F, 15.9 GB RAM
**Mode:** Temporary elevated `Fulbr.Service.exe` console (NOT installed as a service)
**Test config:** public VLESS REality server “Germany, Nuremberg | [BL]”
**Host note:** Happ/xray TUN (`happ-xray`, `172.19.0.1/30`) was active during the test.

---

## 1. What passed

| Check | Result |
|---|---|
| Service console + named-pipe IPC | Passed |
| `status` before start | `Disconnected` |
| `start` with TUN config | `ok:true`, `Connected` |
| TUN adapter | `Fulbr` / `sing-tun Tunnel` / `Up` |
| IPv4 default route | `0.0.0.0/0 -> 10.255.255.2` on `Fulbr`, metric 0 |
| IPv6 default route | `::/0 -> fdfe:dcba:9876:1::2` on `Fulbr`, metric 0 |
| TUN DNS addresses | `10.255.255.2`, `fdfe:dcba:9876:1::2` |
| Engine process | `engine-service\sing-box.exe`, ~55.8 MB |
| `stop` | `ok:true`, `Disconnected` |
| Adapter cleanup | Removed |
| Route/DNS restoration | Restored to Happ/Ethernet/ISP values |
| Service installation | None (expected) |
| WFP filters | None installed by Fulbr |
| Host network after test | Restored; default routes back to `happ-xray` + Ethernet |

The test also confirmed the address-collision fix: the original `172.19.0.1/30` conflicted
with Happ’s TUN and produced `set ipv4 address: The object already exists`. Fulbr now uses
`10.255.255.1/30` and `fdfe:dcba:9876:1::1/126`.

## 2. What failed

| Check | Result |
|---|---|
| `Find-NetRoute 1.1.1.1` | Interface `Fulbr` (route selection looked correct) |
| DNS query to `10.255.255.2` | Timed out |
| HTTPS request (`api.ipify.org`) | Timed out |
| Engine log | TUN started; diagnostic TCP connection only; **no TUN traffic flows logged** |

**Conclusion:** TUN adapter creation, routing, DNS assignment, and cleanup are verified.
The end-to-end payload path is **not** verified. Fulbr must not claim that TUN mode passes
traffic or protects DNS yet.

## 3. Likely causes to investigate

1. **Happ/xray TUN and WFP interaction** — Happ was active with its own default route and
   tunnel adapter `172.19.0.1/30`. Test in an isolated VM or with Happ disconnected.
2. **sing-box `strict_route` interaction** — test with `strict_route=false` in an isolated
   environment to distinguish WFP/interface filtering from the outbound itself.
3. **DNS bootstrap/outbound path** — inspect engine debug log
   `C:\ProgramData\Fulbr\logs\engine.log`; no TUN flow appeared, so this is likely before
   outbound DNS.
4. **VirtualBox VM blocker** — two clean Windows 11 26H2 installs stalled at ~42% with zero
   disk I/O. Try a different Windows image (Windows 10 22H2 / Windows 11 24H2/LTSC) or a
   different hypervisor before another VM test.

## 4. Safety/cleanup evidence

- No `FulbrSvc` service installed.
- No Wintun adapter remains.
- No `sing-box`/`Fulbr.Service` processes remain.
- Default routes returned to:
  - `0.0.0.0/0 via happ-xray`
  - `0.0.0.0/0 via Ethernet 192.168.0.1`
- System Restore checkpoint was created before the test.
- Logs are local; no VPN credentials were written to the test log.

## 5. Next test (do not skip)

- Use an isolated Windows VM with Happ disabled/uninstalled.
- Run the same service-console test.
- If it still fails, try `strict_route=false` only as a diagnostic, never as a release
  default.
- Capture engine debug logs and `Find-NetRoute`, DNS, and HTTPS results.
- Only then claim TUN payload support.
