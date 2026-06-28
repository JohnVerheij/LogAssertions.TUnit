# LogAssertions.TUnit

[![CI](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/JohnVerheij/LogAssertions.TUnit/badge)](https://scorecard.dev/viewer/?uri=github.com/JohnVerheij/LogAssertions.TUnit)
[![codecov](https://codecov.io/gh/JohnVerheij/LogAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/LogAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Docs](https://img.shields.io/badge/docs-logassertions.dev-512BD4.svg)](https://logassertions.dev)

A TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. Built using TUnit's `[AssertionExtension]` source generator, so the assertion entry points integrate directly into TUnit's `Assert.That(...)` pipeline with rich failure diagnostics.

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
  - [Value-returning terminators (`GetMatch` / `GetMatches`)](#value-returning-terminators-getmatch--getmatches)
- [Sequence assertions: `HasLoggedSequence`](#sequence-assertions--hasloggedsequence)
  - [Concurrent steps: `ThenAnyOrder` (v0.4.0+)](#concurrent-steps--thenanyorder-v040)
- [Combining assertions with `.And` / `.Or`](#combining-assertions-with-and--or)
- [Batch assertions: `AssertAllAsync` and `Assert.Multiple` interop](#batch-assertions--assertallasync-and-assertmultiple-interop)
- [TUnit-native conveniences (`Because`, parallelism, Should)](#tunit-native-conveniences-because-parallelism-should)
- [Non-asserting inspection](#non-asserting-inspection)
  - [Dump verbosity (v0.4.0+)](#dump-verbosity-v040)
- [Failure diagnostics](#failure-diagnostics)
- [Cookbook: common patterns](#cookbook--common-patterns)
- [Troubleshooting](#troubleshooting)
- [Design notes](#design-notes)
- [Stability intent (pre-1.0)](#stability-intent-pre-10)
- [Limitations and future work](#limitations-and-future-work)
- [Family compatibility](#family-compatibility)
- [Pair with](#pair-with)
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

**Requirements:** TUnit 1.57.0 or later, .NET 10. The package is AOT-compatible, trimmable, and uses no reflection in the assertion path.

## Package layout

This repo ships **two** NuGet packages:

| Package | Purpose | Depends on |
|---|---|---|
| [`LogAssertions`](https://www.nuget.org/packages/LogAssertions/) | Framework-agnostic core: `ILogRecordFilter` + `LogFilter` + rendering + collector inspection extensions | `Microsoft.Extensions.Diagnostics.Testing` |
| [`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/) | TUnit-specific entry points: `HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()` and shorthands | `LogAssertions` + `TUnit.Assertions` |

You install `LogAssertions.TUnit`; `LogAssertions` comes transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are *not* shipped today: they'd reuse the `LogAssertions` core. If you'd find one useful, [open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml).

## Namespaces (and a `GlobalUsings.cs` recommendation)

The two packages place types in two namespaces with deliberately-different scopes:

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| `HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()` (the source-generated entry points) | `TUnit.Assertions.Extensions` | **Yes**: TUnit auto-imports this namespace |
| `HasLoggedOnce()`, `HasLoggedExactly()`, ... (shorthand entry points, since 0.2.2) | `TUnit.Assertions.Extensions` | **Yes**: same auto-import path |
| `LogCollectorBuilder.Create(...)` (the `(factory, collector)` factory) | `LogAssertions` | **No**: needs `using LogAssertions;` |
| `LogFilter.AtLevel(...)`, `ILogRecordFilter`, `Filter`/`CountMatching`/`DumpTo` extensions | `LogAssertions` | **No**: needs `using LogAssertions;` |
| `AssertAllAsync(...)` batch terminator | `TUnit.Assertions.Extensions` | **Yes**: same auto-import path |

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

**Lifetime / disposal:** the `IDisposable` returned from `LogCollectorBuilder.Create()` (the `factory`) owns the underlying `FakeLoggerProvider`. Disposing it stops new log records from being captured but the records already gathered into the `collector` snapshot remain valid: you can continue to query the `collector` after the `using` block exits. Both block-form (`using (factory) { ... }`) and declaration-form (`using ILoggerFactory factory = ...`) work; pick whichever fits your test layout.

---

## Migrating from manual assertions

If you already use `FakeLogCollector` directly, the typical "before" pattern is to pull a snapshot, filter with LINQ, and write multiple assertions against the result:

```csharp
// Before: manual:
var records = collector.GetSnapshot();
var warnings = records.Where(r => r.Level == LogLevel.Warning).ToList();
await Assert.That(warnings).HasCount().EqualTo(1);
await Assert.That(warnings[0].Message).Contains("timeout", StringComparison.Ordinal);
```

The "after" with `LogAssertions.TUnit` is one fluent chain that reads as a single intent and produces a failure message with every captured record (including structured state and scope content) when the chain doesn't match:

```csharp
// After: LogAssertions.TUnit:
await Assert.That(collector)
    .HasLogged()
    .AtLevel(LogLevel.Warning)
    .Containing("timeout", StringComparison.Ordinal)
    .Once();
```

Two practical wins on top of the readability:

- **Failure diagnostics.** The "before" snippet's failure says *"expected 1, got 3"* (or *"expected to contain 'timeout'"*). The "after" snippet's failure renders the full captured-records snapshot: level, category, message, structured state, scope: so you can see *which* three records came through and why none matched, without adding `Console.WriteLine` calls.
- **Composability.** The same chain extends to scopes (`WithScopeProperty("RequestId", id)`), structured properties (`WithProperty("UserId", 42)`), exception types (`WithException<TimeoutException>()`), and combinator nodes (`MatchingAny(...)`, `Not(...)`) without restructuring the test.

See the [Cookbook](#cookbook--common-patterns) for the patterns this replaces in practice.

### Prefer structured matchers over substring matching

`.Containing("...")` couples a test to the *rendered* message text, so it silently rots when a `[LoggerMessage]` template is reworded. When the assertion is really about a structured field, an attached exception, or a scope value, the dedicated matcher is both more robust and more precise; reach for the substring form only when the rendered text itself is what the test is about.

| Instead of (substring) | Prefer (structured) |
|---|---|
| `.Containing("OrderId=42")` | `.WithProperty("OrderId", 42)` |
| `.Containing("failed")` on an `Error` line | `.WithException<TException>()` (or `.WithExceptionMessage(...)`) |
| `.Containing(requestId.ToString())` | `.WithScopeProperty("RequestId", requestId)` |
| `.Containing("retries=")` just to prove a key is present | `.WithScopeProperty("retries")` *(v0.7.0+)* |
| `.Containing("user created")` matching the template text | `.WithMessageTemplate("user created")` (value-independent) |
| multi-step "warn then debug then reconnected" via several asserts | `.HasLoggedSequence().Then(...).Then(...)` |

`.WithMessageTemplate` matches the original `[LoggerMessage]` template rather than the rendered values, so it survives a value change while still pinning intent. The structured matchers also keep the failure diagnostics: a `.WithProperty` miss lists the structured state actually captured, not just "substring not found".

---

## Entry points

Three core entry points are emitted by TUnit's source generator and surface as extension methods on `Assert.That(FakeLogCollector)`.

| Entry point | Default expectation | Terminators allowed |
|---|---|---|
| `HasLogged()` | At least 1 matching record | All count terminators (see below) |
| `HasNotLogged()` | Zero matching records | None: fixed at zero |
| `HasLoggedSequence()` | An ordered series of matches; `Then()` separates steps | None: each step's match is implicit |

All three accept the full filter chain. `HasLogged()` is the workhorse; `HasNotLogged()` is its inverse with cleaner failure semantics; `HasLoggedSequence()` is for multi-step traces (e.g. *"Started -> Validation failed -> Stopped"*).

### Shorthand entry points

Wrappers that pre-configure the most common chains. Each returns the underlying assertion type so additional filters can still be appended.

| Shorthand | Equivalent to |
|---|---|
| `HasLoggedOnce()` | `HasLogged().Once()` |
| `HasLoggedExactly(int)` | `HasLogged().Exactly(int)` |
| `HasLoggedAtLeast(int)` | `HasLogged().AtLeast(int)` |
| `HasLoggedAtMost(int)` | `HasLogged().AtMost(int)` |
| `HasLoggedBetween(int, int)` | `HasLogged().Between(int, int)` |
| `HasLoggedNothing()` | `HasNotLogged()` (no filters: asserts the collector is empty) |
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

| Filter | Behavior |
|---|---|
| `AtLevel(LogLevel)` | Exact level match |
| `AtLevelOrAbove(LogLevel)` | `record.Level >= threshold` (e.g. *"any warning or worse"*) |
| `AtLevelOrBelow(LogLevel)` | `record.Level <= threshold` (e.g. *"only diagnostic-tier"*) |
| `AtAnyLevel(params LogLevel[])` | Match any level in the supplied set (e.g. *"Warning or Error but not Critical"*) |
| `NotAtLevel(LogLevel)` | Inverse of `AtLevel`: convenience over `Not(LogFilter.AtLevel(...))` |
| `ExcludingLevel(LogLevel)` | Alias for `NotAtLevel`, reads better in negative-filter chains |

```csharp
await Assert.That(collector).HasLogged().AtLevelOrAbove(LogLevel.Warning).AtLeast(1);
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
await Assert.That(collector).HasLogged().AtAnyLevel(LogLevel.Warning, LogLevel.Error).AtLeast(1);
```

### Message filters

| Filter | Behavior |
|---|---|
| `Containing(string substring, StringComparison comparison)` | Formatted message contains substring (comparison **explicit by design**: no implicit culture) |
| `ContainingAll(StringComparison, params string[])` | Formatted message contains every one of the substrings |
| `ContainingAny(StringComparison, params string[])` | Formatted message contains at least one of the substrings |
| `Matching(Regex)` | Formatted message matches the regex |
| `WithMessage(Func<string, bool> predicate)` | Predicate over the formatted message |
| `WithMessageTemplate(string template)` | The pre-substitution template (e.g. `"Order {OrderId} processed"`) equals `template` exactly. Resolved from MEL's magic `{OriginalFormat}` structured-state entry |
| `NotContaining(string, StringComparison)` | Inverse of `Containing`: convenience over `Not(LogFilter.Containing(...))` |

`WithMessageTemplate` is useful when you want to pin a specific call site without coupling to the substituted parameter values:

```csharp
// matches every "Order N processed" log regardless of N
await Assert.That(collector).HasLogged()
    .WithMessageTemplate("Order {OrderId} processed").AtLeast(1);
```

### Exception filters

| Filter | Behavior |
|---|---|
| `WithException<TException>()` | `record.Exception is TException` (assignable) |
| `WithException()` | Any record with a non-null `Exception`, regardless of type |
| `WithoutException()` *(v0.6.0+)* | `record.Exception is null`. The complement of `WithException()`: asserts the deliberate absence of an exception (for example a record logged at warning or error level with no exception attached). |
| `WithException(Func<Exception, bool> predicate)` | Predicate over the exception (predicate not invoked for null exception) |
| `WithExceptionMessage(string substring, StringComparison comparison)` *(explicit-comparison overload added in v0.4.0)* | `record.Exception?.Message` contains substring under the supplied comparison; records without an exception never match. The legacy single-arg `(string substring)` overload was `[Obsolete]` from v0.4.0 and was removed in v0.6.0; pass the comparison explicitly. |
| `WithInnerException<TInner>()` *(v0.4.0+)* | `record.Exception?.InnerException is TInner` (assignable). Walks one level only; deeper nesting is not searched. |
| `WithInnerExceptionMessage(string substring, StringComparison comparison)` *(v0.4.0+)* | `record.Exception?.InnerException?.Message` contains substring under the supplied comparison; records without an inner exception never match. |

```csharp
await Assert.That(collector).HasLogged()
    .WithException<TimeoutException>()
    .WithExceptionMessage("connection", StringComparison.Ordinal)
    .Once();

// gRPC / RPC pattern: transport exception (RpcException) wraps the underlying domain
// exception. Combine the outer-type filter with the inner-type filter on the same chain:
await Assert.That(collector).HasLogged()
    .WithException<RpcException>()
    .WithInnerException<TimeoutException>()
    .WithInnerExceptionMessage("upstream", StringComparison.Ordinal)
    .Once();
```

The `WithInnerException` filters walk only `Exception.InnerException` (one level). Deeper chains (`InnerException.InnerException...`) and `AggregateException.InnerExceptions` are not flattened: most logged exception graphs in MEL flows are one level deep, and one-level matching keeps the predicate fast and predictable. If you need deeper inspection, use `WithException(predicate)` and walk the chain yourself.

### Structured-state (property) filters

`Microsoft.Extensions.Logging` exposes structured properties on each record (the parameters captured by `LoggerMessage` source generators or by message-template logging calls).

| Filter | Behavior |
|---|---|
| `WithProperty(string key, string? value)` | Property's formatted string value equals `value` (ordinal) |
| `WithProperty(string key, Func<string?, bool> predicate)` | Predicate over the formatted string value (use for ranges, regex, or null-checks) |
| `WithProperty<T>(string key, T value)` *(v0.6.0+; `where T : IParsable<T>`)* | Parses the stored string back to `T` (invariant culture) and compares via `EqualityComparer<T>.Default`. An absent or non-parsable value never matches. |
| `WithProperty<T>(string key, Func<T, bool> predicate)` *(v0.6.0+; `where T : IParsable<T>`)* | Parses the stored string back to `T` (invariant culture) and applies the predicate to the typed value. |

Note: `FakeLogRecord` exposes structured-state values as **strings** (the formatted form), so the `string?` predicate overload receives a `string?`. For a known parsable type, prefer the typed `WithProperty<T>` overloads (added in v0.6.0), which parse the stored string back to `T` for you using the invariant culture:

```csharp
// Typed (v0.6.0+): the stored "1001" is parsed back to int before the predicate runs.
await Assert.That(collector).HasLogged()
    .WithProperty<int>("OrderId", n => n > 1000)
    .AtLeast(1);

// String form (still available) when you want raw-string control:
await Assert.That(collector).HasLogged()
    .WithProperty("OrderId", v =>
        int.TryParse(v, CultureInfo.InvariantCulture, out var n) && n > 1000)
    .AtLeast(1);
```

### Scope filters

Scopes are values pushed via `logger.BeginScope(...)`. They surround any log records emitted while the scope is active.

| Filter | Behavior |
|---|---|
| `WithScope<TScope>()` | A scope of type `TScope` was active when the record was emitted |
| `WithScopeProperty(string key, object? value)` | A scope contains a property `key` matching `value` (`object.Equals` semantics) |
| `WithScopeProperty(string key, Func<object?, bool> predicate)` | A scope contains a property `key` whose value satisfies the predicate |
| `WithScopeProperty(string key)` *(v0.7.0+)* | A scope contains a property `key`, regardless of its value (a `null` value still counts as present). For scope values set internally and unknown to the test, such as caller-info scopes. Pairs with `HasNotLogged().WithScopeProperty(key)`. |
| `WithScopeProperty<T>(string key, T value)` *(v0.6.0+)* | Typed match: the scope value is a `T` equal to `value` (compared via `EqualityComparer<T>.Default`). A scope value of a different runtime type never matches. |
| `WithScopeProperty<T>(string key, Func<T, bool> predicate)` *(v0.6.0+)* | Typed predicate: the scope value is a `T` satisfying the predicate. |
| `WithScopeProperties(IDictionary<string, object?> required)` *(v0.4.0+)* | Subset match across all active scopes: every key/value pair in `required` must match in some scope, but different pairs may match in different scopes. Empty dictionary matches every record (vacuous truth). |

Chaining `WithScopeProperty` calls matches each key independently across all active scopes: `.WithScopeProperty("a", 1).WithScopeProperty("b", 2)` accepts a record where `a=1` is in some scope **and** `b=2` is in some scope, not necessarily the same one. To require both on a single scope, push them in one `BeginScope(dictionary)`; to require a set of pairs as a subset across scopes, use `WithScopeProperties(dictionary)`.

Scope values keep their runtime type (unlike structured-state values, which are stored as strings), so the typed `WithScopeProperty<T>` overloads (added in v0.6.0) compare typed-to-typed without boxing boilerplate. They read cleanly when the scope value is a known type:

```csharp
// MessageId is a Guid in the scope; CallerLine is an int.
await Assert.That(collector).HasLogged().WithScopeProperty("MessageId", messageId).AtLeast(1);
await Assert.That(collector).HasLogged().WithScopeProperty<int>("CallerLine", line => line > 0).Once();
```

Scope-property filters recognize the two AOT-friendly idioms:

```csharp
// dictionary scope: the canonical structured pattern
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

> **Anonymous-object scopes** (`logger.BeginScope(new { OrderId = 42 })`) are **not** recognized by `WithScopeProperty`: reading their fields requires reflection, which would compromise AOT-compatibility. Prefer dictionary or `LoggerMessage.DefineScope` form.

#### Subset match across multiple scopes: `WithScopeProperties` (v0.4.0+)

When a record is emitted under several nested scopes (e.g. an outer `RequestId` scope plus an inner `OrderId` scope), `WithScopeProperty` matches one key at a time and lives across the whole active-scope set. `WithScopeProperties` extends this to a *batch* match: pass a dictionary of required key/value pairs, and each pair must match in some scope (different pairs may match in different scopes: they don't have to share one scope).

```csharp
using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("RequestId", "abc-123") }))
using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("OrderId", 42) }))
{
    DoWork();
}

var required = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["RequestId"] = "abc-123",
    ["OrderId"] = 42,
};
await Assert.That(collector).HasLogged().WithScopeProperties(required).AtLeast(1);
```

The dictionary is snapshotted on construction; mutating the input dictionary after the filter is created does not affect subsequent matches. Comparison is via `object.Equals` for values; keys are ordinal.

### Identity filters (category, event)

| Filter | Behavior |
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

| Filter | Behavior |
|---|---|
| `Where(Func<FakeLogRecord, bool> predicate)` | Arbitrary predicate over the full `FakeLogRecord` |

Use only when no other filter expresses the constraint cleanly: composing built-in filters is preferred for diagnostic clarity in failure messages.

### Combinator chain methods (`MatchingAny`, `MatchingAll`, `Not`, `WithFilter`)

The fluent chain is implicitly AND-combined. These four chain methods let you compose richer expressions inside the chain without dropping to `Where`:

| Method | Behavior |
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

Terminators express the count expectation. Pick exactly one: chain it after all filters. `HasNotLogged` has no terminators (the expectation is fixed at zero matches).

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

**`Never()` vs `HasNotLogged()`: when to use which.** They produce identical assertions; the only difference is reading order. **Prefer `HasNotLogged()`** when "this should not happen" is the primary intent of the test (the negative is the headline). **Use `.Never()`** when you've already started building a positive filter chain and only at the end realize you expect zero matches: saves rewriting the prefix. Don't agonize over the choice; either reads clearly to a future maintainer.

### Value-returning terminators (`GetMatch` / `GetMatches`)

Two terminators on `HasLogged()` return the matched record(s) once the assertion passes, so a follow-up TUnit assertion can inspect them without a duplicate `collector.Filter(...)` call.

| Terminator | Returns | Required preceding terminator |
|---|---|---|
| `.GetMatch()` | `Task<FakeLogRecord>` | Any terminator that constrains the count to exactly one (typically `Once()` or `Exactly(1)`; `Between(1, 1)` also accepted) |
| `.GetMatches()` | `Task<IReadOnlyList<FakeLogRecord>>` | Any count terminator |

```csharp
// Get the single matched record and assert further on it:
FakeLogRecord match = await Assert.That(collector)
    .HasLogged()
    .AtLevel(LogLevel.Error)
    .WithException<TimeoutException>()
    .Once()
    .GetMatch();

await Assert.That(match.Exception!.Message).Contains("connection pool exhausted");

// Or get all matches (here: every retry) and assert on the collection:
IReadOnlyList<FakeLogRecord> retries = await Assert.That(collector)
    .HasLogged()
    .WithMessageTemplate("Retrying after {Delay}ms")
    .AtLeast(1)
    .GetMatches();

await Assert.That(retries).Count().IsEqualTo(3);
```

`.GetMatch()` throws `InvalidOperationException` upfront if the chain's count expectation doesn't constrain the match count to exactly one: fail-fast on a nonsensical "give me the single match" expectation against a chain that allows N matches.

The returned list from `.GetMatches()` is a defensive snapshot captured at evaluation time; it isn't bound to the live collector.

---

## Sequence assertions: `HasLoggedSequence`

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

- The walk is **order-preserving but not contiguous**: records between matches are skipped.
- `Then()` commits the current step's filters and starts a new step.
- Each step's filters AND-combine, exactly like the single-match assertions.
- A step with no filters always matches the next available record (use sparingly).
- Failure diagnostics indicate which step failed and dump the full captured-records list (see [Failure diagnostics](#failure-diagnostics)).

### Concurrent steps: `ThenAnyOrder` (v0.4.0+)

`Then()` requires the next step's match to come *after* the previous step's match in the captured-records timeline. For workflows where several events must all occur in some window but the relative order between them isn't fixed (parallel work on a request, fan-out logging from multiple workers, completion notifications that race), use `ThenAnyOrder(...)` to commit a **concurrent group** of sub-steps. All sub-steps must match somewhere in the remaining records, but the order among them is unconstrained.

```csharp
await Assert.That(collector).HasLoggedSequence()
    .AtLevel(LogLevel.Information).Containing("Request received", StringComparison.Ordinal)
    .ThenAnyOrder(
        s => s.AtLevel(LogLevel.Information).Containing("Auth check passed", StringComparison.Ordinal),
        s => s.AtLevel(LogLevel.Information).Containing("Quota check passed", StringComparison.Ordinal),
        s => s.AtLevel(LogLevel.Information).Containing("Cache lookup completed", StringComparison.Ordinal))
    .Then()
    .AtLevel(LogLevel.Information).Containing("Response sent", StringComparison.Ordinal);
```

Semantics:

- Each sub-step is configured by an `Action<HasLoggedSequenceAssertion>`: call the same filter methods (`AtLevel`, `Containing`, `WithException<T>`, etc.) on the supplied assertion to build that sub-step's filter chain.
- All sub-steps must match in the remaining records before the group is satisfied. Records that match no sub-step are skipped.
- **Backtracking matcher:** sub-steps are matched against records via backtracking, so any order-independent valid assignment is found if one exists. Overlapping filters compose safely: a broad sub-step that matches many records will not starve a more specific sub-step that needs a particular record. Sub-step declaration order does not affect whether the group succeeds (it still affects the order filter descriptions appear in failure messages).
- **Sub-step configurators add filters only.** Calling `Then()` or recursive `ThenAnyOrder(...)` from within a sub-step configurator throws `InvalidOperationException`: outer-sequence structure must be expressed at the top level after `ThenAnyOrder(...)` returns.
- The chain re-enters strictly-ordered mode after `ThenAnyOrder(...)`: call `.Then()` to add a strictly-ordered next step, or end the chain.
- Failure diagnostics list every sub-step's filter description, so a missing match is easy to attribute.

> **Use `ThenAnyOrder` sparingly.** Most production logging IS strictly ordered (a single thread writes the records). Reach for `ThenAnyOrder` only when the workflow genuinely involves parallel work and the test would otherwise be brittle to reordering.

---

## Combining assertions with `.And` / `.Or`

Because the assertion types derive from TUnit's `Assertion<T>`, the standard TUnit chaining works. **`.And` is genuinely useful for log assertions**: chain a positive and a negative invariant in one expression:

```csharp
await Assert.That(collector)
    .HasLogged().AtLevel(LogLevel.Information).AtLeast(1)
    .And.HasNotLogged().AtLevel(LogLevel.Error);
```

For three-or-more conditions, prefer the dedicated [`AssertAllAsync`](#batch-assertions--assertallasync-and-assertmultiple-interop) batch terminator: it aggregates failures into a single message rather than failing fast on the first.

**`.Or` is rarely useful for log assertions.** "Either no errors were logged OR a specific recovery was logged" is a contrived shape; in practice tests want both, not either. The mechanism is available via TUnit if you need it, but the cookbook below shows no examples because the use case is genuinely uncommon. If you find yourself reaching for `.Or`, consider whether `MatchingAny(...)` (an OR of *filters*, not whole assertions) expresses the intent more clearly.

---

## Batch assertions: `AssertAllAsync` and `Assert.Multiple` interop

Two ways to run several invariants in one pass and see every violation, not just the first.

**`AssertAllAsync`: the log-specific batch terminator.** Aggregates failures from log-shape assertions against the same collector:

```csharp
await Assert.That(collector).AssertAllAsync(
    c => c.HasLogged().AtLevel(LogLevel.Information).AtLeast(1),
    c => c.HasNotLogged().AtLevel(LogLevel.Error),
    c => c.HasLoggedSequence()
        .Containing("Started", StringComparison.Ordinal)
        .Then().Containing("Stopped", StringComparison.Ordinal));
```

If two of three fail, the thrown exception's message lists both: not just the first.

A second overload (added in 0.2.1) accepts the more verbose `async c => await c.HasLogged()...` form for cases where the lambda needs to mix in non-assertion async work between checks. Pick whichever is clearer for the case at hand; both have identical failure-aggregation semantics.

**`Assert.Multiple`: TUnit's general-purpose batch.** Works cleanly with our chains too. Useful when a test mixes log assertions with non-log assertions (e.g. assert on the return value of the system-under-test AND on what it logged):

```csharp
using (Assert.Multiple())
{
    await Assert.That(result.Status).IsEqualTo(OperationStatus.Succeeded);
    await Assert.That(collector).HasLogged().AtLevel(LogLevel.Information).AtLeast(1);
    await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
}
```

All three failures (if all three are wrong) appear in the aggregated exception. **When to prefer which:** `AssertAllAsync` reads more naturally when every check is against the same collector (no `Assert.That(collector)` repeated); `Assert.Multiple` shines when mixing log and non-log assertions, or when the lambda form of `AssertAllAsync` would force a separate-statement layout anyway.

---

## TUnit-native conveniences (`Because`, parallelism, Should)

Because LogAssertions.TUnit's chains derive from TUnit's `Assertion<T>`, several TUnit core features just work without any LogAssertions-specific wiring. The patterns most worth knowing:

**`.Because("reason")`: annotate why the assertion matters.** Chains cleanly on any of our terminators and surfaces the justification in the failure message. Useful when the failure-message snapshot alone doesn't make the *intent* of the assertion obvious to whoever's reading the CI log:

```csharp
await Assert.That(collector)
    .HasNotLogged()
    .AtLevelOrAbove(LogLevel.Error)
    .Because("happy-path workflow must not log errors");
```

A failure renders both the captured-records snapshot AND the reason, so the next person to triage sees *what* happened and *why* it shouldn't have.

**`[NotInParallel]` and the `FakeLogCollector` lifetime model.** `FakeLogCollector` is **instance-scoped**: each test gets its own via `LogCollectorBuilder.Create()`: so tests using LogAssertions.TUnit DO NOT need `[NotInParallel]` for the collector itself. They run safely in parallel.

`[NotInParallel]` becomes necessary only when the test interacts with **global** logging state (e.g. NLog's `LogManager.Configuration`, Serilog's static `Log.Logger`, or any singleton logging configuration). In that case, give every test that touches the same global a single shared `[NotInParallel("global-logging-config")]` key: different keys do NOT serialize against each other, which is a subtle source of latent races.

**`Should()` style: currently deferred.** Upstream TUnit ships a separate `TUnit.Assertions.Should` package that adds `value.Should().BeEqualTo(...)` style on top of `TUnit.Assertions`. Hands-on verification against the LogAssertions.TUnit chain is pending the upstream package reaching stable; the rest of the chain works today via the canonical `Assert.That(collector).HasLogged(...)` form.

---

## Non-asserting inspection

Sometimes a test wants to inspect what was logged without asserting: for further calculations, debugging output, or cross-checking. The core package adds three extensions on `FakeLogCollector`; the TUnit adapter package adds one more bound to TUnit's per-test output writer.

| Method | Package | Returns |
|---|---|---|
| `Filter(params ILogRecordFilter[] filters)` | `LogAssertions` | The matching records as a defensive `IReadOnlyList<FakeLogRecord>` |
| `CountMatching(params ILogRecordFilter[] filters)` | `LogAssertions` | Just the match count (no list materialisation) |
| `DumpTo(TextWriter writer)` | `LogAssertions` | Writes every captured record in the failure-message format (Default verbosity) |
| `DumpTo(TextWriter writer, DumpVerbosity verbosity)` *(v0.4.0+)* | `LogAssertions` | Verbosity-controlled dump. See [Dump verbosity](#dump-verbosity-v040). |
| `DumpToTestOutput()` | `LogAssertions.TUnit` | Same as `DumpTo`, targeting `TestContext.Current.Output.StandardOutput` |
| `DumpToTestOutput(DumpVerbosity verbosity)` *(v0.4.0+)* | `LogAssertions.TUnit` | Verbosity-controlled variant. |

```csharp
// Inspect without asserting
var warnings = collector.Filter(LogFilter.AtLevel(LogLevel.Warning));
int errors = collector.CountMatching(
    LogFilter.AtLevelOrAbove(LogLevel.Error),
    LogFilter.WithException<DbException>());

// Print the entire snapshot to test output during development: TUnit-aware shorthand
// for the DumpTo(TextWriter) pattern. Records appear inline in the test report.
collector.DumpToTestOutput();

// Verbosity-controlled variants (v0.4.0+)
collector.DumpToTestOutput(DumpVerbosity.Compact);  // headlines only
collector.DumpToTestOutput(DumpVerbosity.Verbose);  // includes full exception ToString()
```

`DumpToTestOutput()` throws `InvalidOperationException` if called outside a TUnit test context (no `TestContext.Current`). For non-TUnit contexts or when you need the rendered text as a string, use the framework-agnostic `DumpTo(TextWriter)` overload from the core `LogAssertions` package.

### Dump verbosity (v0.4.0+)

The `DumpVerbosity` enum controls how much detail each record renders:

| Verbosity | Per-record output |
|---|---|
| `DumpVerbosity.Compact` | One line per record: `[lvl] category: message`. No properties, scopes, or exception details. Use when the captured-records list is only needed as an at-a-glance sanity check. |
| `DumpVerbosity.Default` | The standard rendering used by failure messages: headline plus indented summary lines for structured state, scopes, and a one-line exception summary. The no-arg overloads of `DumpTo` / `DumpToTestOutput` use this. |
| `DumpVerbosity.Verbose` | Default rendering plus the full exception `ToString()` (stack trace + inner-exception chain). Use when an exception's stack is the diagnostic signal. |

```csharp
// Compact during early development: see what was logged at all
collector.DumpToTestOutput(DumpVerbosity.Compact);

// Default: mirrors the failure-message format
collector.DumpToTestOutput();

// Verbose: full stack trace per logged exception
collector.DumpToTestOutput(DumpVerbosity.Verbose);
```

The exact text produced by each level is documented as **not stable** (consistent with the rest of the rendering surface); pin broad markers like `"[warn]"` rather than exact whitespace or punctuation.

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

This eliminates the historical pattern of adding temporary `Console.WriteLine` calls to debug failing log assertions: every dimension you can filter on is also rendered in the failure message.

---

## Cookbook: common patterns

### Assert no errors were logged

```csharp
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
```

### Wait for a log produced by background work

Background- or pump-driven logs (a `BackgroundService`, a timer, a fire-and-forget handler) can land after the call that triggered them returns, so a synchronous `HasLogged()` races the producer and flakes. Wrap the assertion in TUnit's own `Eventually` (or `WaitsFor`) to poll the live collector until the record appears or the timeout elapses, instead of hand-rolling a wait:

```csharp
await Assert.That(collector).Eventually(
    c => c.HasLogged().Containing("dead-lettered", StringComparison.OrdinalIgnoreCase).AtLeast(1),
    TimeSpan.FromSeconds(5));
```

`Eventually` re-runs the inner assertion against the live collector each interval and re-reads its records, so any filter and terminator chain works inside it - `HasNotLogged()`, sequences, counts, and the structured matchers all compose. The poll interval and a `CancellationToken` are optional parameters on `Eventually`/`WaitsFor`.

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

### Assert a startup -> work -> shutdown sequence

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

`Create(minimumLevel)` sets a capture floor. A `HasNotLogged()` whose level filters target only levels below that floor is rejected as vacuous (it would otherwise pass for the wrong reason, since nothing at that level was ever captured): the failure says so and names the floor. Assert at a captured level, or lower the floor.

### Mirror logs into the test report (`CreateTeed`)

`LogCollectorBuilder.Create()` captures into the collector but writes nothing to the test's output, so a passing unit test shows an empty log panel in the TUnit HTML report. `TestOutputLogCollectorBuilder.CreateTeed()` (in `LogAssertions.TUnit`) adds a provider that mirrors each record to `TestContext.Current.Output` as it is logged, while still capturing for assertions:

```csharp
var (factory, collector) = TestOutputLogCollectorBuilder.CreateTeed();
using (factory)
{
    var logger = factory.CreateLogger("MyService");
    new MyService(logger).DoWork();
    await Assert.That(collector).HasLoggedOnce().Containing("done", StringComparison.Ordinal);
    // The "done" line also appears inline in this test's report output.
}
```

Records logged on a background thread are teed too: the owning test's output writer is captured when `CreateTeed()` runs (on the test's own thread), so off-context records reach the report rather than being dropped (v0.10.0+). Keep the plain `Create()` for log-heavy soak tests where buffering every record in the per-test output is undesirable.

#### Wiring into a builder you already own

When the capture must live inside a logging builder you already configure (a host's `ConfigureLogging`, or any `LoggerFactory.Create` callback) rather than a self-contained factory, use `AddTeedFakeLogging` / `AddFakeLogging` (in `LogAssertions.TUnit`):

These extensions (like `CreateTeed`) live in the `LogAssertions.TUnit` namespace, which the recommended [`GlobalUsings.cs`](#namespaces-and-a-globalusingscs-recommendation) does not include, so add `using LogAssertions.TUnit;` (or a matching `global using`) in files that call them.

```csharp
var collector = new FakeLogCollector();
using var factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector, LogLevel.Trace));
```

`AddTeedFakeLogging` takes an optional tee `ILoggerProvider`. Configured on the test's own thread the built-in tee binds to that test's output writer, so background-thread records are teed (v0.10.0+). For a logging host shared across many tests - where no single owning test can be captured at configuration time - pass your own provider to correlate each record back to its test, without this package taking a dependency on it:

```csharp
// CorrelatedTUnitLoggerProvider ships in TUnit.Logging.Microsoft, referenced by your test project.
b.AddTeedFakeLogging(collector, LogLevel.Trace, new CorrelatedTUnitLoggerProvider(LogLevel.Trace));
```

`AddFakeLogging` is the capture-only variant (no tee). Both set the builder's minimum level and register the capture floor, so the vacuous-`HasNotLogged()` guard applies the same as with `CreateTeed`.

### Don't use `NullLogger` for code under coverage

A service guarded by `if (logger.IsEnabled(LogLevel.Debug)) { ... }` needs an **enabled** logger under test, or the guard only ever takes its false branch and that branch's coverage silently drops. `NullLogger` and a provider-less factory both report `IsEnabled == false`. Use a real (enabled) logger. If a particular test does not assert on logs and you only need the guard to evaluate true, an enabled non-capturing provider avoids the unbounded growth of a long-lived `FakeLogCollector`:

```csharp
// An enabled, discarding provider: IsEnabled-guarded log statements run, nothing is retained.
internal sealed class DiscardingLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => DiscardingLogger.Instance;
    public void Dispose() { }

    private sealed class DiscardingLogger : ILogger
    {
        public static readonly DiscardingLogger Instance = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? x, Func<TState, Exception?, string> f) { }
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
```

The `FakeLogCollector` itself grows without bound as records accumulate. Scope it per test (the `Create()` / `CreateTeed()` helpers return a fresh one each call); never hold one in a `static readonly` field shared across a reused host.

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

### Pin the full log sequence as a snapshot

The `HasLogged().Containing(...)` chain pins individual records. To pin the *entire emitted sequence*, so a regression that reorders, drops, or inserts records is caught even when the per-record predicates still pass, render the collector to deterministic text and snapshot it:

```csharp
using LogAssertions.Render;

var rendered = LogSnapshotRenderer.Render(collector);
await Assert.That(rendered).MatchesSnapshot();
```

`LogSnapshotRenderer` lives in the framework-agnostic `LogAssertions` core; `MatchesSnapshot()` is from the sibling [`SnapshotAssertions.TUnit`](https://github.com/JohnVerheij/SnapshotAssertions.TUnit) package. The two-line composition is deliberate: the renderer never takes a hard dependency on a snapshot framework.

The rendered text is verbatim and deterministic: LF line endings (a baseline committed on one OS stays valid on every other), capture order preserved, no baked-in scrubbing. Volatile values (GUIDs, timestamps, durations in message bodies) are the snapshot layer's concern: compose `SnapshotAssertions.Scrubbers` at the `MatchesSnapshot()` call site.

`LogSnapshotOptions` controls the rendering: level abbreviation vs full name, leaf vs full category, scope inclusion, and exception detail:

```csharp
var options = LogSnapshotOptions.Default with
{
    LevelStyle = LogLevelStyle.Full,
    ExceptionStyle = ExceptionStyle.StackTracePlaceholder,
};
await Assert.That(LogSnapshotRenderer.Render(collector, options)).MatchesSnapshot();
```

---

## Troubleshooting

Three issues that surface during early adoption. If you hit something not listed here, please open an issue on the GitHub repo: the FAQ is built from real adoption signal, not from speculation.

### "Shorthands like `HasLoggedOnce()` don't resolve in my test file"

**Symptom:** `error CS1061: 'IAssertionSource<FakeLogCollector>' does not contain a definition for 'HasLoggedOnce'`.

**Cause:** you're on LogAssertions.TUnit 0.2.0 or 0.2.1, where the shorthand entry points lived in the `LogAssertions.TUnit` namespace and required an explicit `using LogAssertions.TUnit;` to discover.

**Fix:** upgrade to **0.2.2 or later**. From 0.2.2 the shorthands live in `TUnit.Assertions.Extensions` and auto-import alongside `Assert.That()`: no extra `using` needed. The repo's external-consumer smoke-test project (added in 0.3.0) now guards against this regression at build time, so the bug class can't ship again.

### "`LogCollectorBuilder` not found" or "The name `LogFilter` does not exist in the current context"

**Symptom:** `error CS0103: The name 'LogCollectorBuilder' does not exist in the current context` (or the same for `LogFilter`, `ILogRecordFilter`, `FakeLogCollectorInspectionExtensions`).

**Cause:** these types live in the framework-agnostic `LogAssertions` namespace (the core package). Unlike the assertion entry points, they do NOT auto-import: they need an explicit `using LogAssertions;`.

**Fix:** add `using LogAssertions;` at the top of the file, or: if many test files use them: add `global using LogAssertions;` to a `GlobalUsings.cs` per the [Namespaces](#namespaces-and-a-globalusingscs-recommendation) section. The recommended GlobalUsings snippet there covers every namespace the test project will need.

### "`IDE0005: Using directive is unnecessary` on `using LogAssertions;` in some test files"

**Symptom:** strict-analysis projects (TreatWarningsAsErrors=true, IDE0005 promoted to error) fail to build because some test files don't reach for `LogCollectorBuilder` / `LogFilter`, only for the assertion entry points (which auto-import).

**Cause:** the `using LogAssertions;` is required in test files that reach for `LogCollectorBuilder` / `LogFilter`, but redundant in files that only call `await Assert.That(collector).HasLogged()...`. IDE0005 flags the redundant uses per file.

**Fix:** move `using LogAssertions;` (and the other recommended imports) into a single `GlobalUsings.cs` per the [Namespaces](#namespaces-and-a-globalusingscs-recommendation) section. Globals are exempt from IDE0005's per-file unused-import check: one declaration covers every file in the project, no analyzer noise.

---

## Design notes

- **Built on `[AssertionExtension]`** ([thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785)): the entry-point methods are emitted by TUnit's source generator. No extension-method wrappers needed.
- **No cross-package coupling.** This package depends on `TUnit.Assertions` and `Microsoft.Extensions.Diagnostics.Testing`. Neither of those depends on the other; this library is the bridge.
- **AOT-compatible / trimmable.** `IsAotCompatible=true`, `IsTrimmable=true`, `EnableTrimAnalyzer=true`. No reflection in the assertion path. Scope-property matching uses interface casts only, never reflection.
- **TFM: current LTS.** Targets `net10.0` today, multi-targeting the current STS while one exists (never long historical tails). A test project may target a higher TFM than the production code it references. See `CONVENTIONS.md` for the schedule.
- **Explicit `StringComparison`.** Every string-matching API requires the caller to pass a `StringComparison` (or uses `Ordinal` internally where unambiguous). No silent culture defaults.
- **Source Link + deterministic builds.** Both packages ship with [`Microsoft.SourceLink.GitHub`](https://github.com/dotnet/sourcelink), a separate `.snupkg` symbol package, and embedded sources (`EmbedUntrackedSources`). When a debugger steps into the assertion code, the source is fetched directly from this GitHub repo at the exact commit the package was built from: useful when you're investigating why a filter didn't match the record you expected. Builds are deterministic by default (the SDK's `<Deterministic>true</Deterministic>`).

---

## Stability intent (pre-1.0)

Per [SemVer](https://semver.org/), the `0.x` series is initial development: anything *may* change in any minor version, and there is no formal contract yet. The intent below documents what we *try* to keep stable so consumers can plan. A `1.0` release will turn this from intent into contract.

**Intended-stable (we will not break these without a CHANGELOG-flagged reason and a clear migration path):**

- The three entry-point methods on `IAssertionSource<FakeLogCollector>`: `HasLogged()`, `HasNotLogged()`, `HasLoggedSequence()`.
- The top-level shorthand entry points (`HasLoggedOnce`, `HasLoggedExactly`, `HasLoggedNothing`, `HasLoggedWarningOrAbove`, etc.).
- The fluent chain methods on `HasLoggedAssertion`, `HasNotLoggedAssertion`, `HasLoggedSequenceAssertion`: every named filter (`AtLevel`, `Containing`, `WithCategory`, `WithException`, `WithInnerException`, `WithInnerExceptionMessage`, `WithScopeProperty`, `WithScopeProperties`, etc.), every terminator (`Once`, `Exactly`, `Between`, `GetMatch`, `GetMatches`, etc.), the sequence-step combinators (`Then`, `ThenAnyOrder`), and the combinator methods (`WithFilter`, `MatchingAny`, `MatchingAll`, `Not`, `When`).
- The `ILogRecordFilter` interface and the `LogFilter` static factory's public methods.
- The `LogCollectorBuilder.Create` factory.
- The `FakeLogCollector` extension methods: `Filter`, `CountMatching`, `DumpTo`, `AssertAllAsync`.

**Explicitly unstable (will change without notice, do not depend on):**

- `LogAssertionBase<TSelf>` and its protected/internal members. The type is `public` only because the CRTP pattern requires it (C# does not allow public classes to inherit from internal); it is annotated `[EditorBrowsable(Never)]` and is **not** a supported derivation point. Treat it as a sealed implementation detail of the three public assertion classes.
- The internal filter classes (`PredicateFilter`, `AndFilter`, `OrFilter`, `NotFilter`). These live behind `ILogRecordFilter` and the `LogFilter` factory.
- The exact format of failure-message snapshot text rendered by `LogAssertionRendering` and exposed via `DumpTo`. The rendering may gain extra detail or change formatting in any release. **Do not pin exact failure-message text in tests**: pin filter match counts and broad markers (e.g. `Contains("[warn]")`) only.
- The `CompatibilitySuppressions.xml` file is a build artifact tracking baseline acceptance, not part of the API contract.

---

## Limitations and future work

The current surface covers the common cases: composable filters, count terminators, sequence assertions (strict and concurrent), scope-property matching, batch assertions, and the `LogSnapshotRenderer` for pinning a full log sequence. Everything below is a demand-driven backlog; nothing is committed.

- **Filters:** time-based (`WithElapsedTime`, `WithTimestamp`, sequence gaps), `DescribedAs` labels, a `WithinTimeout` polling terminator.
- **Sequence:** `ThenImmediately`, `NotInterleaved`, `InOrder`, `HasNotLoggedSequence`, cursor control (`FromNewest` / `SinceLastAssert`).
- **Bigger pieces:** a `[LoggerMessage]`-derived typed-assertion source generator, `ToVerifyString()` golden files, and `LogAssertions.NUnit` / `.xUnit` / `.MSTest` adapters (the core already supports them; built on request).

Declined by design: an analyzer policing our own API, and silently injecting global usings via a package `.props` file. Out of scope for AOT: anonymous-object scope inspection and JSON matching.

[Open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml) if you would use any of these.

## Family compatibility

The nine assertion-family packages: `LogAssertions.TUnit`, `TimeAssertions.TUnit`, `SnapshotAssertions.TUnit`, `MathAssertions.TUnit`, `JsonAssertions.TUnit`, `SseAssertions.TUnit`, `GrpcAssertions.TUnit`, `TracingAssertions.TUnit`, and `MetricsAssertions.TUnit`: release independently and target the same .NET TFM at any moment (LTS-anchored, multi-target during STS support windows; see the [TFM policy in CONVENTIONS.md](CONVENTIONS.md#tfm-policy) for the rotation schedule). **Mix versions freely.** Each package ships under SemVer with `EnablePackageValidation` strict-mode ApiCompat against its previous baseline, so binary breaks within a version line are caught at pack time.

For per-package release notes:
- [LogAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/CHANGELOG.md)
- [TimeAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/CHANGELOG.md)
- [SnapshotAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/SnapshotAssertions.TUnit/blob/main/CHANGELOG.md)
- [MathAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/MathAssertions.TUnit/blob/main/CHANGELOG.md)
- [JsonAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/JsonAssertions.TUnit/blob/main/CHANGELOG.md)
- [SseAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/CHANGELOG.md)
- [GrpcAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/GrpcAssertions.TUnit/blob/main/CHANGELOG.md)
- [TracingAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/TracingAssertions.TUnit/blob/main/CHANGELOG.md)
- [MetricsAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/MetricsAssertions.TUnit/blob/main/CHANGELOG.md)

## Pair with

- **[`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/)**: `FakeTimeProvider` state assertions, `TimeProvider`-aware `DateTimeOffset` recency / past / future checks, and the cross-cutting `.And.WithinTimeBudget(TimeSpan)` chain extension. Compose with `HasLogged()` to add a timing budget to log assertions.
- **[`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/)**: text-snapshot assertions for API-surface tests and similar deterministic-string scenarios. Use `MatchesSnapshot()` to pin the rendered output of `LogAssertionRendering` (e.g. `DumpTo` output) in integration tests.
- **[`MathAssertions.TUnit`](https://www.nuget.org/packages/MathAssertions.TUnit/)**: tolerance-aware fluent assertions over numeric and geometric types (vectors, quaternions, matrices, planes, complex numbers, arrays).
- **[`JsonAssertions.TUnit`](https://www.nuget.org/packages/JsonAssertions.TUnit/)**: fluent JSON assertions over `System.Text.Json`, HTTP response bodies (including RFC 7807 ProblemDetails), and source-generated `JsonSerializerContext` registration.
- **[`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/)**: Server-Sent Events (SSE) wire-format assertions: event-count, field shape (`event:`, `data:`, `id:`, `retry:`), and stream content validation.
- **[`GrpcAssertions.TUnit`](https://www.nuget.org/packages/GrpcAssertions.TUnit/)**: fluent gRPC outcome assertions (`ThrowsGrpcException` with `StatusCode` shorthands and detail refinements) plus the `GrpcCallBuilder` test-double helper.
- **[`TracingAssertions.TUnit`](https://www.nuget.org/packages/TracingAssertions.TUnit/)**: fluent OpenTelemetry distributed-tracing (`Activity` / span) assertions: operation name, tags, status, and parent/child and same-trace relationships, captured via a raw `ActivityListener` with no OpenTelemetry SDK dependency.

---

## Background

The TUnit feature request that motivated this package was [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627), declined on architectural grounds (no cross-package coupling between `TUnit.Logging.Microsoft` and `TUnit.Assertions`). The user-space pattern was unblocked when [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785) shipped `[AssertionExtension]` infrastructure. This package implements the user-space pattern.
- **[`MetricsAssertions.TUnit`](https://www.nuget.org/packages/MetricsAssertions.TUnit/)**: fluent assertions over `System.Diagnostics.Metrics` instruments (counters, histograms, gauges), built on `MetricCollector`.

## Contributing

Issues and pull requests welcome. Before opening a PR:

- Run `dotnet build` and `dotnet test` locally; the CI pipeline enforces the same quality bar (zero warnings as errors, 90% line / 90% branch coverage minimum).
- Match the existing code style (`.editorconfig` is authoritative; `dotnet format` covers formatting).
- For new assertions, include a test for both the happy path and a representative failure case so the failure-message rendering is verified.

For larger ideas (new entry points, breaking changes, cross-cutting refactors), open a [Discussion](https://github.com/JohnVerheij/LogAssertions.TUnit/discussions) first to align on direction before investing implementation time.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full PR review checklist and API design principles, and [CONVENTIONS.md](CONVENTIONS.md) for the family-wide code conventions shared across `LogAssertions.TUnit`, `SnapshotAssertions.TUnit`, `TimeAssertions.TUnit`, `MathAssertions.TUnit`, `JsonAssertions.TUnit`, `SseAssertions.TUnit`, and `GrpcAssertions.TUnit`.

## License

[MIT](LICENSE). Copyright (c) 2026 John Verheij.
