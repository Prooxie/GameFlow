# Remaining-risk and interface review

Reviewed 21 October 2026 on `dev`, including the uncommitted v1.0.7 patch.
HIDMaestro's separate repository was not edited or rebuilt. This review adds
concrete fixes and a prioritized backlog; it does not certify that every
possible defect has been found.

## Fixed in this follow-up

| Finding | Result |
| --- | --- |
| Main navigation could become `-1`, leaving a blank page. Hidden TabItems were no longer selectable. | Hide only the header presenter; keep navigation selected. Reproduced and verified with actual views under Avalonia Headless. |
| Starting from a different working directory displayed translation keys. | Resolve resources from the installation directory. Verified translated labels from an isolated temporary working directory. |
| “Download & install” closed immediately, gave no progress/failure feedback and did not install anything. | “Download update” stays open with progress, cancellation, retry, verified completion, installation instructions and Done. No duplicate background download. |
| Downloads were not automatically verified. | Require GitHub's SHA-256 asset digest before committing the final archive. Missing/mismatching digests fail closed; old releases without digests need manual download. |
| Remote phone/overlay access was available without a deliberate sharing choice or secret. | Sharing defaults off; remote requests need a 256-bit random private link. Disable sharing or reset the link to revoke existing remote sessions. Local OBS URLs use localhost. Remote overlay image requests carry the same token. |
| DSU exposed a UDP service beyond local emulators. | Bind loopback by default; rebind to other interfaces only after explicit opt-in. |
| Motion settings writes could complete out of order; invalid port text could prevent stopping sharing. | Ordered revisions, visible save errors, port commit on focus loss; disabling remains possible with invalid port text. |
| Settings mixed pending edits with immediate effects, and errors were truncated. | Explain immediate changes, label the main action Save and close, expose window/polling controls, validate rates and wrap errors. General restore-defaults edits remain pending until saved. |
| The global polling override was saved but not used by the runtime. | Apply it to both the runtime scheduler and rate-aware input sources, including profile/provider changes. |
| Dialog minimum sizes exceeded smaller screens; controller tuning required a mouse. | Fit dialogs to the owner's working area, including DPI; add Enter/Space access and visible focus. Show the actual cross-platform theme directory in missing-theme instructions. |
| A failed read left a previously pressed virtual button held. | Attempt a direct neutral output after failures, bypassing mapping rules. Surface failures and restore normal status after recovery. An unresponsive sink cannot be guaranteed to accept a release. |
| Wall-clock changes could extend a pulse or runtime wait. | Schedule from monotonic elapsed time; verify backward/forward clock jumps with an injected clock. |
| Linux evdev readers did not handle hot-plugging, retained held state after read failure and relied on cross-thread fd closure. | Rescan once per second, neutralize removed/failed readers, poll nonblocking fds with bounded waits, and close each fd on its owning reader thread. Native Linux validation remains required. |
| Invalid profile collection elements, enums, non-finite values and duplicate rule IDs could reach the runtime. | Validate profile structure, nesting/count/size bounds and numeric validity. Limit imported files to 2 MiB before unbounded reads. Imported Lua starts disabled with an explanation. |
| Failed saves changed the live profile before reaching disk; save/delete could race profile selection. | Commit the in-memory profile only after persistence; share the profile-operation gate. |
| Three theme test files were excluded after the Autofire rename. | Port namespaces and button-mask construction and restore the tests to the suite. |

## Measured Lua allocation bottleneck

The bounded interpreter originally created a coroutine on every tick. MoonSharp
2.0 allocates approximately 2 MiB of VM stack for each coroutine. The patch now
reuses one coroutine per loaded rule, with a private completion marker to reject
partial output and unexpected yields. Instruction limits still apply per tick.
Runtime errors discard output and retry at most once every five seconds.

A local diagnostic used Release/.NET 10.0.13 on Windows x64, Intel Core 7 240H:
2,000 warm-up calls followed by 16,000 `ControllerMappingPipeline.Process` calls,
one unchanged input snapshot and either no rules or one `ctx.press('South')`
script. Allocation was measured with `GC.GetAllocatedBytesForCurrentThread`.

| Case | Before | After |
| --- | ---: | ---: |
| No rules, bytes per call | 968 | 968 |
| One simple Lua rule, bytes per call | 2,105,310 | 7,953 |
| One simple Lua rule, total elapsed for 16,000 calls | 1,176.89 ms | 68.62 ms |

These are a local micro-check, not device latency, sustained throughput or an
end-to-end 16-controller benchmark. The remaining scripted allocation would be
about **121 MiB/s** at 16 slots × 1,000 Hz with one such rule per slot. Cold
compilation and retained per-rule stacks are additional costs. A broad allocation
regression guard catches reintroduction of a VM stack per frame without relying
on a timing threshold. Table/callback pooling still needs profiling and lifetime
tests; retained Lua references must not accidentally observe a later frame.

