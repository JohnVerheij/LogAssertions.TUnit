# LogAssertions

[![NuGet](https://img.shields.io/nuget/v/LogAssertions.svg)](https://www.nuget.org/packages/LogAssertions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

Framework-agnostic core for fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`.

> **Most users want [`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/), not this package directly.** This is the shared engine; framework-specific adapter packages add the assertion entry points your test framework expects.

---

## What's in this package

- **`ILogRecordFilter`** — composable filter interface (`Matches(FakeLogRecord)` + `Description`).
- **`LogFilter`** — static factory for every built-in filter shape (`AtLevel`, `Containing`, `WithMessageTemplate`, `WithException`, `WithProperty`, `WithCategory`, `WithEventId`, `WithScope`, `Where`, etc.) plus combinators (`All`, `Any`, `Not`).
- **`LogAssertionRendering`** — failure-snapshot rendering (4-character level abbreviation, props line, scopes line, exception line). Used by adapter packages and by the `DumpTo` extension below.
- **`LogCollectorBuilder.Create(LogLevel)`** — one-line factory returning a wired `(ILoggerFactory, FakeLogCollector)` tuple.
- **Inspection extensions on `FakeLogCollector`:** `Filter(params ILogRecordFilter[])` returns matching records, `CountMatching(params ILogRecordFilter[])` returns the count, `DumpTo(TextWriter)` writes the captured-records snapshot.

## Test-framework adapters

| Package | Test framework | Status |
|---|---|---|
| [`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/) | TUnit | Available now |
| `LogAssertions.NUnit` | NUnit | Possible if there is demand |
| `LogAssertions.xUnit` | xUnit | Possible if there is demand |
| `LogAssertions.MSTest` | MSTest | Possible if there is demand |

If you'd find a non-TUnit adapter useful, [open a feature request](https://github.com/JohnVerheij/LogAssertions.TUnit/issues/new?template=feature_request.yml) — adapters are not built proactively.

## Installation

```
dotnet add package LogAssertions.TUnit
```

`LogAssertions` comes transitively. You don't need to install it directly unless you're building your own adapter package.

## Direct use

If you're building a custom adapter or want non-asserting access to filtered records, the public surface lets you compose filters and inspect the collector without going through any test-framework's assertion type:

```csharp
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

var (factory, collector) = LogCollectorBuilder.Create();
using (factory)
{
    var logger = factory.CreateLogger("MyCategory");
    logger.LogWarning("validation failed: TimeoutMs out of range");

    // Compose a reusable filter:
    ILogRecordFilter validationWarning = LogFilter.All(
        LogFilter.AtLevel(LogLevel.Warning),
        LogFilter.Containing("validation", StringComparison.Ordinal));

    // Inspect without asserting:
    int count = collector.CountMatching(validationWarning);                  // 1
    var matches = collector.Filter(validationWarning);                       // IReadOnlyList<FakeLogRecord>
    using var writer = new StringWriter();
    collector.DumpTo(writer);                                                // print the captured-records snapshot
}
```

## Building a custom adapter

A test-framework adapter package needs to:

1. Reference `LogAssertions`.
2. Provide entry-point methods that surface a fluent chain over the framework's assertion type.
3. Use `LogFilter` factory methods to build the filter chain.
4. Use `LogAssertionRendering.AppendCapturedRecords(...)` for failure-message snapshot output (so the format stays consistent across adapters).

`LogAssertions.TUnit` is the reference implementation — see its source for the pattern.

## Stability

- The public surfaces above (`ILogRecordFilter`, `LogFilter`, `LogAssertionRendering`, `LogCollectorBuilder`, the extension methods on `FakeLogCollector`) are semver-bound. Breaking changes require a major version bump.
- The exact text format of failure-message snapshots rendered by `LogAssertionRendering` is **not stable**. May gain extra detail or change formatting in any release. Pin filter match counts and broad markers in tests, not exact failure text.

## License

[MIT](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/LICENSE) — Copyright (c) 2026 John Verheij
