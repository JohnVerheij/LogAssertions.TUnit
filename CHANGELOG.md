# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> *History note:* Sections at and below `[0.5.0]` were reformatted on 2026-05-17 for one-time
> [CONVENTIONS v0.5 §CHANGELOG](CONVENTIONS.md) conformance: forbidden sub-headers folded into
> the six Keep a Changelog headers, non-user-facing content removed (build hygiene, internal
> refactors, test counts, governance churn), bullets normalised to past-tense active voice
> with code-formatted API leads. The nuget.org Release Notes tab and GitHub Releases for each
> shipped version remain unchanged. Sections from `[0.6.0]` onward are frozen per Rule 7.

## [Unreleased]

## [0.6.2] - 2026-06-05: documentation refresh

Documentation-only release. No API or behaviour change.

### Changed

- Refreshed the README (plain-ASCII punctuation) and rewrote the shared `CONVENTIONS.md`: removed the version-history preamble so it reads as a conventions document, not a changelog.

## [0.6.1] - 2026-06-04

### Changed

- **README roadmap label corrected**: the "Possible v0.6.0+ (longer horizon, no commitment)" backlog heading was renamed to "Possible v0.7.0+" now that v0.6.0 has shipped.
- **README "Shipped in v0.6.0" section added**: documents the v0.6.0 surfaces (`WithoutException()`, the typed `WithProperty<T>` overloads, the typed `WithScopeProperty<T>` overloads) and the removal of the obsolete single-arg `WithExceptionMessage(string)`, matching the existing "Shipped in v0.5.0 / v0.4.0 / v0.3.0" entries.
- **README family roster completed**: the package-family enumerations in the root and adapter READMEs were brought to the full seven-package roster.

## [0.6.0] - 2026-06-02

### BREAKING

- The single-arg `WithExceptionMessage(string substring)` overload (on `LogFilter` and on the `HasLoggedAssertion` / `HasNotLoggedAssertion` / `HasLoggedSequenceAssertion` chain), `[Obsolete]` since v0.4.0, has been removed. Append `, StringComparison.Ordinal` to existing call sites to keep the previous behavior, or pass a different comparison for case-insensitive / culture-aware matching. See `### Removed` below.

### Added

- **`LogFilter.WithoutException()`** and the matching **`WithoutException()`** chain method on `HasLoggedAssertion` / `HasNotLoggedAssertion` / `HasLoggedSequenceAssertion`: matched records whose `Exception` is `null`. The complement of `WithException()`. Lets a test assert the deliberate absence of an exception (for example a record logged at warning or error level with no exception object attached) without the `.Where(r => r.Exception is null)` escape hatch.
- **`LogFilter.WithScopeProperty<T>(string key, T value)`** and **`LogFilter.WithScopeProperty<T>(string key, Func<T, bool> predicate)`** plus the matching chain methods: typed scope-property filters. Scope values keep their runtime type, so the value form compares typed-to-typed via `EqualityComparer<T>.Default` and the predicate form receives the typed value; a scope value whose runtime type is not `T` never matches. Removes the object-boxing boilerplate of the existing object-typed overloads when the scope value is a known type such as `Guid` or `int`.
- **`LogFilter.WithProperty<T>(string key, T value)`** and **`LogFilter.WithProperty<T>(string key, Func<T, bool> predicate)`** (`where T : IParsable<T>`) plus the matching chain methods: typed structured-state filters. `FakeLogRecord` stores structured-state values as their formatted strings, so the stored string is parsed back to `T` via `IParsable<T>.TryParse` using `CultureInfo.InvariantCulture` before comparing (value form) or applying the predicate; an absent or non-parsable value never matches. Removes the manual `int.TryParse(...)` boilerplate at the call site. The `IParsable<T>` constraint keeps the round-trip reflection-free and AOT-safe.

### Changed

- **`TUnit`** / **`TUnit.Assertions`** / **`TUnit.Core`** dependency bumped `1.44.0` → `1.48.6`. Taken for family lockstep; the sibling adapters are on the same pin.

