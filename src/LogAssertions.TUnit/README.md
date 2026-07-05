# LogAssertions.TUnit

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.


[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Docs](https://img.shields.io/badge/docs-logassertions.dev-512BD4.svg)](https://logassertions.dev)

> **Scope:** Test projects only. Not intended for production code.

TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. AOT-compatible, trimmable, no reflection.

> **Full documentation, full filter reference, design notes, and roadmap:** [logassertions.dev](https://logassertions.dev)

## Install

```
dotnet add package LogAssertions.TUnit
```

`LogAssertions` (the framework-agnostic core) comes transitively. **Requirements:** TUnit 1.56.35 or later, .NET 10.

## Quick start

```csharp
using LogAssertions;
using Microsoft.Extensions.Logging;

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
            .Once();

        await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
    }
}
```

## Entry points

| Method | Default expectation |
|---|---|
| `HasLogged()` | At least 1 matching record |
| `HasNotLogged()` | Zero matching records |
| `HasLoggedSequence()` | Records appear in order; `Then()` separates steps |

Plus shorthands: `HasLoggedOnce()`, `HasLoggedExactly(int)`, `HasLoggedAtLeast(int)`, `HasLoggedBetween(int, int)`, `HasLoggedNothing()`, `HasLoggedWarningOrAbove()`, `HasLoggedErrorOrAbove()`.

Filters chain with AND semantics: `AtLevel`, `AtLevelOrAbove`, `Containing`, `WithException<T>`, `WithException`, `WithoutException` *(v0.6.0+)*, `WithInnerException<T>` *(v0.4.0+)*, `WithInnerExceptionMessage` *(v0.4.0+)*, `WithProperty`, `WithProperty<T>` *(v0.6.0+)*, `WithCategory`, `WithEventId`, `WithScope<T>`, `WithScopeProperty`, `WithScopeProperty<T>` *(v0.6.0+)*, `WithScopeProperties` *(v0.4.0+)*, plus combinators `MatchingAny`/`MatchingAll`/`Not`/`WithFilter` for composable filter objects. Sequence assertions chain via `Then()` (strict order) or `ThenAnyOrder(...)` *(v0.4.0+)* (concurrent group; sub-steps may match in any order). [Full filter reference on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#filter-reference)

## Cookbook

**Assert no errors were logged:**

```csharp
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
```

**Wait for a log from background work (poll until it arrives):** background- or pump-driven logs can land after the triggering call returns, so a synchronous `HasLogged()` races the producer. Wrap it in TUnit's `Eventually` to poll the live collector until the record appears or the timeout elapses, instead of hand-rolling a wait loop:

```csharp
await Assert.That(collector).Eventually(
    c => c.HasLogged().Containing("dead-lettered", StringComparison.OrdinalIgnoreCase).AtLeast(1),
    TimeSpan.FromSeconds(5));
```

`Eventually` (and `WaitsFor`) come from TUnit itself; they re-run the inner assertion against the live collector each interval, so any filter and terminator chain works inside them.

**Mirror logs into the test report (v0.8.0+):** `TestOutputLogCollectorBuilder.CreateTeed()` captures for assertions and also mirrors each record to the test's output, so logs appear inline in the TUnit report. The owning test is captured when you call it (on the test's own thread), so records logged on a background thread are teed too (v0.10.0+):

```csharp
var (factory, collector) = TestOutputLogCollectorBuilder.CreateTeed();
```

**Wire into an existing builder (v0.9.0+):** `AddTeedFakeLogging(this ILoggingBuilder, ...)` composes the same capture and tee into a logging builder you already own (an ASP.NET Core test host, any `LoggerFactory.Create` callback). Configured on the test's own thread it binds the built-in tee to that test, so background-thread records are teed (v0.10.0+); for a host shared across many tests, pass your own correlation-aware `ILoggerProvider` tee instead. `AddFakeLogging` is the capture-only variant. If you previously hand-rolled an `AddTeedFakeLogging` extension of your own, remove it to avoid an ambiguous-call (CS0121) collision.

`Create(minimumLevel)` records a capture floor; a `HasNotLogged()` restricted to levels below it fails as vacuous rather than passing for the wrong reason (v0.8.0+).

**Assert a specific call site was hit (anchored on the message template, not the substituted value):**

```csharp
await Assert.That(collector).HasLogged()
    .WithMessageTemplate("Order {OrderId} processed").AtLeast(1);
```

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

On a failed assertion, the exception message includes the expected match count, the actual count, and a snapshot of every captured record (level abbreviation, category, message, structured properties, scopes, exception). No need for `Console.WriteLine` debugging: every dimension you can filter on is also rendered in the failure message.

[Full failure-diagnostics example, design notes, stability intent, and roadmap on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#failure-diagnostics)

## Family

Part of an assertion family for TUnit:

- [TimeAssertions.TUnit](https://github.com/JohnVerheij/TimeAssertions.TUnit)
- [SnapshotAssertions.TUnit](https://github.com/JohnVerheij/SnapshotAssertions.TUnit)
- [MathAssertions.TUnit](https://github.com/JohnVerheij/MathAssertions.TUnit)
- [JsonAssertions.TUnit](https://github.com/JohnVerheij/JsonAssertions.TUnit)
- [SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)
- [GrpcAssertions.TUnit](https://github.com/JohnVerheij/GrpcAssertions.TUnit)

## License

[MIT](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
