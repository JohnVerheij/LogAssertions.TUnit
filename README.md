# LogAssertions.TUnit

[![CI](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/LogAssertions.TUnit/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/JohnVerheij/LogAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/LogAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/LogAssertions.TUnit.svg)](https://www.nuget.org/packages/LogAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

TUnit-native fluent log-assertion DSL on top of `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. Built using TUnit 1.41.0+'s `[AssertionExtension]` source generator, so the assertion methods integrate directly into TUnit's `Assert.That(...)` pipeline with rich failure diagnostics.

> **Scope:** Test projects only. This package is not intended for production code.

## Why this package

Asserting on log output during tests typically devolves into either:

- Manual `collector.GetSnapshot().Where(...).Count()` plumbing in every test, or
- Adding temporary `Console.WriteLine` calls during debugging because the assertion failure says "expected 1, got 3" without showing what was actually logged.

This library replaces both with a fluent DSL that integrates with TUnit's assertion pipeline and shows every captured record in failure messages.

## Install

```
dotnet add package LogAssertions.TUnit
```

**Requirements:** TUnit 1.41.0+ (for `[AssertionExtension]`), .NET 10.

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

## API

### Entry points

| Method | Default expectation |
|---|---|
| `HasLogged()` | At least 1 matching record |
| `HasNotLogged()` | Zero matching records |

### Filters (chain any combination)

| Filter | Behaviour |
|---|---|
| `AtLevel(LogLevel)` | Exact level match |
| `Containing(string, StringComparison)` | Message contains substring (comparison explicit by design) |
| `WithMessage(Func<string, bool>)` | Message satisfies predicate |
| `WithException<TException>()` | Record's exception is assignable to `TException` |
| `WithProperty(string key, string? value)` | Structured-state key matches value (ordinal) |
| `WithCategory(string)` | Logger category equals string (ordinal) |
| `WithEventId(int)` | `EventId.Id` equals value |
| `WithEventName(string)` | `EventId.Name` equals string (ordinal) |
| `WithScope<TScope>()` | A scope of type `TScope` was active when the record was emitted |
| `Where(Func<FakeLogRecord, bool>)` | Escape hatch: arbitrary predicate over the full record |

All filters return `this` for chaining and combine with AND semantics — every predicate must hold for a record to count as a match.

### Terminators (`HasLogged` only — pick one)

| Terminator | Match count expectation |
|---|---|
| `Once()` | Exactly 1 |
| `Exactly(int count)` | Exactly N |
| `AtLeast(int count)` | At least N |
| `AtMost(int count)` | At most N |
| `Never()` | Exactly 0 |

`HasNotLogged` has no terminators — the expectation is fixed at zero matches.

### Combining assertions

`HasLoggedAssertion` and `HasNotLoggedAssertion` derive from TUnit's `Assertion<T>`, so the standard TUnit `.And` / `.Or` chaining works:

```csharp
await Assert.That(collector).HasLogged().AtLevel(LogLevel.Information).AtLeast(1)
    .And.HasNotLogged().AtLevel(LogLevel.Error);
```

### Sequence assertions — `HasLoggedSequence` with `Then()`

For tests that need to verify a series of records appeared in order:

```csharp
await Assert.That(collector).HasLoggedSequence()
    .AtLevel(LogLevel.Information).Containing("Started", StringComparison.Ordinal)
    .Then().AtLevel(LogLevel.Warning).Containing("validation failed", StringComparison.Ordinal)
    .Then().AtLevel(LogLevel.Information).Containing("Stopped", StringComparison.Ordinal);
```

The walk is order-preserving but not contiguous — records between matches are skipped. Each `Then()` commits the current step's filters and starts a new step.

## Failure diagnostics

On a failed assertion, the `AssertionException` message includes:

- The terminator description (`"exactly 1 log record(s) to have been logged"`)
- The actual match count (`"3 record(s) matched"`)
- A snapshot of every captured record with level, category, message, and exception details

Example failure output:

```
Expected: exactly 1 log record(s) to have been logged matching: Level = Warning, Message contains "timeout"
3 record(s) matched

Captured records (5 total):
  [Information] MyApp.Worker: Started cycle 1
  [Warning] MyApp.Worker: timeout exceeded for cycle 1
  [Warning] MyApp.Worker: timeout exceeded for cycle 2
  [Warning] MyApp.Worker: timeout exceeded for cycle 3
  [Information] MyApp.Worker: Cycle batch finished
```

This eliminates the historical pattern of adding temporary `Console.WriteLine` calls to debug failing log assertions — the diagnostic is in the assertion failure itself.

## Design notes

- **Built on `[AssertionExtension]`** (TUnit 1.41.0+, PR thomhurst/TUnit#5785): the entry-point methods are emitted by TUnit's source generator. No extension-method wrappers needed.
- **No cross-package coupling.** This package depends on `TUnit.Assertions` and `Microsoft.Extensions.Diagnostics.Testing`. Neither of those depends on the other; this library is the bridge.
- **AOT-compatible.** The library declares `IsAotCompatible=true` and uses no reflection in the assertion path.
- **Single TFM:** targets `net10.0`. .NET 10 is the current LTS (until Nov 2028).

## Limitations / future work

The 0.1.0 surface (10 filters + 5 terminators + sequence assertions) covers the common cases. Possible additions if demand surfaces:

- **`WithTimestamp(...)` filter** — currently considered fragile in tests; not planned
- **`HasLoggedSequenceContiguously(...)` variant** — strict adjacency rather than order-preserving
- **Source-generated assertions for project-specific log message methods** — would let `.HasLogged().ValidationFailed("msg")` chain against a `[LoggerMessage]` declaration

If you'd find any of these useful, [open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml).

## Background

The TUnit feature request that motivated this package was [thomhurst/TUnit#5627](https://github.com/thomhurst/TUnit/issues/5627), declined on architectural grounds (no cross-package coupling between `TUnit.Logging.Microsoft` and `TUnit.Assertions`). The user-space pattern was unblocked when [thomhurst/TUnit#5785](https://github.com/thomhurst/TUnit/pull/5785) shipped `[AssertionExtension]` infrastructure in TUnit 1.41.0. This package implements the user-space pattern.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branch convention, PR checklist, and code style.

## License

[MIT](LICENSE) — Copyright (c) 2026 John Verheij