### Removed

- **`LogFilter.WithExceptionMessage(string substring)`** and the matching single-arg `WithExceptionMessage(string)` chain method on `HasLoggedAssertion` / `HasNotLoggedAssertion` / `HasLoggedSequenceAssertion`: the implicit-`Ordinal` overloads, `[Obsolete]` since v0.4.0, are gone. The explicit-comparison overload `WithExceptionMessage(string substring, StringComparison comparison)` is unchanged. Migrate by appending `, StringComparison.Ordinal` to existing call sites. Removing a public member is a source-and-binary breaking change; see `### BREAKING` above.

## [0.5.0] - 2026-05-14

### Added

- **`LogAssertions.Render.LogSnapshotRenderer.Render(FakeLogCollector, LogSnapshotOptions?)`**: rendered every captured record as deterministic multi-line text suitable for snapshot comparison. Each record renders as a `[NN] level category "message"` header followed by optional indented `state:`, `scope:`, and `exception:` detail lines; records are separated by a single blank line; an empty collector renders as `string.Empty`. Lines are terminated with the literal LF byte (never `Environment.NewLine`) so a baseline committed on one OS stays valid for test runs on every other. Output is a stable, pin-able contract, unlike `LogAssertionRendering` whose output is explicitly *not* stable. The renderer emits captured text verbatim; volatile values are the snapshot layer's concern, composed via `SnapshotAssertions.Scrubbers` at the `MatchesSnapshot()` call site.
- **`LogAssertions.Render.LogSnapshotOptions`**: controlled rendering via `LevelStyle` (`Abbreviation` / `Full`), `CategoryStyle` (`LeafOnly` / `Full`), `ScopeStyle` (`Include` / `Exclude`), `ExceptionStyle` (`TypeAndMessage` / `StackTracePlaceholder` / `Full`). `LogSnapshotOptions.Default` exposed the all-default instance.
- **`LogAssertions.Render.LogLevelStyle`, `CategoryStyle`, `ScopeStyle`, `ExceptionStyle`**: enums backing the options above.

### Changed

- **Dependency refresh** to bring pins back into lockstep with the rest of the assertion family (`TimeAssertions` v0.4.0 moved the baseline forward on 2026-05-13):
  - `DotNetProjectFile.Analyzers` 1.13.1 → 1.14.0
  - `Meziantou.Analyzer` 3.0.78 → 3.0.84
  - `Microsoft.Extensions.Diagnostics.Testing` 10.5.0 → 10.6.0
  - `Microsoft.SourceLink.GitHub` 10.0.203 → 10.0.300
- **`README.md`**: added a "Pin the full log sequence as a snapshot" cookbook section showing the two-line `Assert.That(LogSnapshotRenderer.Render(collector)).MatchesSnapshot()` composition and the `LogSnapshotOptions` surface.

## [0.4.1] - 2026-05-12

### Changed

- **Dependency refresh** to the family-lockstep versions:
  - `TUnit` / `TUnit.Assertions` / `TUnit.Core` 1.43.11 → 1.44.0
  - `Microsoft.CodeAnalysis.BannedApiAnalyzers` 3.3.4 → 4.14.0
  - `Meziantou.Analyzer` 3.0.72 → 3.0.78
  - `SnapshotAssertions.TUnit` 0.2.0 → 0.3.0
- **Production-code source style ratcheted** via `MeziantouAnalysisMode=all-warnings` for `src/` projects (path-conditional via the normalised `Replace('\\','/').Contains('/tests/')` predicate). Test projects retain Meziantou defaults. Findings surfaced and fixed at source: CRTP self-cast pattern centralised via an `Unsafe.As<TSelf>(this)` helper property; discrete-value equality comparisons modernised to pattern-matching form (`is 0`, `is SequenceStepKind.Simple`); trailing-`null` constructor arguments given explicit `AnyOrderSubSteps:` named-argument form; culture-sensitive string concatenations switched to `string.Create(CultureInfo.InvariantCulture, ...)`.
- **`BannedSymbols.txt`**: collapsed bare `#` comment lines into adjacent text-bearing lines so the file parses cleanly under the stricter `BannedApiAnalyzers` 4.x grammar.

