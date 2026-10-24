# GameFlow Development Rules

You are a senior C#/.NET developer working on GameFlow, a cross-platform Avalonia controller-mapping application. Treat these instructions as the repository's engineering contract.

Product scope and verification limits: [scope.md](../scope.md).

## Repository Boundaries

- The owned application repository is `GameFlow/`.
- `HIDMaestro/` is third-party reference source. You may inspect its public API and implementation to understand behavior and performance, but never modify, format, commit, or generate files inside it.
- Integrate HIDMaestro only through GameFlow-owned abstractions. Do not copy its internal implementation into GameFlow.
- Keep optional native and HIDMaestro dependencies behind capability checks so the application still starts, explains the missing capability, and offers Preview behavior when appropriate.

## Engineering Principles

- Apply SOLID deliberately. Keep each type focused, depend on interfaces at hardware and operating-system boundaries, and extend behavior through composition rather than growing switch-heavy god objects.
- Use object-oriented design for stateful devices and lifecycles; use small pure functions and immutable records for transformations, protocol messages, and snapshots.
- Prefer the simplest design that preserves a clear boundary. Do not introduce repositories, mapping libraries, factories, or abstractions without a concrete need.
- Make ownership and lifetime explicit. Long-running services, native handles, timers, sockets, and subscriptions must be cancellable and disposed deterministically.
- Use dependency injection for services and side-effecting boundaries. Avoid service locators and mutable global state.
- Preserve backward compatibility for persisted profiles and settings. New fields need safe defaults and migration or tolerant deserialization tests.

## C# and .NET Style

- Follow the repository `.editorconfig`, current target framework, and official C# coding conventions.
- Write concise, idiomatic C# with nullable reference types enabled.
- Use PascalCase for types, methods, and public members; camelCase for parameters, locals, and private fields; prefix interfaces with `I`.
- Use `var` when the type is obvious, collection expressions and pattern matching when they improve clarity, and records for value-oriented data.
- Prefer async/await for I/O. Accept and propagate `CancellationToken` through cancellable operations.
- Exceptions represent exceptional failures, not normal branching. Catch only where adding context, translating a boundary failure, or keeping a supervised background loop alive.
- Use structured logging with stable message templates. Never log secrets, pairing codes, access tokens, or high-frequency controller state at normal levels.
- Add XML documentation to public APIs and to non-obvious internal contracts, especially threading, units, ranges, ownership, and platform behavior.

## Avalonia and MVVM

- Follow Avalonia best practices and MVVM separation: Views declare presentation, ViewModels expose state and commands, and services own hardware, networking, persistence, and OS integration.
- Do not put business logic, device access, or blocking work in `.axaml.cs` code-behind. Code-behind is limited to genuinely view-specific behavior that cannot be expressed cleanly with binding or behaviors.
- Keep UI-bound collections and property notifications on the Avalonia UI thread. Keep controller polling, networking, and native calls off it.
- Avoid blocking waits (`.Wait()`, `.Result`, synchronous I/O) on the UI thread.
- Use compiled bindings where supported, reusable styles/resources, localization resources for user-visible text, and accessible names for interactive controls.
- Design layouts for resizing, scaling, keyboard navigation, high-DPI displays, and light/dark themes. Do not hardcode Windows-only assumptions into shared UI.

## Controller Pipeline and Performance

- Treat polling, mapping, feedback, rendering snapshots, and network input as hot paths. Measure before major optimization, then optimize allocations, contention, redundant device scans, and unnecessary UI invalidations.
- Avoid per-tick LINQ, reflection, logging, JSON serialization, and temporary collections in high-frequency loops unless benchmarks show the cost is negligible.
- Keep snapshots coherent and thread-safe. Prefer immutable messages, bounded channels/queues, latest-value exchange, and cancellation-aware loops over shared mutable state.
- Validate all device and network values: reject non-finite numbers, clamp normalized axes and effect ranges, bound message sizes and queues, and clear held state on disconnect.
- Coalesce output effects where appropriate and avoid sending unchanged rumble, adaptive-trigger, or LED reports to physical hardware.
- Inspect HIDMaestro for its cheapest supported connection and update pattern. Cache stable controller/session objects, avoid repeated enumeration, and never depend on undocumented internals when a public contract exists.

