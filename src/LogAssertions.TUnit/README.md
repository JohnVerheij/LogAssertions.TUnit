# LogAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Docs](https://img.shields.io/badge/docs-logassertions.dev-512BD4.svg)](https://logassertions.dev)

> **Scope:** Test projects only. Not intended for production code.

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.

TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. AOT-compatible, trimmable, no reflection.

> **Full documentation, full filter reference, design notes, and roadmap:** [logassertions.dev](https://logassertions.dev)

## Install

```
dotnet add package LogAssertions.TUnit
```

`LogAssertions` (the framework-agnostic core) comes transitively. **Requirements:** TUnit 1.83.0 or later, .NET 10.

**Setup (v0.14.0+):** add `[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]` once to the test project (for example in `GlobalUsings.cs`). It lets the package see when each test ends, which `DumpToTestOutputOnFailure()` needs (it throws without it) and which makes the test-output tee exact. Everything else works without it; without it, the first tee built in a run writes one note naming this line. **Upgrading from 0.13:** see the [upgrade table on GitHub](https://github.com/JohnVerheij/LogAssertions.TUnit#upgrading-to-v0140) (`Create()` returns a `LogCapture`, and `var (factory, collector) = ...` fails with a build error naming the new form; `AddTeedFakeLogging` lost `teeProvider`; absence counts below the capture floor fail as vacuous; the tee no longer writes into a test that has ended).

## Quick start

```csharp
using LogAssertions;
using Microsoft.Extensions.Logging;

[Test]
public async Task Validation_failure_is_logged()
{
    await using var logs = LogCollectorBuilder.Create();
    var logger = logs.CreateLogger<MyValidator>();
    new MyValidator(logger).Validate(invalidInput);

    await Assert.That(logs.Collector)
        .HasLogged()
        .AtLevel(LogLevel.Warning)
        .Containing("validation failed", StringComparison.Ordinal)
        .Once();

    await Assert.That(logs.Collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
}
```

`LogCollectorBuilder.Create()` returns a `LogCapture` *(v0.14.0+)*: the logger factory itself plus its `Collector`; one `using` disposes everything (`await using` in async code, as the capture is also `IAsyncDisposable`). Record timestamps come from the system clock; `LogCollectorBuilder.Create(timeProvider)` and `CreateTeed(timeProvider)` *(v0.14.0+)* stamp them with a fake clock. `WillLog`'s timeout is always real time ([Clocks on GitHub](https://github.com/JohnVerheij/LogAssertions.TUnit#clocks-v0140)).

## Entry points

| Method | Default expectation |
|---|---|
| `HasLogged()` | At least 1 matching record |
| `HasNotLogged()` | Zero matching records |
| `HasLoggedSequence()` | Records appear in order; `Then()` separates steps, `Step(definition)` adds a typed step *(v0.12.0+)* |
| `HasLoggedOnly(floor)` *(v0.12.0+)* | Nothing at or above `floor` except the definitions passed to `Allowing(...)`; records below the floor are always permitted |
| `WillLog(timeout, cancellationToken)` *(v0.14.0+)* | At least 1 matching record, waiting until it arrives; fails with the full diagnostics when the wait ends |

Plus shorthands: `HasLoggedOnce()`, `HasLoggedExactly(int)`, `HasLoggedAtLeast(int)`, `HasLoggedAtMost(int)`, `HasLoggedBetween(int, int)`, `HasLoggedNothing()`, `HasLoggedWarningOrAbove()`, `HasLoggedErrorOrAbove()`.

Filters chain with AND semantics: `AtLevel`, `AtLevelOrAbove`, `Containing`, `WithException<T>`, `WithException`, `WithoutException` *(v0.6.0+)*, `WithInnerException<T>` *(v0.4.0+)*, `WithInnerExceptionMessage` *(v0.4.0+)*, `WithProperty`, `WithProperty<T>` *(v0.6.0+)*, `WithCategory`, `WithEventId`, `WithScope<T>`, `WithScopeProperty`, `WithScopeProperty<T>` *(v0.6.0+)*, `WithScopeProperties` *(v0.4.0+)*, `Matching(LogDefinition)` / `MatchingCall(...)` *(v0.11.0+)*, plus combinators `MatchingAny`/`MatchingAll`/`Not`/`WithFilter` for composable filter objects. Count terminators on `HasLogged()`: `Once`, `Exactly`, `AtLeast`, `AtMost`, `Between`, `Never`, plus `AtMostOncePer(TimeSpan)` *(v0.14.0+)* for spacing. Sequence assertions chain via `Then()` (strict order) or `ThenAnyOrder(...)` *(v0.4.0+)* (concurrent group; sub-steps may match in any order). [Full filter reference on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#filter-reference)

## Cookbook

**Assert no errors were logged:**

```csharp
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
```

**Wait for a log from background work (v0.14.0+):** background- or pump-driven logs can land after the triggering call returns, so a synchronous `HasLogged()` races the producer. Assert that the record will be logged; the wait wakes on each new record, with no polling and no real-time delay. The count is read from the collector right after the minimum number of matching records has been captured. Records captured by then count, including ones logged before the assertion ran; a record that arrives after the assertion has passed isn't checked (to pin "no more than one", assert `HasLogged()...Once()` after the work has settled). When the `timeout` (real time, from when the assertion runs) or the token ends the wait, the records captured by then decide: enough matching records pass, otherwise it fails with the expectation, the count, and the captured records:

```csharp
await Assert.That(collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken)
    .Containing("dead-lettered", StringComparison.OrdinalIgnoreCase)
    .Once();
```

It replaces polling the collector with `Eventually`. A wait proves arrival, not absence: for "has not logged", let the work finish and assert `HasNotLogged()`. When the work waits on a fake clock, start the assertion, advance the clock, then await the assertion ([`WillLog` with a fake clock on GitHub](https://github.com/JohnVerheij/LogAssertions.TUnit#willlog-with-a-fake-clock)). To get the record outside an assertion, use the core's `collector.WaitForLogAsync(filter, cancellationToken)` *(v0.13.0+)*.

**Mirror logs into the test report (v0.8.0+):** `TestOutputLogCollectorBuilder.CreateTeed()` captures for assertions and also mirrors each record to the test's output, so logs appear inline in the TUnit report:

```csharp
await using var logs = TestOutputLogCollectorBuilder.CreateTeed();
```

Each record goes to the test current when it is logged; otherwise to the test that built the tee, while that test runs; otherwise it is skipped and counted. With the lifetime attribute, a test counts as running until its `[After(Test)]` hooks finish, and a record logged on the flow of a test that has ended is not teed. A background loop (a `BackgroundService`, say) keeps the context of the test it was started in: its records go to that test while it runs, then are skipped and counted, never to a later test. So build and start a host shared across tests inside `ExecutionContext.SuppressFlow()`; TUnit runs hooks such as `[Before(Class)]` and shared-fixture initialization on the flow of the first test that needs them. [Where each record goes, on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#where-each-record-goes)

**A teed logger that keeps nothing (v0.14.0+):** `TestOutputLogCollectorBuilder.CreateTeedDiscarding()` returns an enabled factory (so `[LoggerMessage]` and `IsEnabled`-guarded code runs) that keeps no records but tees them to the report: for fixtures and long-lived hosts nobody asserts on.

**Wire into an existing builder (v0.9.0+):** `AddTeedFakeLogging(this ILoggingBuilder, ...)` composes the same capture and tee into a logging builder you already own (an ASP.NET Core test host, any `LoggerFactory.Create` callback), per test or shared across tests. To add another provider, chain it: `.AddTeedFakeLogging(collector).AddProvider(provider)`, or for TUnit's correlated output `.AddFakeLogging(collector, level).AddCorrelatedTUnit(level)` from `TUnit.Logging.Microsoft`. `AddFakeLogging` is the capture-only variant. If you previously hand-rolled an `AddTeedFakeLogging` extension of your own, remove it to avoid an ambiguous-call (CS0121) collision.

**Write the logs of a test that did not pass (v0.14.0+):** arm once in the test or a `[Before(Test)]` hook; when the test fails, times out, or is canceled, the records captured since the call are written to its output, each with its timestamp from the collector's clock (needs the lifetime attribute):

```csharp
[Before(Test)]
public void ArmLogDump() => Logs.Collector.DumpToTestOutputOnFailure();
```

`Create(minimumLevel)` records a capture floor; an absence check (`HasNotLogged()`, or a `HasLogged()` count that admits zero such as `Never()`) restricted to levels below it fails as vacuous rather than passing for the wrong reason (v0.8.0+; `HasLogged()` counts since v0.14.0).

**Assert a rate-limited record (v0.14.0+):** every two consecutive matching records, by timestamp (the collector's clock: a fake one with `Create(timeProvider)`), are at least the interval apart; exactly the interval passes, one tick less fails, and the failure names the pair that was too close:

```csharp
await Assert.That(collector).HasLogged().Matching(QueueFull).AtMostOncePer(TimeSpan.FromMinutes(1));
```

**Assert a specific call site was hit (anchored on the message template, not the substituted value):**

```csharp
await Assert.That(collector).HasLogged()
    .WithMessageTemplate("Order {OrderId} processed").AtLeast(1);
```

**Assert a `[LoggerMessage]` definition was logged by identity (v0.11.0+):** capture the definition once (argument values in the capture lambda are throwaway) and assert by identity: event ID, name, and template. Wording edits stop breaking the test; a renamed definition breaks at compile time:

```csharp
private static readonly LogDefinition OrderShipped =
    LogDefinition.Capture(log => LogMessages.OrderShipped(log, 0, ""));

await Assert.That(collector).HasLogged().Matching(OrderShipped).Once();

// pin only the placeholder values that matter
await Assert.That(collector).HasLogged()
    .Matching(OrderShipped).WithProperty("OrderId", 42).Once();

// or pin the exact call: every placeholder value plus the exception
await Assert.That(collector).HasLogged()
    .MatchingCall(log => LogMessages.OrderShipped(log, 42, "NYC")).Once();
```

A `private` `[LoggerMessage]` method can't be captured (the capture calls it): match its records with `WithEventId(int)` / `WithEventName(string)` instead, or promote it to `internal` plus `[InternalsVisibleTo]` for the test project; production code never references this package. The `HasLoggedOnly` gate allows definitions, so a method you allow there must be `internal`. **Definitions you assert on must not live on a generic type** (Roslynator RCS1158 fires once they are non-private): host them in a non-generic `static partial class`.

**Pin the log contract (v0.14.0+):** `LogContractRenderer.Render(definitions)` (in `LogAssertions.Render`) writes one deterministic line per definition, so with `MatchesSnapshot()` a renamed placeholder or a reworded template shows as a diff:

```csharp
await Assert.That(LogContractRenderer.Render([OrderProcessed, OrderShipped])).MatchesSnapshot();
// 1201 OrderProcessed Information "Order {OrderId} processed in {DurationMs} ms" OrderId,DurationMs
```

**Gate a run on its log output (v0.12.0+):** assert that no *unexpected* record escaped, which is the check most suites lack. Records below the floor are always permitted, so the Debug/Trace volume never needs enumerating:

```csharp
await Assert.That(collector).HasLoggedOnly(LogLevel.Warning)
    .Allowing(UpstreamContractViolated, StaleSessionDropped);

await Assert.That(collector).HasLoggedOnly(LogLevel.Warning);  // clean-run check
```

**One event, two definitions (v0.12.0+):** when a verbose and a terse form of the same event are selected at run time, match either with `MatchingAny(verbose, terse)`. [Full typed-definition reference on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#typed-definition-filters-matching-matchingcall-v0110)

**Assert a specific exception flowed through a logger:**

```csharp
await Assert.That(collector).HasLogged()
    .AtLevel(LogLevel.Error)
    .WithException<DbUpdateConcurrencyException>()
    .Once();
```

**Assert a wrapped exception (gRPC / RPC pattern, v0.4.0+):**

```csharp
await Assert.That(collector).HasLogged()
    .WithException<RpcException>()
    .WithInnerException<TimeoutException>()
    .Once();
```

**Assert a startup -> work -> shutdown sequence:**

```csharp
await Assert.That(collector).HasLoggedSequence()
    .WithEventName("Startup")
    .Then().AtLevel(LogLevel.Information).Containing("processed", StringComparison.Ordinal)
    .Then().WithEventName("Shutdown");
```

**Assert a fan-out completion in any order (v0.4.0+):**

```csharp
await Assert.That(collector).HasLoggedSequence()
    .Containing("Request received", StringComparison.Ordinal)
    .ThenAnyOrder(
        s => s.Containing("Auth check passed", StringComparison.Ordinal),
        s => s.Containing("Quota check passed", StringComparison.Ordinal))
    .Then().Containing("Response sent", StringComparison.Ordinal);
```

**Assert several invariants and report all failures together:**

```csharp
await Assert.That(collector).AssertAllAsync(
    c => c.HasLogged().AtLevel(LogLevel.Information).AtLeast(1),
    c => c.HasNotLogged().AtLevelOrAbove(LogLevel.Error),
    c => c.HasLoggedSequence().WithEventName("Startup").Then().WithEventName("Shutdown"));
```

## Failure diagnostics

On a failed count assertion, the exception message includes the expectation, the actual count, and a snapshot of the captured records (level abbreviation, category, message, structured properties, scopes, exception): the last 50 *(v0.14.0+)*, after one line that counts the earlier ones. A sequence failure names the step that did not match; a `HasLoggedOnly` failure lists the unexpected records and the allowlist; a capture-floor failure explains the floor. The explicit dumps (`DumpTo`, `DumpToTestOutput`, `DumpToTestOutputOnFailure`) write every record. No need for `Console.WriteLine` debugging: every dimension you can filter on is also rendered in the failure message.

[Full failure-diagnostics example, design notes, stability intent, and roadmap on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#failure-diagnostics)

## Family

Part of an assertion family for TUnit:

- [TimeAssertions.TUnit](https://github.com/JohnVerheij/TimeAssertions.TUnit)
- [SnapshotAssertions.TUnit](https://github.com/JohnVerheij/SnapshotAssertions.TUnit)
- [MathAssertions.TUnit](https://github.com/JohnVerheij/MathAssertions.TUnit)
- [JsonAssertions.TUnit](https://github.com/JohnVerheij/JsonAssertions.TUnit)
- [SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)
- [GrpcAssertions.TUnit](https://github.com/JohnVerheij/GrpcAssertions.TUnit)
- [TracingAssertions.TUnit](https://github.com/JohnVerheij/TracingAssertions.TUnit)
- [MetricsAssertions.TUnit](https://github.com/JohnVerheij/MetricsAssertions.TUnit)

## License

[MIT](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