## [0.4.0] - 2026-05-07

### Added

- **`LogFilter.WithInnerException<TInner>()` and `LogFilter.WithInnerExceptionMessage(string substring, StringComparison comparison)`**: matched records whose `Exception.InnerException` is assignable to `TInner` (or whose inner-exception message contains a substring under the supplied comparison). Walks one level only; designed for the gRPC / RPC pattern where a transport exception wraps the underlying domain exception once.
- **`LogFilter.WithExceptionMessage(string substring, StringComparison comparison)`** overload: explicit-comparison variant of the legacy `WithExceptionMessage(string)`. The single-arg overload is now `[Obsolete]` and removed in v0.6.0; see `### Deprecated` below.
- **`LogFilter.WithScopeProperties(IDictionary<string, object?>)`**: subset match across all active scopes for the record. Each key/value pair must match in some scope; different pairs may match in different scopes. The dictionary is snapshotted on construction; mutating the input afterwards does not affect the filter.
- **`DumpVerbosity` enum + `DumpTo(TextWriter, DumpVerbosity)` overload**: three levels: `Compact` (headlines only), `Default` (existing one-liner detail), `Verbose` (`Default` plus full exception `ToString()` including stack trace). The no-arg overload is unchanged (`Default`).
- **`HasLoggedAssertion.WithInnerException<TInner>()` and `WithInnerExceptionMessage(string substring, StringComparison comparison)`**: chain methods on `HasLoggedAssertion` / `HasNotLoggedAssertion` / `HasLoggedSequenceAssertion`. Compose with the existing `WithException<T>()` filter via the chain (no `LogFilter.All(...)` boilerplate needed).
- **`HasLoggedAssertion.WithExceptionMessage(string substring, StringComparison comparison)`**: chain-method overload on the same assertion classes; paired with the same overload on `LogFilter`.
- **`HasLoggedAssertion.WithScopeProperties(IDictionary<string, object?>)`**: chain method on the same assertion classes.
- **`FakeLogCollectorTUnitInspectionExtensions.DumpToTestOutput(DumpVerbosity)`** overload: the no-arg overload now delegates to this with `DumpVerbosity.Default`.
- **`HasLoggedSequence.ThenAnyOrder(params Action<HasLoggedSequenceAssertion>[])`**: concurrent step group. All sub-steps must match somewhere in the remaining records, but the order among them is unconstrained. Records that match no sub-step are skipped. Sub-steps are matched via backtracking, so an order-independent valid assignment is found if one exists: broad filters never starve more specific filters. Sub-step configurators must add filters only; calls to `Then()` or nested `ThenAnyOrder()` from within a configurator throw `InvalidOperationException`. The `Then()` chain still works for strictly-ordered next steps before / after a `ThenAnyOrder` group.
- **`Microsoft.CodeAnalysis.BannedApiAnalyzers`** wired into both `src/` projects with a shared `BannedSymbols.txt` at the repo root, codifying the no-reflection convention from `CONVENTIONS.md` as a build-time error (RS0030 + `TreatWarningsAsErrors=true`). Test projects do not reference the banned-symbols file so reflection-using helpers (e.g. `PublicApiGenerator`) stay usable in tests.

### Changed

- **Dependency refresh** to latest stable for every direct and analyzer dependency:
  - `TUnit` / `TUnit.Assertions` / `TUnit.Core` 1.43.2 → 1.43.11
  - `Microsoft.Extensions.Diagnostics.Testing` 10.0.0 → 10.5.0
  - `PublicApiGenerator` 11.4.6 → 11.5.4
  - `Microsoft.Sbom.Targets` 3.0.1 → 4.1.5
  - `Microsoft.SourceLink.GitHub` 8.0.0 → 10.0.203
  - `DotNetProjectFile.Analyzers` 1.12.2 → 1.13.1
  - `Meziantou.Analyzer` 2.0.219 → 3.0.72
  - `Microsoft.VisualStudio.Threading.Analyzers` 17.13.61 → 17.14.15
  - `Roslynator.Analyzers` 4.13.1 → 4.15.0
  - `SonarAnalyzer.CSharp` 10.24.0.138807 → 10.25.0.139117
