# Next Steps

**Date:** 2026-11-10
**Current state:** 0.2.0 proxy mode works and is installed. 0.3.0 service/TUN code is
built; TUN adapter/routes/DNS/cleanup were verified in a temporary elevated host test, but
payload DNS/HTTPS through the TUN timed out and is **not verified**. No service or WFP is
installed on the host.

---

## Service/TUN (in progress)

- [x] `Fulbr.Service` host + ACL'd named pipe
- [x] `ping`/`status`/invalid-link IPC smoke tests
- [x] TUN config builder + pinned-engine `sing-box check`
- [x] Wintun 0.14.1 verified and bundled
- [x] Host TUN adapter/routes/DNS/cleanup test (temporary, elevated console; no service install)
- [ ] TUN payload DNS/HTTPS path (timed out; see `docs/development/tun-host-test-2026-11-10.md`)
- [ ] Windows VM install (both 26H2 attempts stalled at ~42%; VMs saved)
- [ ] Persistent service-owned WFP kill switch
- [ ] Service install in the main installer (admin, separate approval)

## Windows test VM (blocked)

- ISO is verified: 8,225,329,152 bytes, SHA-256
  `BC3F24086EBADC94489066B5AD78089E2CF5C3491E90E790BB81A2B199C10E38`.
- VMs `FulbrWin11` and `FulbrWin11B` are saved, not deleted. Both Windows 11 26H2 installs
  stalled at ~42% with no disk I/O.
- Next: try Windows 10 22H2 or Windows 11 24H2/LTSC, or a different hypervisor. Do not
  retry the same 26H2 image unchanged.
- The host has no Fulbr service, persistent Wintun adapter, or WFP filter.

## Next engineering

1. **TUN payload path** — repeat in an isolated Windows VM with Happ disabled; capture
   `engine.log`, `Find-NetRoute`, DNS, HTTPS. Test `strict_route=false` only as a diagnostic.
2. **Persistent kill switch** — service-owned WFP filters; fail closed across engine/service
   crashes; recovery tool.
3. **UI integration** — mode selector, service detection, honest protected states.
4. **DNS/IPv6 leak tests** — real results only.
5. **Routing** — Full Tunnel / Smart Split / Custom / Proxy / Direct; Russian direct lists.
6. **Lifecycle** — protocol probes, backoff, quarantine, outage detection.
7. **Release** — VM installer test, code signing if available, SBOM, source archive.

## Do not do yet

- Do not install a Windows service, Wintun driver, or WFP filter without explicit approval.
- Do not modify Windows proxy/DNS settings.
- Do not claim kill-switch, DNS-leak, or split-tunnel protection.
- Do not publish the repository.

## Open questions

1. Approve the 0.2.0 installer upgrade?
2. Is a code-signing certificate available, or should 0.2.0 remain explicitly unsigned?
3. When TUN work starts, should the service run as LocalSystem or a dedicated service
   account? (Security review required.)
