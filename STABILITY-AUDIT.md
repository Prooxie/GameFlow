# GameFlow stability and release audit

Historical report. See the [1.0.9 quality report](docs/quality-report-1.0.9.md) for current evidence.

## Follow-up: HIDMaestro adapter and warnings, 22 October 2026

Reviewed the typed HIDMaestro 2.0.1 adapter at `50966ff` and its output sink.
The SDK remains separately supplied. This pass changed only GameFlow; the
sibling HIDMaestro checkout was not edited or rebuilt.

| Finding | Fix and evidence |
| --- | --- |
| The SDK search cooldown, unavailable-output retry and broken-pad replacement used wall-clock deadlines. Moving the clock forward could skip a cooldown; moving it backward could postpone recovery for hours. | Use `TimeProvider` monotonic timestamps for the 10-second, 45-second and 5-minute delays. Keep device activation timestamps in UTC for input-catalog ownership matching. Eight regression cases cover forward/backward jumps and the exact retry boundaries. |
| A throwing rumble subscriber escaped `Configure`/`DisposeAsync` and prevented later subscribers from receiving the final motor stop. | Attempt each subscriber separately, log its failure and still remove the pad. Two sink regression cases cover reconfiguration and disposal; the lifecycle test also checks that later subscribers receive zero. |
| 44 xUnit1051 warnings reported asynchronous operations that omitted the test cancellation token. | Pass `TestContext.Current.CancellationToken` through settings/profile operations, file reads, scheduled tasks and effect-service waits. File-handle cleanup still runs when cancellation occurs. No analyzer suppression was added. |

The 10 new regression cases failed against the previous behavior, with the
original 1,267 cases still passing. After the fixes, all **1,277 tests pass**
on Windows x64 with SDK 10.0.401, and the Release solution build passes with
warnings treated as errors: **0 warnings, 0 errors**. TRX reports are in
`TestResults/`.

The adapter's driver-install guard still latches both success and failure;
unchanged output templates still retain their pad or failure cooldown. The
existing tests for these behaviors, SDK absence/version handling, catalog
projection, state writing and rumble routing pass. These are automated tests
using read-only SDK discovery and fake pads, not a live driver-install or
USB/Bluetooth/rumble check. Hosted CI and native Linux/macOS execution were
not run by this follow-up.

The broader persistence, Lua isolation, transport, signing and hardware risks
listed below and in [UI-AND-RISK-REVIEW.md](UI-AND-RISK-REVIEW.md) remain open.
The earlier validation counts below describe the 21 October review.

## Original release audit, 21 October 2026

Reviewed on 21 October 2026 against `dev` at `229ca2f`, with the local patch
for **v1.0.7**. The patch is ready for code review and CI submission. A public
release still requires the hosted OS matrix and hardware checks below.

The review covers dependency advisories, repository history, input processing,
scripting, file persistence, third-party themes, HTTP/WebSocket and DSU services,
update downloads, packaging and GitHub workflows. It is a risk-oriented code
review with automated checks, not a proof that every defect or vulnerability
has been found. The separate HIDMaestro repository was read only; it was not
edited or rebuilt. GameFlow's own bridge received one logging correction.

## Verified fixes

