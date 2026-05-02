# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.4] — Second docs polish from adoption feedback (no API change)

All changes in this release are documentation. Both packages still publish in lockstep so consumers who pin transitively don't end up on mismatched core/adapter versions.

### Changed (README only)

- **`GlobalUsings.cs` recommendation extended** with `global using System;`. The previous recommendation covered `LogAssertions`, `Microsoft.Extensions.Logging`, and `Microsoft.Extensions.Logging.Testing`, but every `Containing(...)` filter call also needs `StringComparison`, which lives in `System`. Test projects that don't enable `<ImplicitUsings>` (some strict-analysis configurations deliberately don't) hit a compile error on their first `.Containing()` call without it. Adding the line to the recommended snippet is a one-character pre-empt.
- **Quick start gained a "Lifetime / disposal" paragraph** clarifying what the `IDisposable` returned from `LogCollectorBuilder.Create()` actually owns. Disposing the `factory` stops new records from being captured but the records already gathered into the `collector` snapshot remain valid — you can continue to query the `collector` after the `using` block exits. Both block-form (`using (factory) { ... }`) and declaration-form (`using ILoggerFactory factory = ...`) are explicitly endorsed; the README previously only showed the block form, which led to questions about whether the collector remained usable in the alternative layout.
- **New "Migrating from manual assertions" section** with a side-by-side before/after. The "before" pattern is a `collector.GetSnapshot()` + LINQ filter + multiple separate assertions; the "after" is one fluent chain. The section also calls out the two non-obvious wins on top of readability — the failure message renders the full captured-records snapshot (so you see *which* records came through and why none matched), and the same chain extends to scopes, structured properties, exception types, and combinator nodes without restructuring the test. Sits between Quick Start and Entry Points so evaluators see it before the API reference.

### Background

Second release driven by adoption feedback. No code changes; the v0.2.2 surface stands. Same rationale as v0.2.3 for shipping as a docs-only patch — keep nuget.org's package page reflecting current ergonomics for anyone evaluating the package today, while v0.3.0's substantive engineering work waits for actual demand.

The v0.2.x line shipped four releases inside a 48-hour window (v0.2.0 broken-shorthand, v0.2.1 additive ergonomic overload, v0.2.2 namespace fix, v0.2.3 + v0.2.4 docs polish). The v0.3.0 roadmap item to add an external-consumer smoke-test project to CI — a separately-namespaced test project that references `LogAssertions.TUnit` only via PackageReference — is the direct mitigation for the gap that velocity exposed.

## [0.2.3] — Docs polish from real consumer feedback (no API change)

All changes in this release are documentation. Both packages still publish in lockstep so consumers who pin transitively don't end up on mismatched core/adapter versions.

### Changed (README only)

- **New "Namespaces" section** explaining the asymmetric `using` situation: the assertion entry points (`HasLogged`, `HasLoggedOnce`, `AssertAllAsync`, ...) live in `TUnit.Assertions.Extensions` and auto-import via TUnit's mechanism, while `LogCollectorBuilder`, `LogFilter`, `ILogRecordFilter`, and the `FakeLogCollector` inspection extensions live in `LogAssertions` and require an explicit `using LogAssertions;`. Includes a recommended `GlobalUsings.cs` snippet that consolidates both for any consuming test project — eliminates IDE0005 ("unnecessary using") chatter in mixed-usage codebases.
- **`AssertAllAsync` cookbook example updated** to use the v0.2.1 sync overload (`c => c.HasLogged()...`) as the primary form. The verbose `async c => await c.HasLogged()...` form is still mentioned as a fallback for cases that mix non-assertion async work between checks.
- **`.And` / `.Or` section rewritten** to stop hand-waving. Confirms `.And` is genuinely useful for chaining a positive + negative invariant in one expression, and explicitly says `.Or` is rarely useful for log assertions (consider `MatchingAny(...)` over filters instead). Recommends `AssertAllAsync` for three-or-more conditions.
- **`Never()` vs `HasNotLogged()` clarification** added to the terminators section: prefer `HasNotLogged()` when "this should not happen" is the test's primary intent; use `.Never()` when an existing positive filter chain ends up needing a zero-count expectation. Either reads clearly; the choice is style.
- **Roadmap (Limitations and future work) extended** with three new v0.3.0 candidate items, all queued from real consumer feedback:
  - `DescribedAs(string)` on filters — human-readable label in failure diagnostics for `Where(...)` and composed filters.
  - `DumpToTestOutput()` extension — TUnit-aware variant of `DumpTo(TextWriter)` routing to `TestContext.OutputWriter`.
  - External-consumer smoke-test project in CI — a separately-namespaced test project that references `LogAssertions.TUnit` only via PackageReference, would have caught the v0.2.0/v0.2.1 shorthand-resolution bug before it shipped.

