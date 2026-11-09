# Next Steps

**Date:** 2026-11-09
**Current state:** 0.2.0 proxy mode works and is installed. 0.3.0 service/TUN code is built
and IPC-tested; TUN runtime verification is pending the Windows 11 VM installation.

---

## Service/TUN (in progress)

- [x] `Fulbr.Service` host + ACL'd named pipe
- [x] `ping`/`status`/invalid-link IPC smoke tests
- [x] TUN config builder + pinned-engine `sing-box check`
- [x] Wintun 0.14.1 verified and bundled
- [ ] Windows 11 VM install (official ISO downloading)
- [ ] TUN adapter/route/DNS test in VM
- [ ] Persistent service-owned WFP kill switch
- [ ] Service install in the main installer (admin, separate approval)

## Windows test VM (approved)

- Official Windows 11 Enterprise Evaluation ISO is downloading via BITS.
  - Source: Microsoft evaluation CDN
  - Size: 8,225,329,152 bytes
  - Expected SHA-256: `BC3F24086EBADC94489066B5AD78089E2CF5C3491E90E790BB81A2B199C10E38`
- VirtualBox VM `FulbrWin11` created: EFI, 4 GB RAM, 4 vCPU, 64 GB disk, NAT.
- Next: verify the ISO hash, attach it, install Windows, deploy `artifacts\service`, and
  run the TUN/WFP checklist in `docs/architecture/service-ipc.md`.
- The host remains untouched: no Fulbr service, Wintun driver, or WFP filter is installed.

## Next engineering after VM verification

1. **Persistent kill switch** — service-owned WFP filters; fail closed across engine/service
   crashes; recovery tool.
2. **Tunnel verification** — end-to-end HTTP check before "Protected"; sleep/resume,
   Wi-Fi/Ethernet, adapter changes, forced crashes.
3. **Routing** — Full Tunnel / Smart Split / Custom / Proxy / Direct; Russian direct lists.
4. **DNS/IPv6** — DNS rules aligned with routing; real leak tests.
5. **Lifecycle** — protocol probes, backoff, quarantine, outage detection.
6. **Release** — VM installer test, code signing if available, SBOM, source archive.

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
