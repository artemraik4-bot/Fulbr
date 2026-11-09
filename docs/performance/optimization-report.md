# Optimization Report (Prototype)

**Status:** Initial observations, no tunnel yet
**Date:** 2026-11-09

---

## 1. Current observations

| Area | Observation | Action |
|------|-------------|--------|
| Startup | ~880–924 ms to window | WPF/XAML load dominates. Acceptable for v0.1; measure again after engine integration. |
| Idle memory | ~127 MB working set / ~79 MB private | Acceptable for a self-contained WPF app. Monitor with 1k+ servers. |
| Idle CPU | 0% in a 4 s sample | No background polling in the prototype. Preserve this. |
| Installer | ~46 MB | Self-contained runtime. Consider a framework-dependent variant or trimming later. |
| Server list | 67 items, virtualized ListView | Enable recycling (already set). Measure 1k/10k. |
| Subscription import | One fetch + parse of 67 entries completes quickly on this machine | Add explicit timing metrics before claiming a number. |
| Probe batch | 67 TCP/TLS probes at concurrency 4, 6 s timeout | Completed within the 100 s test window. Measure precisely and reduce timeout per method. |
| Proxy engine RAM | ~39 MB working set (38.8–39.6 MB observed, one sing-box session) | Separate process; acceptable. Measure with TUN and high throughput. |
| Proxy connect | ~4.7 s to the first successful candidate, including two failed candidates | Each candidate includes engine start + HTTP check. Tune timeouts and candidate count. |
| Proxy disconnect | sing-box stopped; runtime config deleted | Verified. Keep as a regression test. |

## 2. Deliberate non-optimizations

- No connection pooling for subscription fetches: sources are few and refresh infrequently.
- No background timer for subscriptions yet: the 6-hour schedule is designed but not
  implemented. This avoids unnecessary wakeups in the prototype.
- No fake-IP DNS cache: it complicates diagnostics and is not justified yet.
- No UI virtualization for the source/probe/log lists: they are small; add if needed.

## 3. Risks to watch

- **Large subscriptions**: a 10k-server list may make `ObservableCollection` updates and
  filtering expensive. Move filtering/paging to a virtualized data source if measured.
- **Probe fan-out**: 4 concurrent probes with 6 s timeouts is polite; do not increase
  blindly. Respect targets and rate limits.
- **DPAPI save cost**: state is rewritten on every significant action. Debounce or move to
  SQLite with incremental writes before the data set grows.
- **Self-contained size**: 46 MB installer is acceptable, but update bandwidth matters.
  Use delta updates or a framework-dependent option later.

## 4. Next optimization targets

1. Replace full-state JSON saves with SQLite incremental writes.
2. Add precise timers for import and probe phases.
3. Measure 1k/10k server lists and implement paging if needed.
4. Measure engine startup and tunnel overhead.
5. Profile WPF rendering at 100–200% DPI.
6. Add a battery/polling policy for health checks.

No optimization is claimed without a before/after measurement.
