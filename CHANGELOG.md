# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.1.0
