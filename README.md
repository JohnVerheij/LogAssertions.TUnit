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
- [Package layout](#package-layout)
- [Namespaces (and a `GlobalUsings.cs` recommendation)](#namespaces-and-a-globalusingscs-recommendation)
- [Quick start](#quick-start)
- [Migrating from manual assertions](#migrating-from-manual-assertions)
- [Entry points](#entry-points)
  - [Shorthand entry points](#shorthand-entry-points)
- [Filter reference](#filter-reference)
  - [Level filters](#level-filters)
  - [Message filters](#message-filters)
  - [Exception filters](#exception-filters)
  - [Structured-state (property) filters](#structured-state-property-filters)
  - [Scope filters](#scope-filters)
  - [Identity filters (category, event)](#identity-filters-category-event)
  - [Escape hatch](#escape-hatch)
  - [Combinator chain methods (`MatchingAny`, `MatchingAll`, `Not`, `WithFilter`)](#combinator-chain-methods-matchingany-matchingall-not-withfilter)
  - [Conditional configuration (`When`)](#conditional-configuration-when)
- [Terminators (`HasLogged` only)](#terminators-haslogged-only)
- [Sequence assertions — `HasLoggedSequence`](#sequence-assertions--hasloggedsequence)
- [Combining assertions with `.And` / `.Or`](#combining-assertions-with-and--or)
- [Batch assertions — `AssertAllAsync`](#batch-assertions--assertallasync)
- [Non-asserting inspection](#non-asserting-inspection)
- [Failure diagnostics](#failure-diagnostics)
- [Cookbook — common patterns](#cookbook--common-patterns)
- [Design notes](#design-notes)
- [Stability intent (pre-1.0)](#stability-intent-pre-10)
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

## Namespaces (and a `GlobalUsings.cs` recommendation)

The two packages place types in two namespaces with deliberately-different scopes:

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| `HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()` (the source-generated entry points) | `TUnit.Assertions.Extensions` | **Yes** — TUnit auto-imports this namespace |
| `HasLoggedOnce()`, `HasLoggedExactly()`, ... (shorthand entry points, since 0.2.2) | `TUnit.Assertions.Extensions` | **Yes** — same auto-import path |
| `LogCollectorBuilder.Create(...)` (the `(factory, collector)` factory) | `LogAssertions` | **No** — needs `using LogAssertions;` |
| `LogFilter.AtLevel(...)`, `ILogRecordFilter`, `Filter`/`CountMatching`/`DumpTo` extensions | `LogAssertions` | **No** — needs `using LogAssertions;` |
| `AssertAllAsync(...)` batch terminator | `TUnit.Assertions.Extensions` | **Yes** — same auto-import path |

**Practical consequence:** test files that *only* call assertion entry points need no `using` from this package. Files that use `LogCollectorBuilder` or build composable filters via `LogFilter` need `using LogAssertions;`.

**Recommended:** put all four into a single `GlobalUsings.cs` in your test project so every test file sees them without ceremony:

```csharp
// tests/MyApp.Tests/GlobalUsings.cs
global using System;                                     // StringComparison
global using LogAssertions;                              // LogCollectorBuilder, LogFilter, etc.
global using Microsoft.Extensions.Logging;               // LogLevel
global using Microsoft.Extensions.Logging.Testing;       // FakeLogCollector, FakeLoggerProvider
```

`System` is included because every `Containing(...)` filter call requires `StringComparison`. Test projects that disable `<ImplicitUsings>enable</ImplicitUsings>` (common in strict-analysis codebases) won't get `System` for free, so this entry prevents a compile error on the first `Containing()` call.

This setup also eliminates the IDE0005 ("unnecessary using") chatter that otherwise appears in test files that don't directly use `LogCollectorBuilder` but live alongside ones that do.

## Quick start

```csharp
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

[Test]
public async Task Validation_failure_is_logged()
{
    var (factory, collector) = LogCollectorBuilder.Create();
    using (factory)
    {
        var logger = factory.CreateLogger<MyValidator>();
        new MyValidator(logger).Validate(invalidInput);

        await Assert.That(collector)
            .HasLogged()
            .AtLevel(LogLevel.Warning)
            .Containing("validation failed", StringComparison.Ordinal)
            .WithCategory("MyApp.MyValidator")
            .Once();

        await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Error);
    }
}
```

**Lifetime / disposal:** the `IDisposable` returned from `LogCollectorBuilder.Create()` (the `factory`) owns the underlying `FakeLoggerProvider`. Disposing it stops new log records from being captured but the records already gathered into the `collector` snapshot remain valid — you can continue to query the `collector` after the `using` block exits. Both block-form (`using (factory) { ... }`) and declaration-form (`using ILoggerFactory factory = ...`) work; pick whichever fits your test layout.

---

## Migrating from manual assertions

If you already use `FakeLogCollector` directly, the typical "before" pattern is to pull a snapshot, filter with LINQ, and write multiple assertions against the result:

```csharp
// Before — manual:
var records = collector.GetSnapshot();
var warnings = records.Where(r => r.Level == LogLevel.Warning).ToList();
await Assert.That(warnings).HasCount().EqualTo(1);
await Assert.That(warnings[0].Message).Contains("timeout", StringComparison.Ordinal);
```

The "after" with `LogAssertions.TUnit` is one fluent chain that reads as a single intent and produces a failure message with every captured record (including structured state and scope content) when the chain doesn't match:

```csharp
// After — LogAssertions.TUnit:
await Assert.That(collector)
    .HasLogged()
    .AtLevel(LogLevel.Warning)
    .Containing("timeout", StringComparison.Ordinal)
    .Once();
```

Two practical wins on top of the readability:

- **Failure diagnostics.** The "before" snippet's failure says *"expected 1, got 3"* (or *"expected to contain 'timeout'"*). The "after" snippet's failure renders the full captured-records snapshot — level, category, message, structured state, scope — so you can see *which* three records came through and why none matched, without adding `Console.WriteLine` calls.
- **Composability.** The same chain extends to scopes (`InScope("RequestId", id)`), structured properties (`WithProperty("UserId", 42)`), exception types (`WithExceptionOfType<TimeoutException>()`), and combinator nodes (`MatchingAny(...)`, `Not(...)`) without restructuring the test.

See the [Cookbook](#cookbook--common-patterns) for the patterns this replaces in practice.

---

## Entry points

Three core entry points are emitted by TUnit's source generator and surface as extension methods on `Assert.That(FakeLogCollector)`.

| Entry point | Default expectation | Terminators allowed |
|---|---|---|
| `HasLogged()` | At least 1 matching record | All count terminators (see below) |
| `HasNotLogged()` | Zero matching records | None — fixed at zero |
| `HasLoggedSequence()` | An ordered series of matches; `Then()` separates steps | None — each step's match is implicit |

All three accept the full filter chain. `HasLogged()` is the workhorse; `HasNotLogged()` is its inverse with cleaner failure semantics; `HasLoggedSequence()` is for multi-step traces (e.g. *"Started → Validation failed → Stopped"*).

### Shorthand entry points

Wrappers that pre-configure the most common chains. Each returns the underlying assertion type so additional filters can still be appended.

| Shorthand | Equivalent to |
|---|---|
| `HasLoggedOnce()` | `HasLogged().Once()` |
| `HasLoggedExactly(int)` | `HasLogged().Exactly(int)` |
| `HasLoggedAtLeast(int)` | `HasLogged().AtLeast(int)` |
| `HasLoggedAtMost(int)` | `HasLogged().AtMost(int)` |
| `HasLoggedBetween(int, int)` | `HasLogged().Between(int, int)` |
| `HasLoggedNothing()` | `HasNotLogged()` (no filters — asserts the collector is empty) |
| `HasLoggedWarningOrAbove()` | `HasLogged().AtLevelOrAbove(LogLevel.Warning)` |
| `HasLoggedErrorOrAbove()` | `HasLogged().AtLevelOrAbove(LogLevel.Error)` |

```csharp
await Assert.That(collector).HasLoggedOnce().AtLevel(LogLevel.Warning).Containing("retry", StringComparison.Ordinal);
await Assert.That(collector).HasLoggedNothing();
await Assert.That(collector).HasLoggedErrorOrAbove();
```

---

## Filter reference

Filters chain freely. Within a single assertion (or within a single sequence step) every filter is **AND**-combined: a record matches only when every filter's predicate holds.

### Level filters

| Filter | Behaviour |
|---|---|
| `AtLevel(LogLevel)` | Exact level match |
| `AtLevelOrAbove(LogLevel)` | `record.Level >= threshold` (e.g. *"any warning or worse"*) |
| `AtLevelOrBelow(LogLevel)` | `record.Level <= threshold` (e.g. *"only diagnostic-tier"*) |
| `AtAnyLevel(params LogLevel[])` | Match any level in the supplied set (e.g. *"Warning or Error but not Critical"*) |
| `NotAtLevel(LogLevel)` | Inverse of `AtLevel` — convenience over `Not(LogFilter.AtLevel(...))` |
| `ExcludingLevel(LogLevel)` | Alias for `NotAtLevel`, reads better in negative-filter chains |

```csharp
await Assert.That(collector).HasLogged().AtLevelOrAbove(LogLevel.Warning).AtLeast(1);
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
await Assert.That(collector).HasLogged().AtAnyLevel(LogLevel.Warning, LogLevel.Error).AtLeast(1);
```

### Message filters

| Filter | Behaviour |
|---|---|
| `Containing(string substring, StringComparison comparison)` | Formatted message contains substring (comparison **explicit by design** — no implicit culture) |
| `ContainingAll(StringComparison, params string[])` | Formatted message contains every one of the substrings |
| `ContainingAny(StringComparison, params string[])` | Formatted message contains at least one of the substrings |
| `Matching(Regex)` | Formatted message matches the regex |
| `WithMessage(Func<string, bool> predicate)` | Predicate over the formatted message |
| `WithMessageTemplate(string template)` | The pre-substitution template (e.g. `"Order {OrderId} processed"`) equals `template` exactly. Resolved from MEL's magic `{OriginalFormat}` structured-state entry |
| `NotContaining(string, StringComparison)` | Inverse of `Containing` — convenience over `Not(LogFilter.Containing(...))` |

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
| `WithException()` | Any record with a non-null `Exception`, regardless of type |
| `WithException(Func<Exception, bool> predicate)` | Predicate over the exception (predicate not invoked for null exception) |
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
| `WithLoggerName(string)` | Alias for `WithCategory` |
| `ExcludingCategory(string)` | Inverse of `WithCategory` |
| `WithEventId(int)` | `EventId.Id` equals value |
| `WithEventIdInRange(int min, int max)` | `EventId.Id` is within the inclusive range |
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

### Combinator chain methods (`MatchingAny`, `MatchingAll`, `Not`, `WithFilter`)

The fluent chain is implicitly AND-combined. These four chain methods let you compose richer expressions inside the chain without dropping to `Where`:

| Method | Behaviour |
|---|---|
| `MatchingAny(params ILogRecordFilter[])` | OR of the supplied filters as one composite filter on the chain. Empty array matches no record. |
| `MatchingAll(params ILogRecordFilter[])` | Explicit AND of the supplied filters. Empty array matches every record. |
| `Not(ILogRecordFilter)` | Negates the supplied filter. |
| `WithFilter(ILogRecordFilter)` | Adds a user-supplied or pre-built filter to the chain. |

```csharp
// "level == Warning AND (msg contains "a" OR msg contains "b")"
await Assert.That(collector).HasLogged()
    .AtLevel(LogLevel.Warning)
    .MatchingAny(
        LogFilter.Containing("a", StringComparison.Ordinal),
        LogFilter.Containing("b", StringComparison.Ordinal))
    .AtLeast(1);

// Reusable filter shared across many tests:
static readonly ILogRecordFilter CriticalDbError = LogFilter.All(
    LogFilter.AtLevel(LogLevel.Critical),
    LogFilter.WithException<DbException>());

await Assert.That(collector).HasLogged().WithFilter(CriticalDbError).AtLeast(1);
```

### Conditional configuration (`When`)

```csharp
// In a parameterised test, fold a boolean branch into the chain
// instead of duplicating the entire await:
await Assert.That(collector).HasLogged()
    .AtLevel(LogLevel.Warning)
    .When(expectRetry, b => b.Containing("retry", StringComparison.Ordinal))
    .AtLeast(1);
```

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

**`Never()` vs `HasNotLogged()` — when to use which.** They produce identical assertions; the only difference is reading order. **Prefer `HasNotLogged()`** when "this should not happen" is the primary intent of the test (the negative is the headline). **Use `.Never()`** when you've already started building a positive filter chain and only at the end realise you expect zero matches — saves rewriting the prefix. Don't agonise over the choice; either reads clearly to a future maintainer.

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

Because the assertion types derive from TUnit's `Assertion<T>`, the standard TUnit chaining works. **`.And` is genuinely useful for log assertions** — chain a positive and a negative invariant in one expression:

```csharp
await Assert.That(collector)
    .HasLogged().AtLevel(LogLevel.Information).AtLeast(1)
    .And.HasNotLogged().AtLevel(LogLevel.Error);
```

For three-or-more conditions, prefer the dedicated [`AssertAllAsync`](#batch-assertions--assertallasync) batch terminator — it aggregates failures into a single message rather than failing fast on the first.

**`.Or` is rarely useful for log assertions.** "Either no errors were logged OR a specific recovery was logged" is a contrived shape; in practice tests want both, not either. The mechanism is available via TUnit if you need it, but the cookbook below shows no examples because the use case is genuinely uncommon. If you find yourself reaching for `.Or`, consider whether `MatchingAny(...)` (an OR of *filters*, not whole assertions) expresses the intent more clearly.

---

## Batch assertions — `AssertAllAsync`

Run several independent assertions against the same collector in one pass and aggregate every failure into a single `AssertionException`. Conceptually similar to TUnit's own `Assert.Multiple`, scoped to log assertions. Useful when several invariants must all hold and the test author wants to see every violation in one CI run, not just the first.

```csharp
await Assert.That(collector).AssertAllAsync(
    c => c.HasLogged().AtLevel(LogLevel.Information).AtLeast(1),
    c => c.HasNotLogged().AtLevel(LogLevel.Error),
    c => c.HasLoggedSequence()
        .Containing("Started", StringComparison.Ordinal)
        .Then().Containing("Stopped", StringComparison.Ordinal));
```

If two of three fail, the thrown exception's message lists both — not just the first.

A second overload (added in 0.2.1) accepts the more verbose `async c => await c.HasLogged()...` form for cases where the lambda needs to mix in non-assertion async work between checks. Pick whichever is clearer for the case at hand; both have identical failure-aggregation semantics.

---

## Non-asserting inspection

Sometimes a test wants to inspect what was logged without asserting — for further calculations, debugging output, or cross-checking. The core package adds three extensions on `FakeLogCollector`:

| Method | Returns |
|---|---|
| `Filter(params ILogRecordFilter[] filters)` | The matching records as a defensive `IReadOnlyList<FakeLogRecord>` |
| `CountMatching(params ILogRecordFilter[] filters)` | Just the match count (no list materialisation) |
| `DumpTo(TextWriter writer)` | Writes every captured record in the failure-message format |

```csharp
// Inspect without asserting
var warnings = collector.Filter(LogFilter.AtLevel(LogLevel.Warning));
int errors = collector.CountMatching(
    LogFilter.AtLevelOrAbove(LogLevel.Error),
    LogFilter.WithException<DbException>());

// Print the entire snapshot to test output during development
using var writer = new StringWriter();
collector.DumpTo(writer);
Console.WriteLine(writer);
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

### Set up the collector in one line

```csharp
var (factory, collector) = LogCollectorBuilder.Create();
using (factory)
{
    var logger = factory.CreateLogger("MyService");
    new MyService(logger).DoWork();
    await Assert.That(collector).HasLoggedOnce().Containing("done", StringComparison.Ordinal);
}
```

### Reuse a filter across many tests

```csharp
// Define once in a test base class:
private static readonly ILogRecordFilter CriticalDbError = LogFilter.All(
    LogFilter.AtLevel(LogLevel.Critical),
    LogFilter.WithException<DbException>());

// Use in many tests:
await Assert.That(collector).HasNotLogged().WithFilter(CriticalDbError);
await Assert.That(otherCollector).HasLoggedExactly(1).WithFilter(CriticalDbError);
```

### Assert several invariants and report all failures together

```csharp
await Assert.That(collector).AssertAllAsync(
    c => c.HasLogged().AtLevel(LogLevel.Information).AtLeast(1),
    c => c.HasNotLogged().AtLevelOrAbove(LogLevel.Error),
    c => c.HasLoggedSequence()
        .WithEventName("Startup")
        .Then().WithEventName("Shutdown"));
```

### Assert "Warning OR Error in this scope, but not Critical"

```csharp
await Assert.That(collector).HasLogged()
    .WithScopeProperty("RequestId", "req-42")
    .AtAnyLevel(LogLevel.Warning, LogLevel.Error)
    .AtLeast(1);
```

### Inspect what was actually logged during test development

```csharp
// Run your code-under-test, then dump everything to the test output:
using var writer = new StringWriter();
collector.DumpTo(writer);
Console.WriteLine(writer);

// Or get a typed handle on the matching records for further checks:
var retries = collector.Filter(
    LogFilter.AtLevel(LogLevel.Warning),
    LogFilter.Containing("retry", StringComparison.Ordinal));
```

---

## Design notes

- **Built on `[AssertionExtension]`** (TUnit 1.41.0+, [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785)): the entry-point methods are emitted by TUnit's source generator. No extension-method wrappers needed.
- **No cross-package coupling.** This package depends on `TUnit.Assertions` and `Microsoft.Extensions.Diagnostics.Testing`. Neither of those depends on the other; this library is the bridge.
- **AOT-compatible / trimmable.** `IsAotCompatible=true`, `IsTrimmable=true`, `EnableTrimAnalyzer=true`. No reflection in the assertion path. Scope-property matching uses interface casts only, never reflection.
- **Single TFM, forward-only by policy:** targets `net10.0` and only `net10.0`. .NET 10 is the current LTS (until November 2028); future versions will track the latest LTS, never multi-target downward. The policy keeps the codebase free of compatibility shims and lets the library use the newest C# / runtime / `Microsoft.Extensions.Logging` features as they ship.

  **You can still consume this package even if your production code targets an older TFM.** Test projects routinely target a higher TFM than the production code they test — the .NET SDK supports a `net10` test project referencing a `net8` production project (`net10` runtime is forward-compatible with `net8` assemblies). The test exe loads on the `net10` runtime and invokes the production code through its `net8` surface. The reverse — referencing a `net10` production lib from a `net8` test — does not work, but that's not a typical setup.

  Concrete: if your production lib targets `net8.0`, set your test project's `<TargetFramework>` to `net10.0`, install `LogAssertions.TUnit`, and the production `<ProjectReference>` continues to resolve cleanly.
- **Explicit `StringComparison`.** Every string-matching API requires the caller to pass a `StringComparison` (or uses `Ordinal` internally where unambiguous). No silent culture defaults.
- **Source Link + deterministic builds.** Both packages ship with [`Microsoft.SourceLink.GitHub`](https://github.com/dotnet/sourcelink), a separate `.snupkg` symbol package, and embedded sources (`EmbedUntrackedSources`). When a debugger steps into the assertion code, the source is fetched directly from this GitHub repo at the exact commit the package was built from — useful when you're investigating why a filter didn't match the record you expected. Builds are deterministic by default (the SDK's `<Deterministic>true</Deterministic>`); the snapshot test project is the one exception (Verify needs absolute PDB paths, so its build is non-deterministic — that project's binaries are not shipped).

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

The 0.2.0 surface covers the high-frequency 80% of real-world log-assertion needs — composable filters, all common count terminators, sequence assertions, scope-property matching, batch assertions, the inspection extensions, and the framework-agnostic core split. The list below is the candidate backlog for future versions; nothing here is committed and nothing will be built without demonstrated demand.

### Plausible v0.3.0 (would make the library substantially more capable)

These need new primitives (timestamp + polling + cursor) but are coherent additions, not architectural shifts.

- **Time-based filters:** `WithElapsedTime(min, max)`, `WithTimestamp(at, tolerance)`, `ThenGap(TimeSpan)` in sequence, `Throttled(maxPerWindow)` for rate-limit verification.
- **Async-await polling terminator:** `WithinTimeout(TimeSpan)` for tests against background services / event handlers, replacing the brittle `await Task.Delay(...)` pattern.
- **Sequence variants:** `ThenImmediately()` (strict adjacency), `NotInterleaved()` (no other records from same category between matches), `InOrder()` terminator on `HasLogged` (multiple matches in chronological order, not necessarily adjacent).
- **Cursor / direction:** `FromNewest()` / `FromOldest()` direction control, `SinceLastAssert()` watermark, `Pin()` snapshot pinning, `HasLoggedDistinct(int)` (dedupe + count).
- **`HasNotLoggedSequence()`** — mirror of `HasLoggedSequence`, asserts a specific sequence did NOT occur.
- **`DescribedAs(string label)` on filters** (queued from real consumer feedback) — when a `Where(predicate)` or composed `MatchingAny`/`All` is used, let the caller attach a human-readable label that shows in failure diagnostics instead of the generic `"Custom predicate"` / `"(... AND ...)"` rendering.
- **`DumpToTestOutput()` extension** (queued from real consumer feedback) — TUnit-aware variant of `DumpTo(TextWriter)` that routes captured records to TUnit's `TestContext.OutputWriter` automatically, eliminating the `using var sw = new StringWriter(); ... Console.WriteLine(sw)` boilerplate during test development.
- **External-consumer smoke-test project in CI** (process improvement queued from real consumer feedback) — a deliberately-namespaced test project (e.g. `External.Consumer.Tests`) that references `LogAssertions.TUnit` only via PackageReference and verifies every public entry point resolves without inheriting visibility from the `LogAssertions.TUnit.*` namespace tree. Would have caught the v0.2.0/v0.2.1 shorthand-resolution bug fixed in v0.2.2 before it shipped.
- **Package-shipped `<Using Include="LogAssertions" />` via `build/LogAssertions.props`** — alternative to the documented `GlobalUsings.cs` recommendation. Would auto-add `LogAssertions` as a global using to every consuming project on install (no consumer code change needed for `LogCollectorBuilder` / `LogFilter` to resolve). Trade-off: more invasive (silently adds a global to consumers), strict-using-policy teams might object. Consumer can opt-out via `<Using Remove="LogAssertions" />`. Defer until multiple consumers report the explicit-using as friction; the documented `GlobalUsings.cs` route is the lower-surprise default.

### Possible v0.4.0+ (separate packages, more substantial work)

- **Roslyn analyzer** for common mistakes: forgotten terminator, missing `StringComparison`, forgotten `await`. Standalone analyzer package.
- **Source generator** for `[LoggerMessage]`-derived typed assertion helpers — e.g. `HasLogged().RetryExhausted(maxRetries: 3)` generated from the `[LoggerMessage]` declaration.
- **Verify integration** — `collector.ToVerifyString()` for golden-file approval of full log sequences.
- **Framework adapter packages:** `LogAssertions.NUnit`, `LogAssertions.xUnit`, `LogAssertions.MSTest`. The `LogAssertions` core package already supports them architecturally; only built when someone asks.

### Could-go-either-way (no current plan, depends on demand)

- Multi-collector aggregate: `Assert.That(c1, c2, c3).HasLogged(...)` for pipeline tests with several loggers.
- Diagnostic upgrades: per-record match-tagging in failure dump, grouping by category/level.
- Scope-aware sequence: `HasLoggedSequence().InScope("RequestId", "abc")...`.
- Parallel-safe collector partitioning (depends on TUnit's parallel-test story).
- Benchmarks + perf documentation (will probably do once before v1.0 to honestly characterise).

### Probably not (wrong fit or no clear demand)

- `WithCallerInfo(...)` — MEL doesn't auto-propagate `[CallerMemberName]` etc. into log records.
- `WithContext<T>` AsyncLocal context filter — niche, conflates with `WithScope`.
- `WithStructuredState<T>` typed state — `FakeLogger` empirically does not preserve the typed state object (we proved this by testing).
- `WithFailureMessage` custom override — TUnit's own `Assert.That(...).WithMessage(...)` already covers this at the framework level.
- `Should()` syntax — orthogonal API style choice.
- JSON property matching (`HasLoggedJson`) — depends on JSON serializer, AOT-incompatible without source-gen, ecosystem-fragmenting.
- Anonymous-object scope inspection — would require reflection; intentionally out of scope for AOT-compatibility.
- Localization-aware level names — `LevelAbbreviation` is intentionally English-centric to match MEL's console formatter.

### Out of scope per project policy

- Multi-target `net8;net9;net10` — see "Single TFM, forward-only" in [Design notes](#design-notes).

If you'd find any of the candidate items useful, [open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml).

---

## Background

The TUnit feature request that motivated this package was [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627), declined on architectural grounds (no cross-package coupling between `TUnit.Logging.Microsoft` and `TUnit.Assertions`). The user-space pattern was unblocked when [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785) shipped `[AssertionExtension]` infrastructure in TUnit 1.41.0. This package implements the user-space pattern.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branch convention, PR checklist, and code style.

## License

[MIT](LICENSE) — Copyright (c) 2026 John Verheij
