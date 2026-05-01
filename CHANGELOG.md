# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] — Initial release

### Added

- Two assertion entry points on `FakeLogCollector`:
  - `HasLogged()` — defaults to "at least 1 matching record"
  - `HasNotLogged()` — fixed at "zero matching records"
- Six filter methods (chain any combination, all AND together):
  - `AtLevel(LogLevel)` — exact level match
  - `Containing(string)` — message contains substring (ordinal)
  - `WithMessage(Func<string, bool>)` — message satisfies predicate
  - `WithException<TException>()` — record's exception is assignable to `TException`
  - `WithProperty(string key, string? value)` — structured-state key matches value (ordinal)
  - `WithCategory(string)` — logger category equals string (ordinal)
- Five terminators on `HasLogged()`:
  - `Once()`, `Exactly(int)`, `AtLeast(int)`, `AtMost(int)`, `Never()`
- Captured-records snapshot in failure messages (level, category, message, exception)
- `.And` / `.Or` chaining via TUnit's `Assertion<T>` base class

### Quality bar

- Targets `net10.0` only (current .NET LTS until Nov 2028)
- `IsAotCompatible=true` — no reflection in the assertion path; safe for AOT-mode test contexts
- `IsTrimmable=true` with trim analyzer enabled
- `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`
- Analyzer pack: Meziantou.Analyzer, SonarAnalyzer.CSharp, Roslynator.Analyzers
- Argument validation on every public method (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative`)
- Source Link via `Microsoft.SourceLink.GitHub` for debugger-friendly NuGet symbols
- Symbol package (`.snupkg`) generated alongside `.nupkg`
- ApiCompat package validation enabled

### Repository hygiene

- GitHub Actions CI (build + test + pack) on push and PR, with concurrency cancellation, doc-only path-ignore, and 10-minute timeout
- CodeQL static analysis on push and PR
- Dependabot weekly updates for NuGet and GitHub Actions
- Issue templates (bug report, feature request) and PR template
- `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), `CONTRIBUTING.md`, `SECURITY.md`
- `.editorconfig`, `.gitattributes`, `global.json` (SDK pinned to 10.0.100, latestFeature roll-forward)

### Background

This package implements the user-space pattern that the TUnit maintainer pointed at when declining [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627). The `[AssertionExtension]` infrastructure that makes this clean shipped in TUnit 1.41.0 via [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785).

[Unreleased]: https://github.com/JohnVerheij/LogAssertions.TUnit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/JohnVerheij/LogAssertions.TUnit/releases/tag/v0.1.0
