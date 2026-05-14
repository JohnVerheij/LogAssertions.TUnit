using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using LogAssertions.Render;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Coverage-instrumentation exercise for <see cref="LogSnapshotRenderer"/>. The authoritative
/// contract tests live in <c>tests/LogAssertions.Tests/LogSnapshotRendererTests.cs</c>: that
/// project is framework-agnostic (no <c>LogAssertions.TUnit</c> reference), so the renderer's
/// framework-independence is structurally enforced. The CI coverage gate, however, instruments
/// only this project's test exe; the renderer's lines sit in <c>LogAssertions.dll</c> and would
/// show as uncovered without a touchpoint here. Each branch of the renderer is exercised once
/// below so the production assembly's coverage rate reflects the actual test depth.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogSnapshotRendererCoverageExercise
{
    /// <summary>Custom non-key-value-pair log state, used to drive the
    /// <see cref="LogSnapshotRenderer"/> guarded-<c>StructuredState</c> branch.</summary>
    private sealed record CustomState(string Text);

    /// <summary>A scope value whose <see cref="object.ToString"/> returns <see langword="null"/>,
    /// used to drive the renderer's <c>?? "null"</c> defensive fall-backs around
    /// <see cref="Convert.ToString(object?, IFormatProvider?)"/>.</summary>
    private sealed class NullToString
    {
        /// <inheritdoc/>
#pragma warning disable S2225 // Returning null is the point: it drives the renderer's Convert.ToString(...) ?? "null" guard.
        public override string? ToString() => null;
#pragma warning restore S2225
    }

    /// <summary>Exercises empty input, multi-record separation, both level styles, and the
    /// category-rendering branches (leaf-only trim, plain name, full dotted name).</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task RendersRecordAndCategoryVariants(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            await Assert.That(LogSnapshotRenderer.Render(collector)).IsEqualTo(string.Empty);

#pragma warning disable CA1848, CA1873
            factory.CreateLogger("My.App.OrderService").LogInformation("first");
            factory.CreateLogger("Worker").LogWarning("second");
#pragma warning restore CA1848, CA1873

            // Default: abbreviated level, leaf-only category (dotted name trimmed; plain name passes through).
            await Assert.That(LogSnapshotRenderer.Render(collector))
                .IsEqualTo("[00] info OrderService \"first\"\n\n[01] warn Worker \"second\"\n");

            // Full level name plus full dotted category.
            var full = LogSnapshotOptions.Default with
            {
                LevelStyle = LogLevelStyle.Full,
                CategoryStyle = CategoryStyle.Full,
            };
            await Assert.That(LogSnapshotRenderer.Render(collector, full))
                .IsEqualTo("[00] Information My.App.OrderService \"first\"\n\n[01] Warning Worker \"second\"\n");
        }
    }

    /// <summary>Exercises the structured-state path: normal key-value-pair state, the guarded
    /// <c>catch</c> branch for a custom typed (non-KVP) state, and a null state.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task RendersStructuredStateVariants(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Svc");
#pragma warning disable CA1848, CA1873
            logger.LogInformation("order {OrderId} for {Customer}", 42, "Acme");
            logger.Log(LogLevel.Information, new EventId(0), new CustomState("payload"), null, static (s, _) => s.Text);
            logger.Log<object?>(LogLevel.Information, new EventId(0), null, null, static (_, _) => "nullstate");
            logger.LogInformation("user {UserId}", (object?)null);
#pragma warning restore CA1848, CA1873

            var rendered = LogSnapshotRenderer.Render(collector);
            // Two placeholders so the state loop walks both the first (" state: ") and
            // subsequent ("; ") separator branches; the null placeholder value drives the
            // kvp.Value ?? "null" fall-back.
            await Assert.That(rendered).Contains("    state: OrderId=42; Customer=Acme\n");
            await Assert.That(rendered).Contains("    state: UserId=null\n");
            await Assert.That(rendered).DoesNotContain("OriginalFormat");
            // Custom non-KVP state and null state both render as a header with no state line.
            await Assert.That(rendered).Contains("[01] info Svc \"payload\"\n");
            await Assert.That(rendered).Contains("[02] info Svc \"nullstate\"\n");
        }
    }

    /// <summary>Exercises scope rendering: a multi-entry key-value-pair scope, a plain-object
    /// scope, the multi-scope separator, and <see cref="ScopeStyle.Exclude"/>.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task RendersScopeVariants(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Svc");