- **`CONVENTIONS.md`** upgraded to v0.2: codified family-wide rules (trailing `CancellationToken ct = default` on every new async API, `Task.Delay(TimeSpan, TimeProvider, ct)` for polling loops, the 100/200/400/800/1000ms exponential schedule for time-based polls, the `# <Package> snapshot v<N>` header convention for `ToSnapshotString()`, TFM policy, and the "Verify is not promoted" stance).
- **`README.md`**: added a "Pair with" section cross-referencing `TimeAssertions.TUnit` and `SnapshotAssertions.TUnit`; added the `> **Scope:** Test projects only. Not intended for production code.` blockquote at the top of the per-package short README.

### Deprecated

- **`LogFilter.WithExceptionMessage(string substring)`** and the corresponding chain method on `HasLoggedAssertion`: single-arg overloads now carry `[Obsolete(error: false)]`. They default to `StringComparison.Ordinal` and delegate to the new explicit-comparison overload. Will be removed in v0.6.0 (two-minor cycle). Migrate by appending `, StringComparison.Ordinal` to existing call sites, or pick a different comparison if you want case-insensitive / culture-aware behaviour.

## [0.3.0] - 2026-05-02

### Added

- **`HasLoggedAssertion.GetMatch()` / `GetMatches()`**: value-returning terminators on the `HasLogged()` chain. Eliminated the duplicate `collector.Filter(...)` call after asserting: hand the matched record(s) directly to a follow-up TUnit assertion. `GetMatch()` requires the chain's count expectation to constrain the match count to exactly one (typically via `Once()` or `Exactly(1)`, but any terminator with both bounds equal to one is accepted, including `Between(1, 1)`); throws `InvalidOperationException` upfront for any other expectation. `GetMatches()` accepts any terminator and returns the matched records as a defensive `IReadOnlyList<FakeLogRecord>` snapshot captured at evaluation time.
- **`FakeLogCollectorTUnitInspectionExtensions.DumpToTestOutput()`**: TUnit-aware variant of the framework-agnostic `DumpTo(TextWriter)` that routes captured records to `TestContext.Current.Output.StandardOutput`. Eliminated the `using var sw = new StringWriter(); collector.DumpTo(sw); Console.WriteLine(sw)` boilerplate during test development. Throws `InvalidOperationException` with a clear diagnostic if called outside a TUnit test context.

### Changed

- **`TUnit`** dependency bumped `1.41.0` → `1.43.2` (latest stable). Adds `TUnit.Core` as a direct `PackageReference` to `LogAssertions.TUnit` (needed for `TestContext.Current` in `DumpToTestOutput`). The published `LogAssertions.TUnit.nupkg` therefore declares `TUnit.Core 1.43.2` and `TUnit.Assertions 1.43.2` as direct dependencies; consumers who pin a lower TUnit version will see the standard NuGet upgrade prompt on install.
- **`README.md`**: added a "TUnit-native conveniences" section documenting `Because("reason")` (the correct reason-annotation API; `.WithMessage(...)` is a separate predicate-on-failure-message feature for negative-testing patterns), `Assert.Multiple()` aggregating failures from our chains, `[NotInParallel]` guidance (`FakeLogCollector` is instance-scoped: tests using `LogAssertions.TUnit` do NOT need `[NotInParallel]` for the collector itself), `Should()` interop (deferred pending the upstream `TUnit.Assertions.Should` package reaching stable), and a Troubleshooting section covering shorthand resolution failures on pre-0.2.2 versions, `LogCollectorBuilder` not found (missing `using LogAssertions;`), and `IDE0005` noise on per-file imports under strict-analysis projects.
- **`README.md`**: corrected v0.2.4 typos in the "Migrating from manual assertions" section: `InScope("RequestId", id)` → `WithScopeProperty("RequestId", id)`, `WithExceptionOfType<TimeoutException>()` → `WithException<TimeoutException>()`.

