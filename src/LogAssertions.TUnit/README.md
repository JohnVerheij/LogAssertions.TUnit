# LogAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. AOT-compatible, trimmable, no reflection.

> **Full documentation, full filter reference, design notes, and roadmap:** [github.com/JohnVerheij/LogAssertions.TUnit](https://github.com/JohnVerheij/LogAssertions.TUnit)

## Install

```
dotnet add package LogAssertions.TUnit
```

`LogAssertions` (the framework-agnostic core) comes transitively. **Requirements:** TUnit 1.47.0 or later, .NET 10.

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

Filters chain with AND semantics: `AtLevel`, `AtLevelOrAbove`, `Containing`, `WithException<T>`, `WithInnerException<T>` *(v0.4.0+)*, `WithInnerExceptionMessage` *(v0.4.0+)*, `WithProperty`, `WithCategory`, `WithEventId`, `WithScope<T>`, `WithScopeProperty`, `WithScopeProperties` *(v0.4.0+)*, plus combinators `MatchingAny`/`MatchingAll`/`Not`/`WithFilter` for composable filter objects. Sequence assertions chain via `Then()` (strict order) or `ThenAnyOrder(...)` *(v0.4.0+)* (concurrent group; sub-steps may match in any order). [Full filter reference on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#filter-reference)

## Cookbook

**Assert no errors were logged:**
```csharp
await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
```

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

**Assert a startup → work → shutdown sequence:**
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

## License

[MIT](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