## Physical and Virtual Device Safety

- A GameFlow-created output controller must never be eligible as a physical input source.
- Exclude virtual outputs at discovery, assignment, hot-plug, persisted-assignment restore, and pipeline construction boundaries. Do not rely on a UI-only filter.
- Prefer stable provider/device identity and explicit provenance over display-name heuristics. If a heuristic is unavoidable, document it and cover false-positive and false-negative cases with tests.
- Defend against indirect cycles involving multiple slots as well as direct self-selection. On ambiguity, refuse the assignment and present an actionable reason.
- Disconnecting, disabling, or failing an output must release buttons, axes, effects, sockets, native handles, and background work safely.

## Rumble, Adaptive Triggers, and LEDs

- Expose capabilities per assigned physical controller. Unsupported controls are disabled or hidden with a clear explanation; never pretend an effect was applied.
- Use user-friendly names, units, ranges, safe defaults, presets, reset actions, and immediate preview where hardware permits.
- Keep persisted configuration separate from runtime effect transport. UI edits update configuration; a dedicated service validates, coalesces, schedules, and delivers effects.
- Apply consistent gain and precedence rules for game feedback, previews, macros, battery modes, and user overrides. Document those rules in `GameFlow/README.md` and `GameFlow/WIKI.md`.
- Stop previews and restore the configured effect when an editor closes, a device changes, or cancellation occurs.

## Web Controller and OBS Overlay

- The browser controller is a real physical input provider from GameFlow's perspective: give each connection a stable session identity, enforce the 16-controller limit, clear input on disconnect, and return feedback to the originating phone.
- Keep the phone UI touch-safe, responsive, install-free, and usable in portrait and landscape. Handle reconnects, visibility changes, motion permission, Vibration API absence, and browsers that suppress default touch behavior.
- The OBS overlay must use a stable, copyable URL and be directly addable as an OBS Browser Source. It must not require QR rescanning or an always-on-top native window.
- Overlay routes are read-only by default, use transparent backgrounds, reconnect automatically, and avoid exposing unrelated local files or control endpoints.
- Bind network services conservatively, validate origins/paths/messages, cap clients and payload sizes, and clearly report firewall or LAN reachability problems.

## Testing and Verification

- Use xUnit for unit and integration tests, matching the existing projects.
- Add regression tests with every bug fix. Cover pure mapping logic exhaustively and isolate hardware/network dependencies behind fakes.
- For device recursion, test self-output exclusion, cross-slot cycles, hot-plug, reconnect, and persisted stale identifiers.
- For web protocols, test malformed input, finite/range validation, disconnect cleanup, client limits, reconnect, and bounded feedback queues.
- For Avalonia ViewModels, test state and commands without constructing Views where practical.
- Before handing off code, run the smallest relevant test subset, then `dotnet test GameFlow.sln` and `dotnet build GameFlow.sln` when feasible. Report any test that could not run because hardware or platform support was unavailable.

## Documentation

- Keep `GameFlow/README.md` task-oriented: purpose, supported platforms/devices, prerequisites, install, quick start, common workflows, and known limitations.
- Keep `GameFlow/WIKI.md` as the detailed reference: architecture, configuration, mappings, feedback precedence, phone protocol, OBS URLs, troubleshooting, and contributor notes.
- Update both documents in the same change whenever behavior, setup, UI labels, URLs, compatibility, or limitations change.
- Document platform-specific and hardware-dependent behavior explicitly. Distinguish implemented, preview, experimental, and planned capabilities.
- Keep code examples and commands executable, paths accurate, links valid, and terminology consistent with the UI.
- Record important architectural trade-offs close to the relevant code or in a focused document under `docs/`; do not leave essential decisions only in chat or commit messages.

## Working Method and Definition of Done