### Background

This release is the first one driven entirely by real-consumer feedback (production-adjacent adoption review). No code changes; the v0.2.2 surface stands. The rationale for shipping these as a docs-only patch rather than rolling them into v0.3.0 is to keep nuget.org's package page reflecting current ergonomics for anyone evaluating the package today, while v0.3.0's substantive engineering work waits for actual demand.

## [0.2.2] — Shorthand entry-point auto-discovery (BREAKING namespace move)

### Changed (breaking, but resolves real consumer-experience friction)

- `HasLoggedShorthandExtensions` (the `HasLoggedOnce`, `HasLoggedExactly`, `HasLoggedAtLeast`, `HasLoggedAtMost`, `HasLoggedBetween`, `HasLoggedNothing`, `HasLoggedWarningOrAbove`, `HasLoggedErrorOrAbove` entry points) **moved from namespace `LogAssertions.TUnit` to namespace `TUnit.Assertions.Extensions`**.
- The shipped assembly (`LogAssertions.TUnit.dll`), package ID, and method signatures are unchanged. Only the public **namespace** of the static class moved.

**Why:** the previous namespace required consumers to add an explicit `using LogAssertions.TUnit;` to discover the shorthand entry points, while the *core* entry points (`HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()`) — emitted by TUnit's `[AssertionExtension]` source generator — auto-resolve via TUnit's `TUnit.Assertions.Extensions` import path. The asymmetry was invisible discoverability friction (you could call `HasLogged()` in a file but not `HasLoggedOnce()`) and produced analyzer noise in strict-rule consumer codebases (IDE0005 flagging the extra `using` as unnecessary in files that didn't reach for it). Putting the shorthands in the same namespace TUnit's source generator emits to means they auto-import alongside the core entry points — one fewer `using`, one fewer thing to teach, no analyzer noise.

**Migration if you were using the shorthands directly via fully-qualified name:**

```csharp
// Before:
LogAssertions.TUnit.HasLoggedShorthandExtensions.HasLoggedOnce(source)

// After:
TUnit.Assertions.Extensions.HasLoggedShorthandExtensions.HasLoggedOnce(source)
```

If you used the shorthands as the extension methods they were meant to be, **no code change is required** — the methods now resolve via TUnit's auto-imported `TUnit.Assertions.Extensions` namespace, so any file that already calls `HasLogged()` will also pick up `HasLoggedOnce()` etc. without changes.

### Background

This was the first concrete consumer-experience finding from real-world use of v0.2.x in a production-adjacent test suite — exactly the demand-driven signal the README's "Stability intent" section describes as the trigger for breaking changes during the 0.x window.

## [0.2.1] — `AssertAllAsync` ergonomic overload (additive)

### Added

- New `AssertAllAsync` overload that accepts `Func<IAssertionSource<TActual>, Assertion<FakeLogCollector>>` (sync return) instead of `Func<IAssertionSource<TActual>, Task>` (awaited delegate). Drops the `async`/`await` boilerplate from every entry:

  ```csharp
  // Before (still supported):
  await Assert.That(c).AssertAllAsync(
      async c => await c.HasLogged().AtLevel(LogLevel.Information).AtLeast(1),
      async c => await c.HasNotLogged().AtLevel(LogLevel.Error));

  // After (preferred for the common case):
  await Assert.That(c).AssertAllAsync(
      c => c.HasLogged().AtLevel(LogLevel.Information).AtLeast(1),
      c => c.HasNotLogged().AtLevel(LogLevel.Error));
  ```

  The compiler picks the right overload based on the lambda's return type — `Assertion<T>` for the new sync form, `Task` for the existing async form. Failure-aggregation semantics are identical between the two overloads. The existing overload remains for cases where the lambda needs to mix in non-assertion async work.

### Internal

- Aggregation logic factored into a private `ThrowIfAnyFailed` helper shared between the two overloads — keeps both code paths in lockstep on aggregate-message format and single-failure-rethrow semantics.
- Both packages' `PackageValidationBaselineVersion` is now pinned to `0.2.0` (was `0.1.0` for `LogAssertions.TUnit`; not set on `LogAssertions` because 0.2.0 was its first release). ApiCompat baseline checks now compare against the previous shipped version of each package.

### Tests

- 3 new tests in `LogAssertions.TUnit.Tests` covering the new overload's happy path, failure aggregation, and null-arg guards (94 main tests total + 14 framework-agnostic + 2 snapshot = 110).
- The existing `AssertAllRejectsNullArgsAsync` test updated with explicit cast disambiguation since `null!` is now ambiguous between the two overloads.

## [0.2.0] — Composable filters, shorthands, batch assertions, package split, and Linux coverage fix

### Package split — new `LogAssertions` (core) package

This repo now ships **two** NuGet packages, both versioned in lockstep:

- **`LogAssertions`** (new, first published at 0.2.0) — framework-agnostic core. Contains `ILogRecordFilter`, the `LogFilter` factory, the four internal filter primitives (`PredicateFilter`, `AndFilter`, `OrFilter`, `NotFilter`), `LogAssertionRendering` (failure-snapshot rendering), `LogCollectorBuilder.Create`, and the `FakeLogCollector` inspection extensions (`Filter`, `CountMatching`, `DumpTo`).
- **`LogAssertions.TUnit`** — the TUnit adapter. Now declares a transitive dependency on `LogAssertions`. Continues to contain the three assertion classes (`HasLoggedAssertion`, `HasNotLoggedAssertion`, `HasLoggedSequenceAssertion`), `LogAssertionBase`, the source-generated `[AssertionExtension]` entry points, the shorthand entry points, and `AssertAllExtensions`.

Why the split: positions the package family for hypothetical future adapters (`LogAssertions.NUnit`, `LogAssertions.xUnit`, `LogAssertions.MSTest`) without requiring a breaking refactor at adoption time. **No such adapters are planned today** — they will only be built if someone asks for them.

**Migration impact for existing v0.1.0 consumers:** none required. `LogAssertions.TUnit` continues to be the package you install; `LogAssertions` comes transitively. The framework-agnostic types previously named `LogAssertions.TUnit.X` (e.g. `ILogRecordFilter`, `LogFilter`) — which only existed in unreleased post-v0.1.0 development — are now named `LogAssertions.X`. Since v0.1.0 didn't expose any of those types, no real consumer is affected.

### Added — public API (additive)

- **`ILogRecordFilter` interface + `LogFilter` static factory** — composable filter primitives. Every built-in chain method (`AtLevel`, `Containing`, etc.) creates one of these internally; users can build reusable filters and inject them via the new `WithFilter(ILogRecordFilter)` chain method.
- **Combinator chain methods:** `WithFilter`, `MatchingAny(params ILogRecordFilter[])`, `MatchingAll(params ILogRecordFilter[])`, `Not(ILogRecordFilter)`. The `LogFilter` factory exposes the same as `LogFilter.All`, `LogFilter.Any`, `LogFilter.Not`.
- **New filters on the chain:**
  - `AtAnyLevel(params LogLevel[])` — match any level in the supplied set
  - `Matching(Regex)` — regex match against the formatted message
  - `ContainingAll(StringComparison, params string[])` and `ContainingAny(StringComparison, params string[])`
  - `WithException()` (parameterless — any non-null exception) and `WithException(Func<Exception, bool>)` (predicate)
  - `WithEventIdInRange(int min, int max)` — inclusive range match
  - `WithLoggerName(string)` — alias for `WithCategory`
  - `NotContaining(string, StringComparison)`, `NotAtLevel(LogLevel)`, `ExcludingCategory(string)`, `ExcludingLevel(LogLevel)` — negation shortcuts
  - `When(bool condition, Action<TSelf> apply)` — conditional sub-chain configuration for parameterised tests
- **Top-level shorthand entry points:** `HasLoggedOnce()`, `HasLoggedExactly(int)`, `HasLoggedAtLeast(int)`, `HasLoggedAtMost(int)`, `HasLoggedBetween(int, int)`, `HasLoggedNothing()`, `HasLoggedWarningOrAbove()`, `HasLoggedErrorOrAbove()`. Each wraps the equivalent `HasLogged()...X()` chain.
- **`AssertAllAsync(...)` batch terminator** — runs N independent assertions against the same collector and aggregates failures into a single `AssertionException` (analogous to TUnit's `Assert.Multiple`, scoped to log assertions).
- **`LogCollectorBuilder.Create(LogLevel)`** — one-line factory returning a wired `(ILoggerFactory, FakeLogCollector)` tuple. Replaces the 3-4 lines of boilerplate every test would otherwise need.
- **Inspection extensions on `FakeLogCollector`:** `Filter(params ILogRecordFilter[])` returns matching records, `CountMatching(params ILogRecordFilter[])` returns the count, `DumpTo(TextWriter)` writes the captured-records snapshot in the same format the failure message uses.
- **`LogAssertionRendering` public static class** — exposes the failure-snapshot rendering helpers (`AppendCapturedRecords`, `LevelAbbreviation`) so users can build their own diagnostic surfaces with the same format. Format itself is **not stable** (see Stability promise).

### Changed — public API (breaking)

- **`LogAssertionBase<TSelf>` is annotated `[EditorBrowsable(Never)]`** and documented as not for external derivation. The CRTP + sealed-derived-classes pattern requires the type to remain `public` (C# does not allow public classes to inherit from internal classes), so this is the strongest signal we can send while keeping the source generator working. The `Stability promise` section in the README spells out which surfaces are stable vs implementation detail.
- **Removed: `protected virtual void AddPredicate(Func<FakeLogRecord, bool>, string)`** on `LogAssertionBase`. Replaced by `protected virtual void AddFilter(ILogRecordFilter)` as part of the internal refactor. Only matters if you were deriving from `LogAssertionBase` (which was already documented above as unsupported).

### Internal refactor (no public effect)

- `LogAssertionBase` now stores filters as `List<ILogRecordFilter>` instead of parallel `List<Func<...,bool>>` + `List<string>`.
- `HasLoggedSequenceAssertion` updates its parallel sequence-step storage similarly.
- Failure-snapshot rendering moved from a private helper on `LogAssertionBase` to the new public `LogAssertionRendering` so the same code powers `DumpTo`.
- `AppendStructuredState` now defensively checks `record.State` directly instead of going through the throwing `record.StructuredState` getter — fixes a crash when a record's state is custom typed state (rather than the standard `FormattedLogValues`).

### Coverage badge fix

- Snapshot tests moved into a dedicated `LogAssertions.TUnit.SnapshotTests` project, annotated `[assembly: ExcludeFromCodeCoverage]`. Verify.props' `Deterministic=false` requirement breaks Microsoft.CodeCoverage's instrumentation pipeline on Linux runners; isolating the Verify-using tests lets the main test project's coverage report populate cleanly. (See [thomhurst/TUnit#4149](https://github.com/thomhurst/TUnit/discussions/4149).)
- New `.config/codecoverage.settings.xml` (MTP flat schema) with explicit `ModulePaths/Include` so Microsoft.CodeCoverage knows what to instrument on Linux.
- New `codecov.yml` ignoring `obj/`, `bin/`, and `tests/` so source-generator output doesn't poison the report.
- `tests/**/*.cs` excluded from `MA0004` / `CA2007` / `VSTHRD200` analyzers (no SynchronizationContext under the test runner; analyzer suppressions are project-policy noise rather than rule violations).

### Removed from this release scope

- `WithStructuredState<T>(Func<T, bool>)` was prototyped but reverted: `FakeLogger` does not preserve the original typed state object (it stores the formatted string instead), so the filter would never match in tests built on `FakeLogCollector`. The `Where()` escape hatch remains the way to do typed-state matching when callers can guarantee the state is preserved.
- `LogAssertions.Core` package split (multi-framework adapter shape) deferred to a future major version — no demonstrated demand for non-TUnit adapters yet.

### Quality numbers

- 90 tests, 96.1% line coverage, 86.2% branch coverage (above the CI hard gates of 90% / 80%).
- Zero analyzer suppressions in production code (the one philosophical Proj0039 override from v0.1.0 is unchanged).
- ApiCompat strict-mode validation against the v0.1.0 baseline (`PackageValidationBaselineVersion=0.1.0`), with auto-generated `CompatibilitySuppressions.xml` documenting every additive change plus the one deliberate breaking change.

### Added — quality bar reinforcements (post-launch hardening, no public API changes)

- README **Design Notes** call out the **net10-forever, single-TFM, forward-only** policy explicitly: future versions will track the latest LTS, never multi-target downward. Anchored on the test-only nature of the library — applications on older TFMs can still consume by bumping only their test project's TFM.
- CI workflow now hard-gates coverage: **90% line / 80% branch**. Below either threshold the build fails. Both metrics are read from the cobertura `<coverage line-rate="…" branch-rate="…">` attributes.
- `codecov.yml` added at repo root: ignores `obj/`, `bin/`, and `tests/` so source-generated files (TUnit `[AssertionExtension]` outputs) don't poison the report. Sets a project-coverage status with 1% drift tolerance and a stricter 80% patch-coverage status to encourage tests on new code.
- 16 new edge-case tests added (current run: 64 tests, line 95.2%, branch 85.8%):
  - **Nested scopes:** `WithScopeProperty` walks every active scope; same key in outer + inner matches both
  - **Unicode:** `Containing` works correctly under both `Ordinal` and `OrdinalIgnoreCase` against unicode-bearing messages
  - **Concurrency:** records emitted from 4 threads × 250 iterations all captured
  - **Large snapshot:** 1,000-record collector handled without pathological slowdown
  - **Sequence corners:** empty step is silently skipped; long chains (5+ steps with intervening noise records) walk correctly
  - **Filter ordering invariance:** AND-combine semantics independent of chain order
  - **Case sensitivity:** `WithCategory` is ordinal (case-sensitive)
  - **Absent-key handling:** `WithProperty` against an absent key does not match for non-null expected values
  - **Parameterless template matching:** `WithMessageTemplate` works on `[LoggerMessage]` calls without parameters
  - **Failure-snapshot rendering corner cases:** multiple structured properties on one record render comma-separated; multiple active scopes render pipe-separated; opaque scope objects render via `ToString()`; every standard `LogLevel` gets its 4-char abbreviation in the snapshot

## [0.1.0] — Initial release

### Added — assertion API

- Three assertion entry points on `FakeLogCollector`:
  - `HasLogged()` — defaults to "at least 1 matching record"
  - `HasNotLogged()` — fixed at "zero matching records"
  - `HasLoggedSequence()` — order-preserving sequence matching with `Then()` step terminator
- Fourteen filter methods (chain any combination, all AND together within a step):
  - **Level:** `AtLevel(LogLevel)`, `AtLevelOrAbove(LogLevel)`, `AtLevelOrBelow(LogLevel)`
  - **Message:** `Containing(string, StringComparison)` (comparison explicit by design), `WithMessage(Func<string, bool>)`, `WithMessageTemplate(string)` (matches the pre-substitution template via MEL's `{OriginalFormat}` entry)
  - **Exception:** `WithException<TException>()`, `WithExceptionMessage(string)`
  - **Structured state:** `WithProperty(string key, string? value)` (ordinal), `WithProperty(string key, Func<string?, bool> predicate)` (predicate over formatted value)
  - **Scope:** `WithScope<TScope>()` (by scope type), `WithScopeProperty(string key, object? value)` (`object.Equals` on scope-property value), `WithScopeProperty(string key, Func<object?, bool> predicate)` — recognises dictionary scopes and `LoggerMessage.DefineScope` scopes; anonymous-object scopes intentionally not supported (would require reflection, breaking AOT)
  - **Identity:** `WithCategory(string)`, `WithEventId(int)`, `WithEventName(string)`
  - **Escape hatch:** `Where(Func<FakeLogRecord, bool>)`
- Six terminators on `HasLogged()`:
  - `Once()`, `Exactly(int)`, `AtLeast(int)`, `AtMost(int)`, `Between(int, int)`, `Never()`
- Failure-message snapshot rendering:
  - 4-character level abbreviations matching the MEL console formatter (`trce`, `dbug`, `info`, `warn`, `fail`, `crit`, `none`)
  - Indented `props:` line listing each record's structured properties (excluding the magic `{OriginalFormat}` entry — already implied by the message line)
  - Indented `scope:` line rendering each active scope's content as `key=value` pairs (or `ToString()` for opaque scopes)
  - Indented `exception:` line with type name and message
- `.And` / `.Or` chaining via TUnit's `Assertion<T>` base class

### Quality bar (zero suppressions except one explicit philosophical override)

- Targets `net10.0` only (current .NET LTS until Nov 2028)
- C# 14 (`<LangVersion>14.0</LangVersion>`, explicit per dpfa Proj0048)
- `IsAotCompatible=true`, `IsTrimmable=true`, `EnableTrimAnalyzer=true` — no reflection in the assertion path
- `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`
- Analyzer pack (all enabled, all errors-on-violation):
  - Microsoft .NET analyzers (built-in)
  - Meziantou.Analyzer
  - SonarAnalyzer.CSharp
  - Roslynator.Analyzers
  - Microsoft.VisualStudio.Threading.Analyzers
  - DotNetProjectFile.Analyzers (dpfa) — dogfooding the project we contribute to
- Argument validation on every public method (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative` / `ThrowIfLessThan`)
- Source Link via `Microsoft.SourceLink.GitHub` for debugger-friendly NuGet symbols
- Symbol package (`.snupkg`) generated alongside `.nupkg`
- ApiCompat package validation in strict mode (`EnablePackageValidation`, `EnableStrictModeForCompatibleFrameworksInPackage`, `ApiCompatGenerateSuppressionFile`, `ApiCompatEnableRuleCannotChangeParameterName`, `ApiCompatEnableRuleAttributesMustMatch`)
- Public API surface snapshot test via `PublicApiGenerator` + `Verify.TUnit` — any public API change requires explicit acceptance of the `.verified.txt` diff
- SBOM generation via `Microsoft.Sbom.Targets` (Proj0243)
- Reproducible builds: `RestorePackagesWithLockFile=true`, `RestoreLockedMode=true` in CI, deterministic build, embedded sources, NuGet audit mode `all`
- Single intentional dpfa override (`<NoWarn>$(NoWarn);Proj0039</NoWarn>`): we keep `TreatWarningsAsErrors=true` deliberately (rather than dpfa's per-warning opt-in pattern) because immediate failure on new warnings is the right tradeoff for a small, actively-maintained library. Documented in `Directory.Build.props` with full reasoning.

### Initial-release ceremonial omissions (will be addressed by 1.0.0)

- `PackageIcon` and `PackageIconUrl` not yet set (suppresses dpfa Proj0212/Proj0213) — no project icon designed
- `PackageValidationBaselineVersion` not set (suppresses dpfa Proj0241) — no previous version exists to validate against; will be pinned to `0.1.0` from 0.2.0 onward

### Repository hygiene

- GitHub Actions CI (build + test + coverage + pack) on push and PR, with concurrency cancellation, doc-only path-ignore, and 10-minute timeout
- Code coverage collected via `--coverage` flag; uploaded to Codecov (free for public repos)
- CodeQL static analysis on push and PR (skipped on private repos via `if:` condition; auto-enables when public)
- Tag-triggered release workflow (`v[0-9]+.[0-9]+.[0-9]+`) — verifies tag matches `<Version>` in csproj, runs full build+test, packs, pushes to nuget.org via **Trusted Publishing (OIDC, no long-lived API key in the repo)**, creates GitHub Release with auto-generated notes
- Dependabot weekly updates for NuGet and GitHub Actions, with auto-merge of patch + minor bumps after CI passes (major bumps require manual review)
- Issue templates (bug report, feature request) and PR template
- `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), `CONTRIBUTING.md`, `SECURITY.md`
- `.editorconfig`, `.gitattributes`, `global.json` (SDK pinned to 10.0.100, latestFeature roll-forward), `nuget.config` (only nuget.org)
- 41 tests passing on net10.0; `dotnet pack` produces clean `.nupkg` + `.snupkg`

### Background

This package implements the user-space pattern that the TUnit maintainer pointed at when declining [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627). The `[AssertionExtension]` infrastructure that makes this clean shipped in TUnit 1.41.0 via [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785).

[Unreleased]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.2.4...HEAD
[0.2.4]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.2.4
[0.2.3]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.2.3
[0.2.2]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.2.2
[0.2.1]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.2.1
[0.2.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.2.0
[0.1.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.1.0