## [0.2.4] - 2026-05-02

### Changed

- **`README.md`**: `GlobalUsings.cs` recommendation extended with `global using System;` so the first `.Containing(text, StringComparison.X)` call in a project that does not enable `<ImplicitUsings>` compiles without an extra per-file import.
- **`README.md`**: Quick Start gained a "Lifetime / disposal" paragraph clarifying what the `IDisposable` returned from `LogCollectorBuilder.Create()` owns. Disposing the `factory` stops new records from being captured but the records already gathered into the `collector` snapshot remain valid: the collector can be queried after the `using` block exits. Both block-form (`using (factory) { ... }`) and declaration-form (`using ILoggerFactory factory = ...`) are explicitly endorsed.
- **`README.md`**: new "Migrating from manual assertions" section with a side-by-side before / after pairing a manual `collector.GetSnapshot()` + LINQ filter + multiple separate assertions against one fluent chain, calling out the two non-obvious wins on top of readability (failure message renders the full captured-records snapshot; the same chain extends to scopes, structured properties, exception types, and combinator nodes without restructuring the test).

## [0.2.3] - 2026-05-01

### Changed

- **`README.md`**: new "Namespaces" section explaining the asymmetric `using` situation: the assertion entry points (`HasLogged`, `HasLoggedOnce`, `AssertAllAsync`, ...) live in `TUnit.Assertions.Extensions` and auto-import via TUnit's mechanism, while `LogCollectorBuilder`, `LogFilter`, `ILogRecordFilter`, and the `FakeLogCollector` inspection extensions live in `LogAssertions` and require an explicit `using LogAssertions;`. Includes a recommended `GlobalUsings.cs` snippet that consolidates both for any consuming test project.
- **`README.md`**: `AssertAllAsync` cookbook example updated to use the v0.2.1 sync overload (`c => c.HasLogged()...`) as the primary form. The verbose `async c => await c.HasLogged()...` form is mentioned as a fallback for cases that mix non-assertion async work between checks.
- **`README.md`**: `.And` / `.Or` section rewritten. Confirms `.And` is useful for chaining a positive + negative invariant in one expression, and explicitly says `.Or` is rarely useful for log assertions (consider `MatchingAny(...)` over filters instead). Recommends `AssertAllAsync` for three-or-more conditions.
- **`README.md`**: `Never()` vs `HasNotLogged()` clarification added to the terminators section: prefer `HasNotLogged()` when "this should not happen" is the test's primary intent; use `.Never()` when an existing positive filter chain ends up needing a zero-count expectation.

## [0.2.2] - 2026-05-01

### Changed

- **BREAKING:** **`HasLoggedShorthandExtensions`** (the `HasLoggedOnce`, `HasLoggedExactly`, `HasLoggedAtLeast`, `HasLoggedAtMost`, `HasLoggedBetween`, `HasLoggedNothing`, `HasLoggedWarningOrAbove`, `HasLoggedErrorOrAbove` entry points) moved from namespace `LogAssertions.TUnit` to namespace `TUnit.Assertions.Extensions`. The shipped assembly (`LogAssertions.TUnit.dll`), package ID, and method signatures are unchanged. Only the public namespace of the static class moved. Consumers that used the shorthands as the extension methods they were meant to be require no code change: the methods now resolve via TUnit's auto-imported `TUnit.Assertions.Extensions` namespace, so any file that already calls `HasLogged()` will also pick up `HasLoggedOnce()` etc. without changes. Migration for fully-qualified call sites: replace `LogAssertions.TUnit.HasLoggedShorthandExtensions.HasLoggedOnce(source)` with `TUnit.Assertions.Extensions.HasLoggedShorthandExtensions.HasLoggedOnce(source)`.