| Severity | Problem and impact | Patch |
| --- | --- | --- |
| High | The current xUnit/MTP packages rejected the VSTest invocation on all three CI hosts. | Opt into Microsoft.Testing.Platform in `global.json` and both test projects; add the TRX extension and use the MTP command line. |
| High | Lua top-level code or `on_tick` could loop indefinitely and stop input processing. | Bound interpreted execution to 100,000 instructions, disable over-budget scripts, discard partial output, cap source length and validate button indices. Match cached scripts by their source rather than a string hash. |
| High | Theme paths could escape the themes folder and subsequently be served through the overlay asset endpoint. | Constrain resolved paths and ancestor search; reject network/drive paths and links beneath the theme root. Skip linked directories during theme discovery. |
| High | HTTP WebSockets accepted arbitrary origins and hostnames. A web page could attempt to drive a reachable local instance. | Require local hosts and same-origin browser requests; reject unknown routes. Remote access also requires explicit sharing and a revocable private token. |
| Medium | Unlimited HTTP sessions and DSU subscriptions allowed reachable clients to grow work without a bound. | Cap concurrent HTTP handlers and DSU subscribers at 64 each; retain the 16-phone limit and observe HTTP tasks on shutdown. |
| Medium | DSU used `WhenAll` without first cancelling siblings when a session loop ended. | Cancel the session on the first completed loop, then observe all loops. |
| High | Profile IDs were combined directly into filenames, and embedded IDs could control later saves. | Reject nonportable path characters, pin loaded profile identity to its filename, and recover invalid saved selections as missing. |
| Medium | Explicit JSON nulls could bypass non-null C# declarations and fail later in the runtime. | Enable nullable-annotation enforcement during deserialization; malformed profile files follow the existing preservation/recovery path. Collection element and semantic validation remain incomplete. |
| High | Recovering a missing active profile overwrote an existing customized default and left the old selection in settings. | Reuse the saved default and persist its ID as the active selection. |
| Medium | Atomic device-tuning saves could still complete out of order and replace newer state with an older snapshot. | Serialize snapshot capture and file replacement with a separate persistence lock, keeping disk I/O outside the read lock. |
| Medium | Theme refresh cleared asset paths but retained compiled overlay layouts. | Clear both caches on refresh. |
| Low | A missing SDK emitted both a bridge warning and a slot warning, causing the unavailable-backend regression test to fail on Windows. | Keep probe detail at Debug; the GameFlow output sink retains the actionable Warning. |
| Medium | Update filenames and URL schemes were trusted, downloads shared partial filenames, and the response-body read outlived HttpClient's header timeout. | Require safe filenames and HTTPS, isolate attempts, use a linked deadline, cap downloads and verify GitHub's SHA-256 digest. Keep download progress, cancellation and errors visible. |
| High | The self-contained SDK publish selected runtime 10.0.12 even though the current servicing release was 10.0.13. | Pin `RuntimeFrameworkVersion` to 10.0.13 and the minimum SDK to 10.0.401. |
| High | The release job never checked out the repository before reading its changelog. Downloading all artifacts also risked publishing test reports. | Check out the tagged source, download only `GameFlow-*` artifacts and fail if release notes or release assets are missing. |
| Medium | Locked restore fell back silently to unlocked restore; no locks were committed. | Add solution and per-RID lock files, remove the fallback, enforce locked publish and fail restore on NuGet vulnerability warnings. Pin CI actions to verified commit IDs. |
| Medium | Archives omitted release documentation and bundled-component notices. | Package documentation, exact dependency metadata, available upstream license files and framework notices; add SHA-256 sidecars and remove unneeded native PDBs. |
| Medium | Pages expected `docs/index.html`, but website source is held on another computer. | Check source availability before deployment and skip clearly when absent. The existing site is not redeployed until the source is restored. |

