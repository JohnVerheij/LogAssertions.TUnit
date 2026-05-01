# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] — Initial release

### Added

- Three assertion entry points on `FakeLogCollector`:
  - `HasLogged()` — defaults to "at least 1 matching record"
  - `HasNotLogged()` — fixed at "zero matching records"
  - `HasLoggedSequence()` — order-preserving sequence matching with `Then()` step terminator
- Ten filter methods (chain any combination, all AND together within a step):
  - `AtLevel(LogLevel)` — exact level match
  - `Containing(string)` — message contains substring (ordinal)
  - `WithMessage(Func<string, bool>)` — message satisfies predicate
  - `WithException<TException>()` — record's exception is assignable to `TException`
  - `WithProperty(string key, string? value)` — structured-state key matches value (ordinal)
  - `WithCategory(string)` — logger category equals string (ordinal)
  - `WithEventId(int)` — `EventId.Id` matches
  - `WithEventName(string)` — `EventId.Name` matches (ordinal)
  - `WithScope<TScope>()` — record emitted while a scope of type `TScope` was active
  - `Where(Func<FakeLogRecord, bool>)` — escape hatch for arbitrary record predicates
- Five terminators on `HasLogged()`:
  - `Once()`, `Exactly(int)`, `AtLeast(int)`, `AtMost(int)`, `Never()`
- Captured-records snapshot in failure messages (level, category, message, exception)
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
- Argument validation on every public method (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative`)
- Source Link via `Microsoft.SourceLink.GitHub` for debugger-friendly NuGet symbols
- Symbol package (`.snupkg`) generated alongside `.nupkg`
- ApiCompat package validation in strict mode (`EnablePackageValidation`, `EnableStrictModeForCompatibleFrameworksInPackage`, `ApiCompatGenerateSuppressionFile`, `ApiCompatEnableRuleCannotChangeParameterName`, `ApiCompatEnableRuleAttributesMustMatch`)
- SBOM generation via `Microsoft.Sbom.Targets` (Proj0243)
- Reproducible builds: `RestorePackagesWithLockFile=true`, `RestoreLockedMode=true` in CI, deterministic build, embedded sources, NuGet audit mode `all`
- Single intentional dpfa override (`<NoWarn>$(NoWarn);Proj0039</NoWarn>`): we keep `TreatWarningsAsErrors=true` deliberately (rather than dpfa's per-warning opt-in pattern) because immediate failure on new warnings is the right tradeoff for a small, actively-maintained library. Documented in `Directory.Build.props` with full reasoning.

### Initial-release ceremonial omissions (will be addressed by 1.0.0)

- `PackageIcon` and `PackageIconUrl` not yet set (suppresses dpfa Proj0212/Proj0213) — no project icon designed
- `PackageValidationBaselineVersion` not set (suppresses dpfa Proj0241) — no previous version exists to validate against; will be pinned to `0.1.0` from 0.2.0 onward

### Repository hygiene

- GitHub Actions CI (build + test + pack) on push and PR, with concurrency cancellation, doc-only path-ignore, and 10-minute timeout
- CodeQL static analysis on push and PR (skipped on private repos via `if:` condition; auto-enables when public)
- Dependabot weekly updates for NuGet and GitHub Actions
- Issue templates (bug report, feature request) and PR template
- `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), `CONTRIBUTING.md`, `SECURITY.md`
- `.editorconfig`, `.gitattributes`, `global.json` (SDK pinned to 10.0.100, latestFeature roll-forward), `nuget.config` (only nuget.org)
- 35 tests passing on net10.0; `dotnet pack` produces clean `.nupkg` + `.snupkg`

### Background

This package implements the user-space pattern that the TUnit maintainer pointed at when declining [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627). The `[AssertionExtension]` infrastructure that makes this clean shipped in TUnit 1.41.0 via [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785).

[Unreleased]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.1.0