## [0.2.1] - 2026-05-01

### Added

- **`AssertAllExtensions.AssertAllAsync`** new overload accepting `Func<IAssertionSource<TActual>, Assertion<FakeLogCollector>>` (sync return) instead of `Func<IAssertionSource<TActual>, Task>` (awaited delegate). Drops the `async` / `await` boilerplate from every entry. The compiler picks the right overload based on the lambda's return type: `Assertion<T>` for the new sync form, `Task` for the existing async form. Failure-aggregation semantics are identical between the two overloads. The existing overload remains for cases where the lambda needs to mix in non-assertion async work.

### Changed

- **`PackageValidationBaselineVersion`** pinned to `0.2.0` on both packages (was `0.1.0` for `LogAssertions.TUnit`; not previously set on `LogAssertions` because 0.2.0 was its first release). ApiCompat baseline checks now compare against the previous shipped version of each package.

## [0.2.0] - 2026-05-01

### Added

- **`LogAssertions`** new framework-agnostic core package, first published at 0.2.0. Contains `ILogRecordFilter`, the `LogFilter` factory, the four internal filter primitives (`PredicateFilter`, `AndFilter`, `OrFilter`, `NotFilter`), `LogAssertionRendering` (failure-snapshot rendering), `LogCollectorBuilder.Create`, and the `FakeLogCollector` inspection extensions (`Filter`, `CountMatching`, `DumpTo`). `LogAssertions.TUnit` now declares a transitive dependency on `LogAssertions`. Existing v0.1.0 consumers need no migration: `LogAssertions.TUnit` is still the package to install; `LogAssertions` comes transitively.
- **`ILogRecordFilter`** interface + **`LogFilter`** static factory: composable filter primitives. Every built-in chain method (`AtLevel`, `Containing`, etc.) creates one of these internally; users can build reusable filters and inject them via the new `WithFilter(ILogRecordFilter)` chain method.
- **`WithFilter`, `MatchingAny`, `MatchingAll`, `Not`**: combinator chain methods accepting `ILogRecordFilter` (or `params ILogRecordFilter[]`). The `LogFilter` factory exposes the same as `LogFilter.All`, `LogFilter.Any`, `LogFilter.Not`.
- **`AtAnyLevel(params LogLevel[])`**: matched any level in the supplied set.
- **`Matching(Regex)`**: regex match against the formatted message.
- **`ContainingAll(StringComparison, params string[])` and `ContainingAny(StringComparison, params string[])`**.
- **`WithException()` (parameterless: any non-null exception) and `WithException(Func<Exception, bool>)`** (predicate).
- **`WithEventIdInRange(int min, int max)`**: inclusive range match.
- **`WithLoggerName(string)`**: alias for `WithCategory`.
- **`NotContaining(string, StringComparison)`, `NotAtLevel(LogLevel)`, `ExcludingCategory(string)`, `ExcludingLevel(LogLevel)`**: negation shortcuts.
- **`When(bool condition, Action<TSelf> apply)`**: conditional sub-chain configuration for parameterised tests.
- **`HasLoggedOnce()`, `HasLoggedExactly(int)`, `HasLoggedAtLeast(int)`, `HasLoggedAtMost(int)`, `HasLoggedBetween(int, int)`, `HasLoggedNothing()`, `HasLoggedWarningOrAbove()`, `HasLoggedErrorOrAbove()`**: top-level shorthand entry points wrapping the equivalent `HasLogged()...X()` chain.
- **`AssertAllExtensions.AssertAllAsync(...)`**: batch terminator that runs N independent assertions against the same collector and aggregates failures into a single `AssertionException` (analogous to TUnit's `Assert.Multiple`, scoped to log assertions).
- **`LogCollectorBuilder.Create(LogLevel)`**: one-line factory returning a wired `(ILoggerFactory, FakeLogCollector)` tuple. Replaced the 3-4 lines of boilerplate every test would otherwise need.
- **`FakeLogCollector.Filter(params ILogRecordFilter[])`, `CountMatching(params ILogRecordFilter[])`, `DumpTo(TextWriter)`**: inspection extensions.
- **`LogAssertionRendering`**: public static class exposing the failure-snapshot rendering helpers (`AppendCapturedRecords`, `LevelAbbreviation`) so users can build their own diagnostic surfaces with the same format. Format itself is *not* stable (see README "Stability promise").

### Changed

- **BREAKING:** **`LogAssertionBase<TSelf>`** annotated `[EditorBrowsable(Never)]` and documented as not for external derivation. The CRTP + sealed-derived-classes pattern requires the type to remain `public` (C# does not allow public classes to inherit from internal classes), so this is the strongest signal we can send while keeping the source generator working. The README "Stability promise" section spells out which surfaces are stable vs implementation detail.

### Removed

- **BREAKING:** **`LogAssertionBase<TSelf>.AddPredicate(Func<FakeLogRecord, bool>, string)`**: removed. Replaced by `protected virtual void AddFilter(ILogRecordFilter)` as part of the internal refactor. Only matters for consumers that were deriving from `LogAssertionBase` (which was already documented as unsupported).

## [0.1.0] - 2026-05-01

### Added

- **`HasLogged()`**: positive assertion entry point on `FakeLogCollector`, defaults to "at least 1 matching record".
- **`HasNotLogged()`**: negative assertion entry point on `FakeLogCollector`, fixed at "zero matching records".
- **`HasLoggedSequence()`**: order-preserving sequence-matching entry point with `Then()` step terminator.
- **`AtLevel(LogLevel)`, `AtLevelOrAbove(LogLevel)`, `AtLevelOrBelow(LogLevel)`**: level filters chained on the assertion entry points.
- **`Containing(string, StringComparison)`** (comparison explicit by design), **`WithMessage(Func<string, bool>)`**, **`WithMessageTemplate(string)`** (matches the pre-substitution template via `Microsoft.Extensions.Logging`'s `{OriginalFormat}` entry): message filters.
- **`WithException<TException>()`, `WithExceptionMessage(string)`**: exception filters. (`WithExceptionMessage(string, StringComparison)` overload added in v0.4.0; legacy single-arg overload `[Obsolete]` until v0.6.0.)
- **`WithProperty(string key, string? value)`** (ordinal), **`WithProperty(string key, Func<string?, bool> predicate)`** (predicate over formatted value): structured-state filters.
- **`WithScope<TScope>()`** (by scope type), **`WithScopeProperty(string key, object? value)`** (`object.Equals` on scope-property value), **`WithScopeProperty(string key, Func<object?, bool> predicate)`**: scope filters. Recognises dictionary scopes and `LoggerMessage.DefineScope` scopes; anonymous-object scopes intentionally not supported (would require reflection, breaking AOT).
- **`WithCategory(string)`, `WithEventId(int)`, `WithEventName(string)`**: identity filters.
- **`Where(Func<FakeLogRecord, bool>)`**: escape-hatch filter.
- **`Once()`, `Exactly(int)`, `AtLeast(int)`, `AtMost(int)`, `Between(int, int)`, `Never()`**: six terminators on `HasLogged()`.
- **Failure-message snapshot rendering** with 4-character level abbreviations matching the `Microsoft.Extensions.Logging` console formatter (`trce`, `dbug`, `info`, `warn`, `fail`, `crit`, `none`), indented `props:` line listing each record's structured properties (excluding the `{OriginalFormat}` entry, already implied by the message line), indented `scope:` line rendering each active scope's content as `key=value` pairs (or `ToString()` for opaque scopes), and indented `exception:` line with type name and message.
- **`.And` / `.Or`** chaining via TUnit's `Assertion<T>` base class.

[unreleased]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.6.2...HEAD
[0.6.2]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.2.4...v0.3.0
[0.2.4]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.1.0