#pragma warning disable CA1848, CA1873
            using (logger.BeginScope("user {UserId} role {Role}", "u1", "admin"))
            using (logger.BeginScope(42))
            {
                logger.LogInformation("inside");
            }

            using (logger.BeginScope<object>(null!))
            {
                logger.LogInformation("null scope");
            }
#pragma warning restore CA1848, CA1873

            var withScopes = LogSnapshotRenderer.Render(collector);
            await Assert.That(withScopes).Contains("    scope: ");
            await Assert.That(withScopes).Contains("UserId=u1, Role=admin");
            await Assert.That(withScopes).Contains("42");
            await Assert.That(withScopes).Contains(" | ");

            var excluded = LogSnapshotOptions.Default with { ScopeStyle = ScopeStyle.Exclude };
            await Assert.That(LogSnapshotRenderer.Render(collector, excluded)).DoesNotContain("scope:");
        }
    }

    /// <summary>Exercises all three <see cref="ExceptionStyle"/> rendering branches.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task RendersExceptionVariants(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("Svc").LogError(new InvalidOperationException("boom"), "failed");
#pragma warning restore CA1848, CA1873

            await Assert.That(LogSnapshotRenderer.Render(collector))
                .Contains("    exception: InvalidOperationException: boom\n");

            var placeholder = LogSnapshotOptions.Default with { ExceptionStyle = ExceptionStyle.StackTracePlaceholder };
            await Assert.That(LogSnapshotRenderer.Render(collector, placeholder))
                .Contains("    exception: InvalidOperationException: boom {STACKTRACE}\n");

            var fullException = LogSnapshotOptions.Default with { ExceptionStyle = ExceptionStyle.Full };
            await Assert.That(LogSnapshotRenderer.Render(collector, fullException)).Contains("    exception:\n");
        }
    }

    /// <summary>Exercises the renderer's <c>?? "null"</c> defensive fall-backs: a scope object
    /// and a scope key-value-pair value whose <see cref="object.ToString"/> returns
    /// <see langword="null"/>.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task RendersNullToStringDefensiveBranches(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Svc");
#pragma warning disable CA1848, CA1873
            // Plain-object scope whose ToString() is null: hits the fall-through Convert.ToString.
            using (logger.BeginScope(new NullToString()))
            {
                logger.LogInformation("plain-object scope");
            }

            // Key-value-pair scope whose value's ToString() is null.
            using (logger.BeginScope("data {Payload}", new NullToString()))
            {
                logger.LogInformation("kvp scope");
            }
#pragma warning restore CA1848, CA1873

            var rendered = LogSnapshotRenderer.Render(collector);
            await Assert.That(rendered).Contains("    scope: null\n");
            await Assert.That(rendered).Contains("Payload=null");
        }
    }

    /// <summary>Exercises the <c>record.Category ?? string.Empty</c> null-category fall-back.
    /// A <see cref="FakeLogger"/> constructed directly carries a null category; the logger
    /// factory rejects a null category name, so this is the only way to reach the branch.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task RendersNullCategory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector);
#pragma warning disable CA1848, CA1873
        logger.LogInformation("no category");
#pragma warning restore CA1848, CA1873

        await Assert.That(LogSnapshotRenderer.Render(collector))
            .IsEqualTo("[00] info  \"no category\"\n");
    }

    /// <summary>Exercises the null-collector argument-validation branch.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NullCollectorThrows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(() => LogSnapshotRenderer.Render(null!)).Throws<ArgumentNullException>();
    }
}