## Validation and limits

- Release solution build, locked restore and **1,165 tests** pass locally on
  Windows. Existing xUnit cancellation-token analyzer warnings remain.
- Headless rendering checked the actual Settings, mapping, dashboard, tuning
  and update views. Interaction checks covered default navigation, keyboard
  tuning activation and update failure/cancellation/completion. The isolated
  harness did not start hardware services or use the normal user-data folder.
- Mapping layout was inspected at 1,000 × 650. Headless rendering cannot verify
  native title bars, multi-monitor behavior, screen readers or real input delay.
- This is a one-off UI harness, not a permanent screenshot baseline in CI.
  Published builds still require native OS and controller smoke tests.
- The local four-target publish/package checks and scanner results are recorded
  in [STABILITY-AUDIT.md](STABILITY-AUDIT.md). Hosted CI has not run this local patch.

## Remaining risks and opportunities

| Priority | Remaining evidence / consequence | Next concrete step |
| --- | --- | --- |
| High for hostile scripts | Lua is in-process. Memory growth and native-library/reentrant calls are not bounded by the instruction counter. Retained coroutine stacks also grow with enabled rule/slot count. | Keep imported scripts disabled until reviewed. Use process isolation and enforce memory/time limits before offering hostile-script safety. |
| High on hostile networks | HTTP private links are bearer secrets sent without encryption; DSU remote mode has no authentication/encryption. Sharing consent does not make public/shared networks safe. | Keep remote mode for trusted networks. Add authenticated TLS/pairing or a documented secure tunnel before supporting hostile networks. |
| Medium | Slot, calibration, template and device stores still write synchronously on some UI mutation paths. Several log failures after applying an in-memory change, so restart can reveal an unsaved configuration. | Add a shared visible persistence status and retry/dirty state; preserve ordering and crash safety before adding coalesced background writes. Exercise full-disk, read-only-directory and interrupted-save failures. |
| Medium | Lua contexts and ordinary pipeline results still allocate per tick; many active scripts/slots amplify GC and retained-memory pressure. | Profile sustained 16-slot sessions, GC pauses, hot-plugging and live edits on target hardware. Measure before changing snapshot/context lifetimes; use lower polling rates where appropriate. |
| Medium | Current tests do not run physical USB/Bluetooth input, rumble, adaptive triggers, OBS, evdev or macOS permission flows. Linux/macOS virtual gamepad output remains Preview only. | Run the final hosted three-OS CI matrix and a native hardware checklist with the separately supplied HIDMaestro build before tagging master. |
| Medium | GitHub master has no branch protection in the previous repository inspection. | Maintainer should require the Windows/Linux/macOS checks and review before merge. No repository policy was changed. |
| Medium | Archives are unsigned; macOS is not notarized. A GitHub-supplied checksum detects corruption, not compromise of the release publisher/account. | Add publisher signing/notarization and provenance attestations using maintainer-controlled credentials. |
| Medium | Bundled theme source revisions and all embedded native subcomponent notices are not exhaustively established. | Record upstream asset revisions and finish the native/component provenance review before making comprehensive redistribution/compliance claims. |
| Medium for usability | Some network, mapping and startup text is still English in other locales. Custom painted controls and focusable tuning borders need native screen-reader and high-contrast checks. | Localize remaining strings, expose proper invocation/automation semantics, and add a dedicated headless UI test project plus native accessibility smoke tests. |
| Low | Phone link selection picks one best-effort local address; VPNs, multiple NICs and guest Wi-Fi isolation can still make it unreachable. | Offer an interface/address picker and a connection diagnostic. Automatic firewall setup cannot resolve router isolation. |
| Low | Auto-save forms and nested scroll regions still need broader keyboard, long-translation and small-screen testing beyond the checked dialogs. | Add interaction coverage for each rule type and advanced tuning section, including focus order, validation and reset semantics. |
| External blocker | Website source is on another computer and absent here. | Restore the real source before verifying Pages content; the workflow explicitly skips a missing source instead of fabricating a site. |

The most useful next development work is visible persistence failure/retry and
permanent UI interaction coverage. Signing, website restoration, branch policy
and native hardware validation require their respective external inputs.

## References

- [MoonSharp coroutine behavior and preemption caveats](https://www.moonsharp.org/coroutines.html)
- [GitHub release-asset digest metadata](https://docs.github.com/en/rest/releases/releases)
- [Avalonia Headless rendering and input simulation](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform)