1. Read the relevant scope, implementation, tests, README, and WIKI sections before changing behavior.
2. Reproduce or characterize the current behavior and identify the owning layer.
3. Make the smallest cohesive change that improves the product without mixing unrelated refactors.
4. Add or update automated tests and validate failure/cancellation paths.
5. Run formatting, tests, and build checks appropriate to the change.
6. Update README/WIKI and any affected XML documentation.

A feature is done only when its happy path, disconnect/error path, persistence, user-facing status, tests, and documentation agree. Never claim hardware behavior was verified unless it was exercised on that hardware; state clearly when verification is automated or code-level only.


# CODING.md

Mandatory rules for source code in GameFlow.
The rules come from the Prooxie github wiki.
The C# rules follow [csharpcodingguidelines.com](https://csharpcodingguidelines.com).
If a rule does not answer your question, ask.

The build enforces the mechanical rules with `.editorconfig` and analyzers.
These rules include names, `var`, braces, spacing, C# aliases, `this`, `is` patterns, and the `Async` suffix.
Fix each build warning in the code that you change. This file lists only the rules that the build cannot check.

## Basic principles

The best code is the code you do not write. Justify each line before you write it.

- Choose the simplest solution that works (KISS). Write code that a person without deep problem knowledge can read.
- Extract a routine when the same logic occurs a second time (DRY).
- Use object-oriented design. The class is the basic unit of the system. Do not mix in functional style.
- Write the tests before the implementation (TDD).
- Keep all code in Git. Keep each change isolated and visible in the history.
- Use what the platform, framework, or codebase already gives you. Search before you write a new helper.
- Delete dead code. Do not comment it out.
- Keep the diff small. Change only the lines that the task needs.

## Naming

- Use English names. Use full words, not abbreviations (except common ones: `UI`, `Id`, `Xml`, `lbl`).
- Use clear, descriptive names with one interpretation: `SiteSecurityConfig`, not `SiteSecurity`.
- Use plural names for collections: `GetElements`, not `GetElement`.
- Use no type or scope prefixes: `UserName`, not `fUserName`.
- Prefix exceptions: interfaces (`IDevice`), type parameters (`TItem`), events (`OnClick`), UI element names (`btnOk`).
- Name the meaning, not the content: `ExecutionState`, not `EnumExecutionState`. `SystemUser`, not `ClassSystemUser`.
- Do not use names that look like other names: `b00l`, `lO1`.
- Do not repeat the class name in a member: `Employee.Get`, not `Employee.GetEmployee`.
- Use the names that the codebase already uses for the same operation: `Add`, `Count`, `Remove`.
- Name booleans positively with a verb: `IsConnected`, `CanStart`, `SupportsUnlock`. Never `IsNotConnected`.
- Put a specific return type in the method name: `GetCacheLevel()`, `GetConnection()`.
- Use `_` for unused lambda parameters: `(_, _) => HandleClick()`.
- Name each assembly after the namespace that it contains: `DAT4000.Device.S18xx`.
- Name each file after the one type that it contains: `CommunicationInterface.cs`.

## Comments

- Add an XML documentation comment (`///`) to each type, method, and property. Write it in English.
- Add a usage example to the documentation comment when the use is not clear.
- Doxygen generates the internal documentation from these comments.
- Document the structure of each XML or other external data format that the code reads or writes.
- Write a comment to tell why the code does something, when the code does not show it.
- Do not write a comment that repeats the code: `steps.Add(newStep); // add the step to the steps`.

## Readability

- Keep nesting to 5 levels or fewer. Use guard clauses to reduce nesting.
- Make each member `private` by default. Make internal types `internal sealed`. Open access only when a caller needs it.
- Prefer a method to direct field access: `order.SetCustomer("Novak")`, not `order.customer.name = "Novak"`.
- Replace magic numbers and strings with named, documented constants or enums.
- Declare a local variable where you first use it.
- Initialize a full object or collection in one expression, not property by property.
- Write `if (isConnected)`, not `if (isConnected == true)`.
- Return a boolean expression directly. Do not wrap it in `if`/`else`.
- Keep the method parameter count low. Use a parameter object for complex input.
- Put intermediate results in named local variables. This makes debugging easier. Fluent APIs are an exception.