Existing changes after v1.0.6 are also included in the release notes: atomic
store writes, a single settings writer, damaged-profile recovery, provider
disposal resilience, exception logging, theme-path caching, reduced device
catalog churn and profile-edit ownership checks. See the commit range
[`v1.0.6...dev`](https://github.com/Prooxie/GameFlow/compare/v1.0.6...dev).

## Validation

The follow-up UI/runtime fixes and their remaining limitations are detailed in
[UI-AND-RISK-REVIEW.md](UI-AND-RISK-REVIEW.md), including monotonic scheduling,
Linux hot-plugging, input-failure releases, semantic validation, restored theme
tests, navigation/resource lookup and the Lua allocation measurement.

| Check | Result |
| --- | --- |
| Release solution build on Windows x64, SDK 10.0.401 | PASS; 0 errors. 44 existing xUnit cancellation-token analyzer warnings remain. |
| Unit and regression tests using the CI MTP command | PASS; 1,165 passed, 0 failed, 0 skipped. TRX reports are in `TestResults/`. |
| Locked solution restore | PASS. |
| Self-contained single-file publish | PASS for win-x64, linux-x64, osx-arm64 and osx-x64, including locked restore. These are local builds on Windows, not native execution on the other OSes. |
| Package preparation and archive construction | PASS locally for all four targets; documentation and dependency notices are included. |
| NuGet advisory scan | PASS; no known vulnerabilities reported for direct or transitive packages in all five projects. |
| Gitleaks 8.30.1 | PASS; no leaks reported in full reachable history (95 scanned commits), source, tests, workflows or the tracked patch. |
| actionlint 1.7.12 | PASS for `ci.yml` and `pages.yml`. |
| Release-notes extraction | PASS; the exact `## v1.0.7` section exists and is nonempty. |
| Git whitespace checks | PASS. |
| Hosted Windows/Linux/macOS CI for this patch | NOT RUN; the patch has not been pushed. The previous dev run failed at the now-corrected VSTest/MTP boundary. |
| Real devices, Bluetooth, rumble, OBS and native Linux/macOS behavior | NOT VERIFIED by this session. |
| Website build | NOT VERIFIED; source is unavailable on this computer. Missing-source behavior is explicit in the workflow. |

The advisory result applies to the current NuGet advisory database. It does not
independently certify every embedded SDL, Skia, ANGLE, HarfBuzz or OS component.
Gitleaks and actionlint were downloaded into the temporary directory and checked
against their upstream release checksums. No security alerts or repository
settings were changed. Container, infrastructure-as-code and database attack
surfaces are not present in this desktop application.

## Remaining risks and follow-up work

The follow-up review fixed default network exposure, pairing/revocation, wall-clock
scheduling, Linux input hot-plugging and held-state cleanup, semantic profile
validation, update verification and several UI failures. See
[UI-AND-RISK-REVIEW.md](UI-AND-RISK-REVIEW.md) for the findings, measured Lua
allocation reduction, UI checks and the complete prioritized follow-up list.

The material open risks are hostile-script memory/native-call isolation,
unencrypted HTTP/DSU transport, synchronous store writes and save-failure feedback,
residual allocation at high slot/rule counts, unsigned releases, native/asset
provenance, branch protection, and real-device/native-OS verification. The website
source is still unavailable on this computer. These are explicit limitations;
a passing unit suite does not close them.

MoonSharp remains on upstream stable 2.0.0. A beta upgrade would not establish
hostile-script isolation. No speculative dependency major upgrade is included.

## Release procedure

1. Review this patch and the `v1.0.7` section in `CHANGELOG.md`. Run the hosted
   Windows, Linux and macOS build/test matrix on the final commit.
2. Smoke-test controller input, profile save/restart, device disconnect,
   virtual output, rumble, the phone page and OBS. Verify the exact separately
   supplied HIDMaestro release on Windows with Administrator rights.
3. Merge the verified commit to `master`. Create and push `v1.0.7` from the
   intended master commit. This triggers all four publish jobs and then the
   GitHub release using the curated changelog section.
4. Check that the release contains exactly four platform archives and their
   four `.sha256` files, with no test-result attachments. Download and hash at
   least one asset per platform; verify executable permissions on Unix hosts.
5. Restore the website's `docs/index.html` and associated assets from the other
   computer when available. Pages deployment then resumes on matching pushes.

The version and notes are prepared locally. No commit, push, merge, tag or
GitHub release was created by this audit.

## Primary references

- [Microsoft MTP migration](https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform)
- [MoonSharp preemptive coroutine limitations](https://www.moonsharp.org/coroutines.html)
- [.NET October 2026 servicing release](https://devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-october-2026-servicing-updates/)
- [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [AL2009man asset license and redistribution guidance](https://github.com/AL2009man/Gamepad-Asset-Pack)
- [HIDMaestro upstream requirements](https://github.com/hifihedgehog/HIDMaestro)
