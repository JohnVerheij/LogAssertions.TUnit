# LogAssertions.TUnit

[![CI](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/JohnVerheij/LogAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/LogAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

A TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. Built using TUnit 1.41.0+'s `[AssertionExtension]` source generator, so the assertion entry points integrate directly into TUnit's `Assert.That(...)` pipeline with rich failure diagnostics.

> **Scope:** Test projects only. Not intended for production code.

---

## Table of contents

- [Why this package](#why-this-package)
- [Install](#install)
- [Quick start](#quick-start)
- [Entry points](#entry-points)
- [Filter reference](#filter-reference)
  - [Level filters](#level-filters)
  - [Message filters](#message-filters)
  - [Exception filters](#exception-filters)
  - [Structured-state (property) filters](#structured-state-property-filters)
  - [Scope filters](#scope-filters)
  - [Identity filters (category, event)](#identity-filters-category-event)
  - [Escape hatch](#escape-hatch)
- [Terminators (`HasLogged` only)](#terminators-haslogged-only)
- [Sequence assertions — `HasLoggedSequence`](#sequence-assertions--hasloggedsequence)
- [Combining assertions with `.And` / `.Or`](#combining-assertions-with-and--or)
- [Failure diagnostics](#failure-diagnostics)
- [Cookbook — common patterns](#cookbook--common-patterns)
- [Design notes](#design-notes)
- [Limitations and future work](#limitations-and-future-work)
- [Background](#background)
- [Contributing](#contributing)
- [License](#license)

---

## Why this package

Asserting on log output during tests typically devolves into either:

- Manual `collector.GetSnapshot().Where(...).Count()` plumbing in every test, or
- Adding temporary `Console.WriteLine` calls during debugging because the assertion failure says "expected 1, got 3" without showing what was actually logged.

This library replaces both with a fluent DSL that integrates with TUnit's assertion pipeline and shows every captured record (including structured properties and scope content) in failure messages.

## Install

```
dotnet add package LogAssertions.TUnit
```

**Requirements:** TUnit 1.41.0+ (for `[AssertionExtension]`), .NET 10. The package is AOT-compatible, trimmable, and uses no reflection in the assertion path.

## Package layout

This repo ships **two** NuGet packages:

| Package | Purpose | Depends on |
|---|---|---|
| [`LogAssertions`](https://www.nuget.org/packages/LogAssertions/) | Framework-agnostic core: `ILogRecordFilter` + `LogFilter` + rendering + collector inspection extensions | `Microsoft.Extensions.Diagnostics.Testing` |
| [`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/) | TUnit-specific entry points: `HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()` and shorthands | `LogAssertions` + `TUnit.Assertions` |

You install `LogAssertions.TUnit`; `LogAssertions` comes transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are *not* shipped today — they'd reuse the `LogAssertions` core. If you'd find one useful, [open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml).

## Quick start

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

[Test]
public async Task Validation_failure_is_logged()
{
    var collector = new FakeLogCollector();
    using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
    var logger = loggerFactory.CreateLogger<MyValidator>();

    new MyValidator(logger).Validate(invalidInput);

    await Assert.That(collector)
        .HasLogged()
        .AtLevel(LogLevel.Warning)
        .Containing("validation failed", StringComparison.Ordinal)
        .WithCategory("MyApp.MyValidator")
        .Once();

    await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Error);
}
```

---

## Entry points

Three assertion entry points are emitted by TUnit's source generator and surface as extension methods on `Assert.That(FakeLogCollector)`.

| Entry point | Default expectation | Terminators allowed |
|---|---|---|
| `HasLogged()` | At least 1 matching record | All count terminators (see below) |
| `HasNotLogged()` | Zero matching records | None — fixed at zero |
| `HasLoggedSequence()` | An ordered series of matches; `Then()` separates steps | None — each step's match is implicit |

All three accept the full filter chain. `HasLogged()` is the workhorse; `HasNotLogged()` is its inverse with cleaner failure semantics; `HasLoggedSequence()` is for multi-step traces (e.g. *"Started → Validation failed → Stopped"*).

---

## Filter reference

Filters chain freely. Within a single assertion (or within a single sequence step) every filter is **AND**-combined: a record matches only when every filter's predicate holds.

### Level filters

| Filter | Behaviour |
|---|---|
| `AtLevel(LogLevel)` | Exact level match |
| `AtLevelOrAbove(LogLevel)` | `record.Level >= threshold` (e.g. *"any warning or worse"*) |
| `AtLevelOrBelow(LogLevel)` | `record.Level <= threshold` (e.g. *"only diagnostic-tier"*) |

```csharp
await Assert.That(collector).HasLogged().AtLevelOrAbove(LogLevel.Warning).AtLeast(1);
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
```

### Message filters

| Filter | Behaviour |
|---|---|
| `Containing(string substring, StringComparison comparison)` | Formatted message contains substring (comparison **explicit by design** — no implicit culture) |
| `WithMessage(Func<string, bool> predicate)` | Predicate over the formatted message |
| `WithMessageTemplate(string template)` | The pre-substitution template (e.g. `"Order {OrderId} processed"`) equals `template` exactly. Resolved from MEL's magic `{OriginalFormat}` structured-state entry |

`WithMessageTemplate` is useful when you want to pin a specific call site without coupling to the substituted parameter values:

```csharp
// matches every "Order N processed" log regardless of N
await Assert.That(collector).HasLogged()
    .WithMessageTemplate("Order {OrderId} processed").AtLeast(1);
```

### Exception filters

| Filter | Behaviour |
|---|---|
| `WithException<TException>()` | `record.Exception is TException` (assignable) |
| `WithExceptionMessage(string substring)` | `record.Exception?.Message` contains substring (ordinal); records without an exception never match |

```csharp
await Assert.That(collector).HasLogged()
    .WithException<TimeoutException>()
    .WithExceptionMessage("connection")
    .Once();
```

### Structured-state (property) filters

`Microsoft.Extensions.Logging` exposes structured properties on each record (the parameters captured by `LoggerMessage` source generators or by message-template logging calls).

| Filter | Behaviour |
|---|---|
| `WithProperty(string key, string? value)` | Property's formatted string value equals `value` (ordinal) |
| `WithProperty(string key, Func<string?, bool> predicate)` | Predicate over the formatted string value (use for ranges, regex, or null-checks) |

Note: `FakeLogRecord` exposes structured-state values as **strings** (the formatted form), so the predicate receives a `string?`. Parse to your target type inside the predicate when needed:

```csharp
await Assert.That(collector).HasLogged()
    .WithProperty("OrderId", v =>
        int.TryParse(v, CultureInfo.InvariantCulture, out var n) && n > 1000)
    .AtLeast(1);
```

### Scope filters

Scopes are values pushed via `logger.BeginScope(...)`. They surround any log records emitted while the scope is active.

| Filter | Behaviour |
|---|---|
| `WithScope<TScope>()` | A scope of type `TScope` was active when the record was emitted |
| `WithScopeProperty(string key, object? value)` | A scope contains a property `key` matching `value` (`object.Equals` semantics) |
| `WithScopeProperty(string key, Func<object?, bool> predicate)` | A scope contains a property `key` whose value satisfies the predicate |

Scope-property filters recognise the two AOT-friendly idioms:

```csharp
// dictionary scope — the canonical structured pattern
using (logger.BeginScope(new Dictionary<string, object?> { ["OrderId"] = 42 }))
    DoWork();

await Assert.That(collector).HasLogged().WithScopeProperty("OrderId", 42).AtLeast(1);
```

```csharp
// formatted-template scope via LoggerMessage.DefineScope (avoids CA1848)
private static readonly Func<ILogger, int, IDisposable?> OrderScope =
    LoggerMessage.DefineScope<int>("Order {OrderId}");

using (OrderScope(logger, 42)) DoWork();

await Assert.That(collector).HasLogged().WithScopeProperty("OrderId", 42).AtLeast(1);
```

> **Anonymous-object scopes** (`logger.BeginScope(new { OrderId = 42 })`) are **not** recognised by `WithScopeProperty` — reading their fields requires reflection, which would compromise AOT-compatibility. Prefer dictionary or `LoggerMessage.DefineScope` form.

### Identity filters (category, event)

| Filter | Behaviour |
|---|---|
| `WithCategory(string)` | Logger category equals string (ordinal) |
| `WithEventId(int)` | `EventId.Id` equals value |
| `WithEventName(string)` | `EventId.Name` equals string (ordinal) |

```csharp
await Assert.That(collector).HasLogged()
    .WithCategory("MyApp.Bootstrap")
    .WithEventName("Startup")
    .Once();
```

### Escape hatch

| Filter | Behaviour |
|---|---|
| `Where(Func<FakeLogRecord, bool> predicate)` | Arbitrary predicate over the full `FakeLogRecord` |

Use only when no other filter expresses the constraint cleanly — composing built-in filters is preferred for diagnostic clarity in failure messages.

---

## Terminators (`HasLogged` only)

Terminators express the count expectation. Pick exactly one — chain it after all filters. `HasNotLogged` has no terminators (the expectation is fixed at zero matches).

| Terminator | Match count expectation |
|---|---|
| `Once()` | Exactly 1 |
| `Exactly(int count)` | Exactly N |
| `AtLeast(int count)` | At least N (inclusive) |
| `AtMost(int count)` | At most N (inclusive) |
| `Between(int min, int max)` | Inclusive range `[min, max]` |
| `Never()` | Exactly 0 (semantic synonym for `HasNotLogged()`) |

```csharp
await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Between(1, 5);
await Assert.That(collector).HasLogged().WithEventId(42).Never();
```

---

## Sequence assertions — `HasLoggedSequence`

For tests that need to verify a series of records appeared in order:

```csharp
await Assert.That(collector).HasLoggedSequence()
    .AtLevel(LogLevel.Information).Containing("Started",          StringComparison.Ordinal)
    .Then()
    .AtLevel(LogLevel.Warning)    .Containing("validation failed", StringComparison.Ordinal)
    .Then()
    .AtLevel(LogLevel.Information).Containing("Stopped",          StringComparison.Ordinal);
```

Semantics:

- The walk is **order-preserving but not contiguous** — records between matches are skipped.
- `Then()` commits the current step's filters and starts a new step.
- Each step's filters AND-combine, exactly like the single-match assertions.
- A step with no filters always matches the next available record (use sparingly).
- Failure diagnostics indicate which step failed and dump the full captured-records list (see [Failure diagnostics](#failure-diagnostics)).

---

## Combining assertions with `.And` / `.Or`

Because the assertion types derive from TUnit's `Assertion<T>`, the standard TUnit chaining works:

```csharp
await Assert.That(collector)
    .HasLogged().AtLevel(LogLevel.Information).AtLeast(1)
    .And.HasNotLogged().AtLevel(LogLevel.Error);
```

---

## Failure diagnostics

On a failed assertion the `AssertionException` message includes:

1. The expectation (terminator + filter summary)
2. The actual match count
3. A snapshot of every captured record, with **4-character level abbreviation** (matching the `Microsoft.Extensions.Logging` console formatter), category, message, structured properties, active scopes, and exception details

Example failure output:

```
Expected: exactly 1 log record(s) to have been logged matching: Level = Warning, Message contains "timeout"

3 record(s) matched

Captured records (5 total):
  [info] MyApp.Worker: Started cycle 1
    props: cycle=1
    scope: RequestId=abc-123
  [warn] MyApp.Worker: timeout exceeded for cycle 1
    props: cycle=1, threshold=500
    scope: RequestId=abc-123
  [warn] MyApp.Worker: timeout exceeded for cycle 2
    props: cycle=2, threshold=500
    scope: RequestId=abc-123
  [warn] MyApp.Worker: timeout exceeded for cycle 3
    props: cycle=3, threshold=500
    scope: RequestId=abc-123
  [info] MyApp.Worker: Cycle batch finished
    scope: RequestId=abc-123
    exception: TimeoutException: Connection timed out
```

Level abbreviations: `trce`, `dbug`, `info`, `warn`, `fail`, `crit` (matching MEL's console formatter; `none` for `LogLevel.None`).

This eliminates the historical pattern of adding temporary `Console.WriteLine` calls to debug failing log assertions — every dimension you can filter on is also rendered in the failure message.

---

## Cookbook — common patterns

### Assert no errors were logged

```csharp
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
```

### Assert a specific call site was hit

Anchored on the message template, not the substituted value:

```csharp
await Assert.That(collector).HasLogged()
    .WithMessageTemplate("Order {OrderId} processed").AtLeast(1);
```

### Assert a structured property is in a numeric range

```csharp
await Assert.That(collector).HasLogged()
    .WithProperty("DurationMs", v =>
        int.TryParse(v, CultureInfo.InvariantCulture, out var ms) && ms < 1000)
    .AtLeast(1);
```

### Assert all logs in a request scope were warnings or below

```csharp
await Assert.That(collector).HasNotLogged()
    .WithScopeProperty("RequestId", "req-42")
    .AtLevelOrAbove(LogLevel.Error);
```

### Assert a specific exception flowed through a logger

```csharp
await Assert.That(collector).HasLogged()
    .AtLevel(LogLevel.Error)
    .WithException<DbUpdateConcurrencyException>()
    .Once();
```

### Assert a startup → work → shutdown sequence

```csharp
await Assert.That(collector).HasLoggedSequence()
    .WithEventName("Startup")
    .Then().AtLevel(LogLevel.Information).Containing("processed", StringComparison.Ordinal)
    .Then().WithEventName("Shutdown");
```

### Assert exactly N retries fired

```csharp
await Assert.That(collector).HasLogged()
    .AtLevel(LogLevel.Warning)
    .WithMessageTemplate("Retrying after {Delay}ms")
    .Exactly(3);
```

---

## Design notes

- **Built on `[AssertionExtension]`** (TUnit 1.41.0+, [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785)): the entry-point methods are emitted by TUnit's source generator. No extension-method wrappers needed.
- **No cross-package coupling.** This package depends on `TUnit.Assertions` and `Microsoft.Extensions.Diagnostics.Testing`. Neither of those depends on the other; this library is the bridge.
- **AOT-compatible / trimmable.** `IsAotCompatible=true`, `IsTrimmable=true`, `EnableTrimAnalyzer=true`. No reflection in the assertion path. Scope-property matching uses interface casts only, never reflection.
- **Single TFM, forward-only by policy:** targets `net10.0` and only `net10.0`. .NET 10 is the current LTS (until November 2028); future versions will track the latest LTS, never multi-target downward. As a test-only library this works cleanly even when your application code targets an older TFM — bump only your test project's TFM to consume this package. The policy keeps the codebase free of compatibility shims and lets the library use the newest C# / runtime / `Microsoft.Extensions.Logging` features as they ship.
- **Explicit `StringComparison`.** Every string-matching API requires the caller to pass a `StringComparison` (or uses `Ordinal` internally where unambiguous). No silent culture defaults.

---

## Stability intent (pre-1.0)

Per [SemVer](https://semver.org/), the `0.x` series is initial development — anything *may* change in any minor version, and there is no formal contract yet. The intent below documents what we *try* to keep stable so consumers can plan. A `1.0` release will turn this from intent into contract.

**Intended-stable (we will not break these without a CHANGELOG-flagged reason and a clear migration path):**

- The three entry-point methods on `IAssertionSource<FakeLogCollector>`: `HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()`.
- The top-level shorthand entry points (`HasLoggedOnce`, `HasLoggedExactly`, `HasLoggedNothing`, `HasLoggedWarningOrAbove`, etc.).
- The fluent chain methods on `HasLoggedAssertion`, `HasNotLoggedAssertion`, `HasLoggedSequenceAssertion`: every named filter (`AtLevel`, `Containing`, `WithCategory`, etc.), every terminator (`Once`, `Exactly`, `Between`, etc.), and the combinator methods (`WithFilter`, `MatchingAny`, `MatchingAll`, `Not`, `When`).
- The `ILogRecordFilter` interface and the `LogFilter` static factory's public methods.
- The `LogCollectorBuilder.Create` factory.
- The `FakeLogCollector` extension methods: `Filter`, `CountMatching`, `DumpTo`, `AssertAllAsync`.

**Explicitly unstable (will change without notice, do not depend on):**

- `LogAssertionBase<TSelf>` and its protected/internal members. The type is `public` only because the CRTP pattern requires it (C# does not allow public classes to inherit from internal); it is annotated `[EditorBrowsable(Never)]` and is **not** a supported derivation point. Treat it as a sealed implementation detail of the three public assertion classes.
- The internal filter classes (`PredicateFilter`, `AndFilter`, `OrFilter`, `NotFilter`). These live behind `ILogRecordFilter` and the `LogFilter` factory.
- The exact format of failure-message snapshot text rendered by `LogAssertionRendering` and exposed via `DumpTo`. The rendering may gain extra detail or change formatting in any release. **Do not pin exact failure-message text in tests** — pin filter match counts and broad markers (e.g. `Contains("[warn]")`) only.
- The `CompatibilitySuppressions.xml` file is a build artifact tracking baseline acceptance, not part of the API contract.

**Breaking changes log (every release with a breaking change is listed in CHANGELOG.md):**

- **0.2.0:** `LogAssertionBase<TSelf>` annotated `[EditorBrowsable(Never)]`; the `protected virtual void AddPredicate(Func, string)` extension hook replaced by `protected virtual void AddFilter(ILogRecordFilter)` as part of the `ILogRecordFilter` refactor. Affects only consumers who derived from `LogAssertionBase` (an unsupported scenario). Framework-agnostic types (`ILogRecordFilter`, `LogFilter`, etc.) moved from `LogAssertions.TUnit` to a new `LogAssertions` package + namespace; the `LogAssertions.TUnit` package now has a `LogAssertions` transitive dependency.

---

## Limitations and future work

The 0.1.0 surface (3 entry points, 14 filters, 6 terminators, sequence assertions) covers the common cases. Possible additions if demand surfaces:

- **Anonymous-object scope inspection** — would require reflection; intentionally out of scope for AOT-compatibility. Prefer dictionary or `LoggerMessage.DefineScope` scopes.
- **`HasLoggedSequenceContiguously(...)` variant** — strict adjacency rather than order-preserving.
- **`WithTimestamp(...)` filter** — currently considered fragile in tests; not planned.
- **Source-generated assertions for project-specific log message methods** — would let `.HasLogged().ValidationFailed("...")` chain against a `[LoggerMessage]` declaration.

If you'd find any of these useful, [open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml).

---

## Background

The TUnit feature request that motivated this package was [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627), declined on architectural grounds (no cross-package coupling between `TUnit.Logging.Microsoft` and `TUnit.Assertions`). The user-space pattern was unblocked when [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785) shipped `[AssertionExtension]` infrastructure in TUnit 1.41.0. This package implements the user-space pattern.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branch convention, PR checklist, and code style.

## License

[MIT](LICENSE) — Copyright (c) 2026 John Verheij
