using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Pins the v0.6.0 filter additions: <c>WithoutException()</c> (the null-exception filter) and
/// the typed <c>WithScopeProperty&lt;T&gt;</c> / <c>WithProperty&lt;T&gt;</c> overloads (value +
/// predicate forms).
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class LogAssertionsFiltersV060Tests
{
    // ---- WithoutException ----

    /// <summary>A warning logged with no exception matches <c>WithoutException()</c>.</summary>
    [Test]
    public async Task WithoutException_RecordWithNoException_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.ValidationFailed(logger); // Warning, no exception

        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).WithoutException().Once();
    }

    /// <summary>A record that carries an exception does not match <c>WithoutException()</c>.</summary>
    [Test]
    public async Task WithoutException_RecordWithException_DoesNotMatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom")); // Error + exception

        await Assert.That(collector).HasLogged().WithoutException().Never();
    }

    /// <summary><c>WithoutException()</c> and <c>WithException()</c> partition the records: the
    /// warning without an exception matches the former; the error with an exception matches the
    /// latter.</summary>
    [Test]
    public async Task WithoutException_PartitionsRecords(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.ValidationFailed(logger);                                    // no exception
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("x")); // exception

        await Assert.That(collector).HasLogged().WithoutException().Once();
        await Assert.That(collector).HasLogged().WithException().Once();
    }

    /// <summary>Core-factory parity: the standalone <c>LogFilter.WithoutException()</c> matches
    /// the same records as the chain method.</summary>
    [Test]
    public async Task WithoutException_CoreFactory_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.ValidationFailed(logger);
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("x"));

        await Assert.That(collector.CountMatching(LogFilter.WithoutException())).IsEqualTo(1);
        await Assert.That(LogFilter.WithoutException().Description).IsEqualTo("Exception is null");
    }

    // ---- WithScopeProperty<T> ----

    /// <summary>Typed scope-value match: a <see cref="Guid"/> scope value matches the typed
    /// value overload without object-boxing boilerplate.</summary>
    [Test]
    public async Task WithScopePropertyTyped_GuidValue_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        var messageId = Guid.NewGuid();
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("MessageId", messageId) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        await Assert.That(collector).HasLogged().WithScopeProperty("MessageId", messageId).AtLeast(1);
    }

    /// <summary>Typed scope-value mismatch: a different <see cref="Guid"/> does not match.</summary>
    [Test]
    public async Task WithScopePropertyTyped_WrongGuid_DoesNotMatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("MessageId", Guid.NewGuid()) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        await Assert.That(collector).HasLogged().WithScopeProperty("MessageId", Guid.Empty).Never();
    }

    /// <summary>Typed scope predicate: an <see cref="int"/> caller-line predicate matches when the
    /// typed value satisfies it.</summary>
    [Test]
    public async Task WithScopePropertyTyped_IntPredicate_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CallerLine", 17) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        await Assert.That(collector).HasLogged().WithScopeProperty<int>("CallerLine", line => line > 0).Once();
        await Assert.That(collector).HasLogged().WithScopeProperty<int>("CallerLine", line => line > 100).Never();
    }

    /// <summary>Key-existence overload: <c>WithScopeProperty(key)</c> matches a record carrying the
    /// scope key regardless of its value, and <c>HasNotLogged().WithScopeProperty(key)</c> holds when
    /// no record carried the key. Covers scope keys whose value is set internally (caller-info).</summary>
    [Test]
    public async Task WithScopePropertyKeyExistence_MatchesAndNegates(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CallerMember", "Process") }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        // The value is set internally; only the key's presence is asserted.
        await Assert.That(collector).HasLogged().WithScopeProperty("CallerMember").Once();
        await Assert.That(collector).HasNotLogged().WithScopeProperty("CallerFile");
    }

    /// <summary>Runtime-type guard: a scope value whose runtime type is not <typeparamref name="T"/>
    /// never matches the typed overload (an <see cref="int"/> scope value is not matched by a
    /// <c>WithScopeProperty&lt;long&gt;</c> filter).</summary>
    [Test]
    public async Task WithScopePropertyTyped_DifferentRuntimeType_DoesNotMatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CallerLine", 17) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        await Assert.That(collector).HasLogged().WithScopeProperty<long>("CallerLine", 17L).Never();
        await Assert.That(collector).HasLogged().WithScopeProperty<long>("CallerLine", _ => true).Never();
    }

    /// <summary>The object-typed <c>WithScopeProperty(string, object?)</c> chain method remains
    /// reachable when the value is statically typed as <see cref="object"/>; the typed generic
    /// overload only binds for a more specific static type.</summary>
    [Test]
    public async Task WithScopeProperty_ObjectOverload_StillReachable(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("OrderId", 42) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        object boxed = 42;
        await Assert.That(collector).HasLogged().WithScopeProperty("OrderId", boxed).Once();
    }

    /// <summary>Core-factory parity and description for the typed scope overloads.</summary>
    [Test]
    public async Task WithScopePropertyTyped_CoreFactory_MatchesAndDescribes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CallerLine", 17) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("CallerLine", 17))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty<int>("CallerLine", v => v == 17))).IsEqualTo(1);
        await Assert.That(LogFilter.WithScopeProperty("CallerLine", 17).Description).IsEqualTo("Scope CallerLine = 17");
        await Assert.That(LogFilter.WithScopeProperty<int>("CallerLine", _ => true).Description).IsEqualTo("Scope CallerLine matches predicate");
    }

    // ---- WithProperty<T> ----

    /// <summary>Typed structured-state match: the stored formatted string is parsed back to
    /// <see cref="int"/> and compared to the typed value.</summary>
    [Test]
    public async Task WithPropertyTyped_IntValue_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.CycleStarted(logger, 7); // structured property CycleNumber = "7"

        await Assert.That(collector).HasLogged().WithProperty("CycleNumber", 7).Once();
        await Assert.That(collector).HasLogged().WithProperty("CycleNumber", 8).Never();
    }

    /// <summary>Typed structured-state predicate: the parsed <see cref="int"/> value is handed to
    /// the predicate, removing the manual <c>int.TryParse</c> boilerplate.</summary>
    [Test]
    public async Task WithPropertyTyped_IntPredicate_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.CycleStarted(logger, 7);

        await Assert.That(collector).HasLogged().WithProperty<int>("CycleNumber", n => n is > 0 and < 10).Once();
        await Assert.That(collector).HasLogged().WithProperty<int>("CycleNumber", n => n > 100).Never();
    }

    /// <summary>A structured-state value that does not parse to the requested type never matches.</summary>
    [Test]
    public async Task WithPropertyTyped_NonParsableValue_DoesNotMatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.ItemFailed(logger, "not-a-number"); // ItemId = "not-a-number"

        await Assert.That(collector).HasLogged().WithProperty<int>("ItemId", 5).Never();
        await Assert.That(collector).HasLogged().WithProperty<int>("ItemId", _ => true).Never();
    }

    /// <summary>An absent key never matches the typed property overloads.</summary>
    [Test]
    public async Task WithPropertyTyped_AbsentKey_DoesNotMatch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.CycleStarted(logger, 7);

        await Assert.That(collector).HasLogged().WithProperty<int>("MissingKey", 7).Never();
    }

    /// <summary>Core-factory parity and description for the typed property overloads.</summary>
    [Test]
    public async Task WithPropertyTyped_CoreFactory_MatchesAndDescribes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.CycleStarted(logger, 7);

        await Assert.That(collector.CountMatching(LogFilter.WithProperty("CycleNumber", 7))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.WithProperty<int>("CycleNumber", v => v == 7))).IsEqualTo(1);
        await Assert.That(LogFilter.WithProperty("CycleNumber", 7).Description).IsEqualTo("CycleNumber = 7");
        await Assert.That(LogFilter.WithProperty<int>("CycleNumber", _ => true).Description).IsEqualTo("CycleNumber matches predicate");
    }

    /// <summary>Null-argument validation on the typed factory overloads.</summary>
    [Test]
    public async Task WithPropertyAndScopeTyped_NullArguments_Throw(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => LogFilter.WithProperty<int>(null!, 1)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithProperty<int>("k", predicate: null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithProperty<int>(key: null!, predicate: _ => true)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperty<int>(null!, 1)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperty<int>("k", predicate: null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperty<int>(key: null!, predicate: _ => true)).Throws<ArgumentNullException>();
    }

    /// <summary>The typed predicate chain methods propagate the null-argument guard through the
    /// fluent surface (covers the adapter wrappers, not just the core factory).</summary>
    [Test]
    public async Task WithPropertyAndScopeTyped_ChainNullPredicate_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;

        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithProperty<int>("k", (Func<int, bool>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithScopeProperty<int>("k", (Func<int, bool>)null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Composition: the typed scope filter and <c>WithoutException()</c> combine on one
    /// chain, mirroring a structured-logging assertion (a scoped message id, logged without an
    /// exception attached).</summary>
    [Test]
    public async Task TypedScopeAndWithoutException_Compose(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        var messageId = Guid.NewGuid();
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("MessageId", messageId) }))
        {
            TestLogMessages.ValidationFailed(logger); // Warning, no exception
        }

        await Assert.That(collector).HasLogged()
            .AtLevel(LogLevel.Warning)
            .WithScopeProperty("MessageId", messageId)
            .WithoutException()
            .Once();
    }
}
