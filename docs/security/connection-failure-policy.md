# Connection Failure and Safe Fallback Policy

**Status:** Phase 2 design
**Date:** 2026-11-09

---

## 1. Safety modes

The user selects one mode; Fulbr never changes it silently.

### Strict Protection

- Protected traffic must **never** use the physical interface while Fulbr is connecting,
  connected, reconnecting, or in a protection-lost state.
- If the tunnel fails, protected traffic is blocked until recovery or explicit manual
  recovery.
- Direct-routed services may also be blocked; this is the safest interpretation.

### Smart Split Protection (recommended default)

- Explicitly direct-routed services (e.g., selected Russian services) may continue using
  the physical interface when the tunnel fails, because that is the user's chosen policy.
- All other traffic remains blocked.
- The UI must clearly show which traffic is still direct.

### Manual Recovery

- After a failure, Fulbr keeps the last policy but requires the user to approve any change
  (retry, switch server, or disable protection).
- No automatic fallback to direct internet.

---

## 2. Failure timeline

| Event | Behavior |
|-------|----------|
| Connect requested | Enter `Connecting`; install fail-closed filters before starting the engine; show "Internet blocked while connecting" if strict. |
| Engine starts | Permit engine endpoint + TUN; keep protected traffic blocked until end-to-end check passes. |
| End-to-end check fails | `Error`/`ProtectionLost`; keep filters; offer retry/switch/recovery. |
| Tunnel drops | `Reconnecting`; keep filters; retry with bounded exponential backoff (1s, 2s, 4s, 8s, 15s, 30s, 60s max). |
| Engine crash | Service keeps filters; watchdog restarts engine; if repeated, `ProtectionLost`. |
| Service crash | Persistent WFP filters (if installed) remain; watchdog/SCM restarts service; recovery command available. |
| Sleep | Before suspend: note state. On resume: `Reconnecting`, re-detect interface, re-bind, re-verify. |
| Network change | `Reconnecting`; re-detect physical interface; re-verify routes/DNS/WFP. |
| User disconnect | `Disconnecting`; stop engine, remove filters, restore routes/DNS, flush cache, then `Disconnected`. |
| Uninstall | Same cleanup, plus service removal and file cleanup. |

---

## 3. Bounded restart policy

- Max 5 automatic attempts in 10 minutes, then `ProtectionLost`.
- Jittered backoff to avoid synchronized retries.
- Do not restart if the failure is `InvalidConfig`, `AuthFailure`, or a repeated TLS
  verification failure; surface the error instead.
- A successful end-to-end check resets the counter.
- Never auto-switch to a different server without user permission if the mode is
  `Manual Recovery`; `Smart`/`Strict` may offer "best server" only if the user enabled
  automatic failover.

---

## 4. Emergency recovery

A `Fulbr Recovery` tool (also available from the installer directory and Start Menu) must:

1. Stop the Fulbr service.
2. Remove Fulbr-owned WFP filters (by stable provider/sublayer key).
3. Remove Fulbr TUN adapter if present.
4. Restore saved routes/DNS state if available.
5. Flush the DNS cache.
6. Report what was changed.
7. Never reset the entire network stack (`netsh winsock reset`, `netsh int ip reset`) as a
   first-line action; those are last-resort, disruptive, and require explicit confirmation.

The recovery tool must work without the UI and without the engine.

---

## 5. User-visible states

| State | Message | Internet expectation |
|-------|---------|----------------------|
| `Disconnected` | "Not connected" | Normal |
| `Connecting` | "Connecting — protected traffic is blocked" | Blocked (strict) |
| `Connected` | "Protected — verified" | Tunnel + direct rules |
| `Reconnecting` | "Reconnecting — protected traffic is blocked" | Blocked |
| `ProtectionLost` | "Protection lost — traffic blocked. Recover?" | Blocked |
| `Error` | Specific error + recovery actions | Depends on mode; show it |
| `Direct` | "Direct connection — no VPN protection" | Normal, explicitly unprotected |

Never display `Connected` based on a process being alive or a timer. The end-to-end check
must pass.

---

## 6. Test requirements

- Pull the network cable / disable Wi-Fi during a transfer.
- Kill the engine process.
- Kill the service process.
- Sleep/resume.
- Switch Wi-Fi/Ethernet.
- Change DNS servers while connected.
- Uninstall while connected.
- Verify no direct fallback with packet capture and IP checks.

Results will be recorded in `docs/security/split-tunneling-verification.md` after
execution.

---

## 7. Proxy-mode caveat (0.2.0)

Proxy mode has **no kill switch** and does not change system routes. If the engine stops,
applications configured to use the local proxy fail to connect through it; applications
that do not use the proxy continue direct, as before. This is expected and must not be
described as "protected". TUN + service + WFP are required for fail-closed behavior.
