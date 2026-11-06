# LogAssertions

[![NuGet](https://img.shields.io/nuget/v/LogAssertions.svg)](https://www.nuget.org/packages/LogAssertions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Docs](https://img.shields.io/badge/docs-logassertions.dev-512BD4.svg)](https://logassertions.dev)

> **Scope:** Test projects only. Not intended for production code.

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family. This is the framework-agnostic core; the TUnit assertions live in the matching `.TUnit` package.

Framework-agnostic core for fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`.

> **Most users want [`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/), not this package directly.** This is the shared engine; framework-specific adapter packages add the assertion entry points your test framework expects.

---

## What's in this package

- **`ILogRecordFilter`**: composable filter interface (`Matches(FakeLogRecord)` + `Description`).
- **`LogFilter`**: static factory for every built-in filter shape (`AtLevel`, `Containing`, `WithMessageTemplate`, `WithException`, `WithoutException` *(v0.6.0+)*, `WithInnerException` *(v0.4.0+)*, `WithInnerExceptionMessage` *(v0.4.0+)*, `WithProperty`, `WithProperty<T>` *(v0.6.0+)*, `WithCategory`, `WithEventId`, `WithScope`, `WithScopeProperty`, `WithScopeProperty<T>` *(v0.6.0+)*, `WithScopeProperties` *(v0.4.0+)*, `Matching(LogDefinition)` / `MatchingCall(LogDefinition)` *(v0.11.0+)*, `Where`, etc.) plus combinators (`All`, `Any`, `Not`).
- **`LogDefinition`** *(v0.11.0+)*: the captured shape of one log call. `LogDefinition.Capture(log => LogMessages.OrderShipped(log, 0, ""))` invokes the delegate once against a private probe logger and returns a reusable assertion value carrying the event ID, name, template, placeholder values, and exception. Pair with `LogFilter.Matching` (identity: ID + name + template) or `LogFilter.MatchingCall` (identity + placeholder values + exception).
- **`LogAssertionRendering`**: failure-snapshot rendering (4-character level abbreviation, props line, scopes line, exception line) with `DumpVerbosity`-controlled detail level *(v0.4.0+: `Compact` / `Default` / `Verbose`)*. Used by adapter packages and by the `DumpTo` extension below.
- **`LogCollectorBuilder.Create(LogLevel)`**: one-line factory returning a **`LogCapture`** *(v0.14.0+; before, a `(Factory, Collector)` tuple, and `var (factory, collector) = ...` now fails with a build error naming the new form)*: the logger factory itself plus its `Collector`, so one `using` disposes everything, including providers added with `AddProvider` (also `IAsyncDisposable`). Disposal does not stop capture: a logger created before keeps writing into the collector. Record timestamps come from the system clock; **`LogCollectorBuilder.Create(TimeProvider, LogLevel)`** *(v0.14.0+)* stamps them with the given clock, a fake one in tests. **`LogCollectorBuilder.CreateDiscarding(LogLevel)`** *(v0.13.0+)* returns an enabled `ILoggerFactory` that keeps nothing, so `IsEnabled`-guarded code runs under coverage without a growing collector. Its `BeginScope` returns a shared scope whose disposal does nothing, never `null` *(v0.13.1+)*.
- **`LogSnapshotRenderer`** and **`LogContractRenderer`** *(v0.14.0+)* in `LogAssertions.Render`: deterministic text for `MatchesSnapshot()` baselines. `LogSnapshotRenderer.Render(collector)` writes the captured records; `LogContractRenderer.Render(definitions)` writes one line per log definition (`1201 OrderProcessed Information "Order {OrderId} processed in {DurationMs} ms" OrderId,DurationMs`), ordered by event ID, so a renamed placeholder or a reworded template shows as a diff.
- **Event-driven waits on `FakeLogCollector`** *(v0.13.0+)*: `WaitForLogAsync(ILogRecordFilter, CancellationToken)`, `WaitForLogAsync(Func<FakeLogRecord, bool>, CancellationToken)`, and `WaitForLogsAsync(ILogRecordFilter, int, CancellationToken)` return once background work has logged the matching records, built on `FakeLogCollector.GetLogsAsync`: no polling, no real-time delay, and records already collected count. They read no clock: the token is the only bound.
- **Inspection extensions on `FakeLogCollector`:** `Filter(params ILogRecordFilter[])` returns matching records, `CountMatching(params ILogRecordFilter[])` returns the count, `DumpTo(TextWriter)` and `DumpTo(TextWriter, DumpVerbosity)` *(v0.4.0+)* write the captured-records snapshot.

## Test-framework adapters

| Package | Test framework | Status |
|---|---|---|
| [`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/) | TUnit | Available now |

Adapters for other test frameworks (NUnit, xUnit, MSTest) are not planned; the family targets TUnit.

## Installation

```
dotnet add package LogAssertions.TUnit
```

`LogAssertions` comes transitively. You don't need to install it directly unless you're building your own adapter package. **Requirements:** .NET 10; the TUnit adapter needs TUnit 1.80.0 or later, and this core package does not depend on TUnit.

## Direct use

If you're building a custom adapter or want non-asserting access to filtered records, the public surface lets you compose filters and inspect the collector without going through any test-framework's assertion type:

```csharp
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using var logs = LogCollectorBuilder.Create();
var logger = logs.CreateLogger("MyCategory");
logger.LogWarning("validation failed: TimeoutMs out of range");

// Compose a reusable filter:
ILogRecordFilter validationWarning = LogFilter.All(
    LogFilter.AtLevel(LogLevel.Warning),
    LogFilter.Containing("validation", StringComparison.Ordinal));

// Inspect without asserting:
int count = logs.Collector.CountMatching(validationWarning);                 // 1
var matches = logs.Collector.Filter(validationWarning);                      // IReadOnlyList<FakeLogRecord>
using var writer = new StringWriter();
logs.Collector.DumpTo(writer);                                               // print the captured-records snapshot
```

## Building a custom adapter

A test-framework adapter package needs to:

1. Reference `LogAssertions`.
2. Provide entry-point methods that surface a fluent chain over the framework's assertion type.
3. Use `LogFilter` factory methods to build the filter chain.
4. Use `LogAssertionRendering.AppendCapturedRecords(...)` for the snapshot of captured records (so the format stays consistent across adapters). It writes every record; `LogAssertions.TUnit`'s own failure messages show only the last 50.

`LogAssertions.TUnit` is the reference implementation: see its source for the pattern.

## Stability

- The public surfaces above (`ILogRecordFilter`, `LogFilter`, `LogDefinition`, `LogAssertionRendering`, `LogCollectorBuilder`, `LogCapture`, the renderers, the extension methods on `FakeLogCollector`) are intended to stay stable. Before 1.0 a minor version can break them; every break is listed under **BREAKING** in the CHANGELOG with its migration.
- The output of `LogSnapshotRenderer` and `LogContractRenderer` is a pin-able contract: a change to it is listed as breaking.
- The exact text format of failure-message snapshots rendered by `LogAssertionRendering` is **not stable**. May gain extra detail or change formatting in any release. Pin filter match counts and broad markers in tests, not exact failure text.

## License

[MIT](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