Bad (hard to debug):

```csharp
string result = ConvertToXml(ApplyTransform(ExecuteQuery(GetConfig(dbSource))));
```

## Formatting

- Keep lines to about 130 characters.
- Write one statement per line.
- Do not indent initializer blocks. Indent only the continuation lines of a long initializer value.
- Use parentheses to show operator priority in logical expressions.
- Write a LINQ expression on one line if possible. For a long expression, align each operation at the same indent.
- Separate members with one empty line.
- Put one type in one file.

Order members in this sequence:

1. Private constants and fields.
2. Public constants and properties (read-only first).
3. Factory methods.
4. Constructors and finalizers.
5. Events.
6. Other members, in order of logical use: `Init`, `Open`, `Send`, `Receive`, `Close`.

## Class and interface design

- **Single responsibility.** One class has one purpose. One method does one thing.
- **Open/closed.** Add behaviour with new types. Keep stable code unchanged.
- **Liskov substitution.** A subclass must work everywhere its base class works. Do not hide or change base behaviour.
- **Interface segregation.** Use many small interfaces, not one large interface.
- **Dependency inversion.** Depend on interfaces. Inject dependencies through the constructor.
- Use interfaces as the basic design element. A class has one base class but can implement many interfaces.
- Each class has a defined state and predictable behaviour. The same input always gives the same result.
- Access state through properties or methods that validate it.
- Validate input in setters and constructors. Throw an exception for invalid input.
- Classes must not depend on each other in a cycle. Break a cycle with an interface or a design pattern.

## Property and method design

- A property name tells its meaning: `isActivated`, not `activationStatus`.
- Make properties settable in any order. If the order is important, use a method and hide the property.
- Do not add duplicate or contradictory properties: `IsActive` and `IsInactive`.
- Each method and property does one thing only (principle of least surprise).
- Do not expose internal state through static members. Use static members only for stateless logic.
- Prefer `IEnumerable<T>` or `ICollection<T>` to a specific collection type.
- Never return `null` for `string`, collections, or `Task`. Return an empty value.

## Errors

- Report each failure with an exception. Do not return error codes or magic values.
- Give each exception a message that tells the cause.
- Catch only the exceptions that you expect and can handle. Never swallow an exception silently.
- In async code, catch and log exceptions. A background worker must not stop silently.
- Check for `null` where `null` is possible: optional parameters, event sources, external input.

## C# specifics

- Use language syntax: `(int, string)`, `DateTime?`, not `Tuple<int, string>`, `Nullable<DateTime>`.
- Put configuration values in configuration files, not in code.
- Use lambdas, not anonymous methods: `items.FirstOrDefault(item => item.Name == name)`.
- Use `async`/`await` for asynchronous work.

## Frontend

- The frontend calls the backend API only. It uses its own DTOs. It does not reference `Domain` or `Common` types.
- Use the colours, fonts, and styles in `Styles/Resources`. Do not add new ones.

## ReactiveUI

The frontend uses the ReactiveUI 26 `.Reactive` package family. It keeps System.Reactive and `Unit`.

- Import ReactiveUI types from `ReactiveUI.Reactive`.
- The package adds global usings for `ReactiveUI.Binding.Reactive`. Read `obj/**/*.GlobalUsings.g.cs` before you change usings.
- Call `Observable.Switch(source)`. The fluent `.Switch()` is ambiguous with `ObservedProperty.Switch` (CS0121).
- Write `x is null ? fallback : x.WhenAnyValue(...)`. The binding generator rejects `x?.WhenAnyValue(...)` (RXUIBIND021).

## References

- [C# coding guidelines](https://csharpcodingguidelines.com)
- [Guidelines cheat sheet (PDF)](https://github.com/dennisdoomen/CSharpGuidelines/releases/latest)
- [C# guidelines analyzer](https://github.com/bkoelman/CSharpGuidelinesAnalyzer)
- [Microsoft C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
