# LogAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. AOT-compatible, trimmable, no reflection.

> **Full documentation, full filter reference, design notes, and roadmap:** [github.com/JohnVerheij/LogAssertions.TUnit](https://github.com/JohnVerheij/LogAssertions.TUnit)

## Install

```
dotnet add package LogAssertions.TUnit
```

`LogAssertions` (the framework-agnostic core) comes transitively. **Requirements:** TUnit 1.41.0+, .NET 10.

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

Filters chain with AND semantics: `AtLevel`, `AtLevelOrAbove`, `Containing`, `WithException<T>`, `WithProperty`, `WithCategory`, `WithEventId`, `WithScope<T>`, `WithScopeProperty`, plus combinators `MatchingAny`/`MatchingAll`/`Not`/`WithFilter` for composable filter objects. [Full filter reference on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#filter-reference)

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

**Assert a startup → work → shutdown sequence:**
```csharp
await Assert.That(collector).HasLoggedSequence()
    .WithEventName("Startup")
    .Then().AtLevel(LogLevel.Information).Containing("processed", StringComparison.Ordinal)
    .Then().WithEventName("Shutdown");
```

## Failure diagnostics

On a failed assertion, the exception message includes the expected match count, the actual count, and a snapshot of every captured record (level abbreviation, category, message, structured properties, scopes, exception). No need for `Console.WriteLine` debugging — every dimension you can filter on is also rendered in the failure message.

[Full failure-diagnostics example, design notes, stability intent, and roadmap on GitHub.](https://github.com/JohnVerheij/LogAssertions.TUnit#failure-diagnostics)

## License

[MIT](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/LICENSE) — Copyright (c) 2026 John Verheij
