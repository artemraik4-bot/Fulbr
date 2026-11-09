# Performance Benchmarks (Preliminary)

**Status:** Measured prototype, **not a VPN throughput benchmark**
**Date:** 2026-11-09
**Machine:** Windows 11 Pro build 26300, Intel i5-11400F (6C/12T), 15.9 GB RAM
**Build:** Fulbr 0.1.0 prototype, self-contained win-x64
**Data set:** 67 imported servers (47 VLESS, 18 VMess, 2 Trojan), no active tunnel

---

## 1. Method

- **Startup:** stop the process, start `Fulbr.exe`, poll every 25 ms until a non-zero
  `MainWindowHandle` appears. Three runs.
- **Memory:** after startup + 2 s idle, read `WorkingSet64` and `PrivateMemorySize64`.
- **Idle CPU:** sample `TotalProcessorTime` over 4 s and divide by elapsed time and logical
  processor count. Three runs.
- **Installer size:** file length of the compiled `Fulbr-Setup.exe`.
- **Not measured:** VPN throughput, tunnel latency overhead, connection time, server
  switching, 8-hour soak, DNS/leak behavior. Those require the engine and tunnel.

No values below are estimates or marketing claims.

---

## 2. Results

| Metric | Run 1 | Run 2 | Run 3 | Notes |
|--------|------:|------:|------:|-------|
| Startup to window (ms) | 924 | 879 | 880 | Cold-ish process start; includes WPF/XAML load |
| Working set at idle (MB) | 127.3 | 127.0 | 127.0 | 67 servers loaded, Dashboard |
| Private memory at idle (MB) | 79.7 | 78.7 | 78.6 | |
| Idle CPU (% of total) | 0.00 | 0.00 | 0.00 | 4 s sample, no animations |
| Installer size (0.1.0) | 46,083,615 | — | — | Self-contained .NET 10 runtime, no engine |
| Installer size (0.2.0) | 68,526,779 | — | — | Includes pinned sing-box 1.14.4 + libcronet |
| `Fulbr.exe` size (bytes) | 192,000 | — | — | App host only; runtime is beside it |

Environment: .NET 10.0.403 SDK, runtime 10.0.14, self-contained publish, satellite
resources limited to `en;ru`.

---

## 3. Proxy-mode observations (0.2.0, preliminary)

These are operational observations, not controlled benchmarks:

| Observation | Value | Method |
|-------------|-------|--------|
| sing-box working set (one session) | ~39.6 MB | `Get-Process sing-box` during a connected VLESS session |
| Time to first successful proxy connection | ~4.7 s | Timestamped logs; includes two failed candidate attempts and their HTTP checks |
| Local proxy endpoint | `http://127.0.0.1:60602` (example from that run) | UI |
| HTTP-through-proxy check | Passed | `https://cp.cloudflare.com/generate_204` via authenticated local proxy |
| Disconnect cleanup | Engine stopped; runtime config deleted | Process list + runtime directory check |

Caveats: one run, one network, one machine; the first two candidates failed, so the 4.7 s
figure includes failure and retry. It is not a connect-time guarantee.

## 4. What these numbers mean

- WPF + .NET 10 is viable for an always-on desktop client: ~0.9 s startup and ~127 MB
  working set on this machine with 67 servers is acceptable for a first prototype.
- Idle CPU is effectively zero with no active timers or polling. This matches the design
  goal of no continuous background work.
- The self-contained installer is ~46 MB. This is the cost of not requiring a machine-wide
  .NET install. It can be reduced later with trimming or by offering a framework-dependent
  build.

## 4. What these numbers do **not** mean

- They do not measure VPN throughput, latency, or battery impact.
- They do not prove performance with 1,000+ servers or with a live tunnel.
- They do not validate split tunneling, DNS, WFP, or kill-switch overhead.
- They were measured on one machine, not a controlled benchmark lab.

## 5. Next measurements

- Connect/disconnect time per protocol.
- Tunnel throughput and latency overhead.
- 1k/10k server list memory and filter time.
- Health-check CPU/network overhead.
- 8-hour soak with reconnects and network changes.
