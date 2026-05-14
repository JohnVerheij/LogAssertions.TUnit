using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using LogAssertions.Render;
using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for <see cref="LogSnapshotRenderer"/> and
/// <see cref="LogSnapshotOptions"/>. Like the rest of this project, these tests do not
/// reference <c>LogAssertions.TUnit</c> and use only raw TUnit <see cref="Assert"/>
/// infrastructure.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogSnapshotRendererTests
{
    /// <summary>
    /// Verifies an empty collector renders as <see cref="string.Empty"/>, matching the
    /// empty-input contract of the sibling timeline renderer.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task EmptyCollectorRendersEmptyStringAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            await Assert.That(LogSnapshotRenderer.Render(collector)).IsEqualTo(string.Empty);
        }
    }

    /// <summary>
    /// Verifies a single record renders as a header line in the
    /// <c>[NN] level category "message"</c> shape, terminated with a single LF.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task SingleRecordRendersHeaderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("OrderService").LogInformation("order processed");
#pragma warning restore CA1848, CA1873

            await Assert.That(LogSnapshotRenderer.Render(collector))
                .IsEqualTo("[00] info OrderService \"order processed\"\n");
        }
    }

    /// <summary>
    /// Verifies multiple records render with zero-padded ascending indices and a single
    /// blank line between each record block.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task MultipleRecordsAreBlankLineSeparatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Svc");
#pragma warning disable CA1848, CA1873
            logger.LogInformation("first");
            logger.LogWarning("second");
#pragma warning restore CA1848, CA1873

            await Assert.That(LogSnapshotRenderer.Render(collector))
                .IsEqualTo("[00] info Svc \"first\"\n\n[01] warn Svc \"second\"\n");
        }
    }

    /// <summary>
    /// Verifies <see cref="LogLevelStyle.Full"/> renders the full level name instead of the
    /// four-character abbreviation.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task LevelStyleFullRendersFullNameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("Svc").LogWarning("careful");
#pragma warning restore CA1848, CA1873

            var options = LogSnapshotOptions.Default with { LevelStyle = LogLevelStyle.Full };
            await Assert.That(LogSnapshotRenderer.Render(collector, options))
                .IsEqualTo("[00] Warning Svc \"careful\"\n");
        }
    }

    /// <summary>
    /// Verifies the default <see cref="CategoryStyle.LeafOnly"/> reduces a dotted category to
    /// its last segment, while <see cref="CategoryStyle.Full"/> renders it verbatim.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task CategoryStyleControlsNamespaceVerbosityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("My.App.Services.OrderService").LogInformation("done");
#pragma warning restore CA1848, CA1873

            await Assert.That(LogSnapshotRenderer.Render(collector))
                .IsEqualTo("[00] info OrderService \"done\"\n");

            var fullOptions = LogSnapshotOptions.Default with { CategoryStyle = CategoryStyle.Full };
            await Assert.That(LogSnapshotRenderer.Render(collector, fullOptions))
                .IsEqualTo("[00] info My.App.Services.OrderService \"done\"\n");
        }
    }

    /// <summary>
    /// Verifies structured-logging placeholders render on an indented <c>state:</c> line,
    /// with the synthetic <c>{OriginalFormat}</c> entry skipped.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task StructuredStateRendersStateLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("Svc").LogInformation("order {OrderId} for {Customer}", 42, "Acme");
#pragma warning restore CA1848, CA1873

            var rendered = LogSnapshotRenderer.Render(collector);
            await Assert.That(rendered).Contains("    state: OrderId=42; Customer=Acme\n");
            await Assert.That(rendered).DoesNotContain("OriginalFormat");
        }
    }

    /// <summary>
    /// Verifies an active <see cref="ILogger.BeginScope{TState}"/> scope renders on an
    /// indented <c>scope:</c> line by default, and is omitted under
    /// <see cref="ScopeStyle.Exclude"/>.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ScopeStyleControlsScopeRenderingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Svc");
#pragma warning disable CA1848, CA1873
            using (logger.BeginScope("request {RequestId}", "abc"))
            {
                logger.LogInformation("inside scope");
            }
#pragma warning restore CA1848, CA1873

            var withScope = LogSnapshotRenderer.Render(collector);
            await Assert.That(withScope).Contains("    scope: ");
            await Assert.That(withScope).Contains("RequestId=abc");

            var excludeOptions = LogSnapshotOptions.Default with { ScopeStyle = ScopeStyle.Exclude };
            await Assert.That(LogSnapshotRenderer.Render(collector, excludeOptions))
                .DoesNotContain("scope:");
        }
    }

    /// <summary>
    /// Verifies the default <see cref="ExceptionStyle.TypeAndMessage"/> renders an attached
    /// exception as <c>{TypeName}: {Message}</c> with no stack trace.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ExceptionTypeAndMessageRendersTypeAndMessageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("Svc").LogError(new InvalidOperationException("boom"), "operation failed");
#pragma warning restore CA1848, CA1873

            var rendered = LogSnapshotRenderer.Render(collector);
            await Assert.That(rendered).Contains("    exception: InvalidOperationException: boom\n");
            await Assert.That(rendered).DoesNotContain("{STACKTRACE}");
        }
    }

    /// <summary>
    /// Verifies <see cref="ExceptionStyle.StackTracePlaceholder"/> appends the literal
    /// <c>{STACKTRACE}</c> token after the type-and-message, keeping the line deterministic.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ExceptionStackTracePlaceholderAppendsTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("Svc").LogError(new InvalidOperationException("boom"), "operation failed");
#pragma warning restore CA1848, CA1873

            var options = LogSnapshotOptions.Default with { ExceptionStyle = ExceptionStyle.StackTracePlaceholder };
            await Assert.That(LogSnapshotRenderer.Render(collector, options))
                .Contains("    exception: InvalidOperationException: boom {STACKTRACE}\n");
        }
    }

    /// <summary>
    /// Verifies <see cref="ExceptionStyle.Full"/> renders an indented <c>exception:</c>
    /// header followed by the exception's full <see cref="Exception.ToString()"/> text.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ExceptionFullRendersToStringAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            factory.CreateLogger("Svc").LogError(new InvalidOperationException("boom"), "operation failed");
#pragma warning restore CA1848, CA1873

            var options = LogSnapshotOptions.Default with { ExceptionStyle = ExceptionStyle.Full };
            var rendered = LogSnapshotRenderer.Render(collector, options);
            await Assert.That(rendered).Contains("    exception:\n");
            await Assert.That(rendered).Contains("      System.InvalidOperationException: boom");
        }
    }

    /// <summary>
    /// Verifies the rendered output uses only LF line terminators, never CRLF, so a baseline
    /// committed on one platform stays valid for test runs on every other.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task OutputUsesLfLineEndingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Svc");
#pragma warning disable CA1848, CA1873
            using (logger.BeginScope("request {RequestId}", "abc"))
            {
                logger.LogError(new InvalidOperationException("boom"), "failed with {Code}", 7);
            }
            logger.LogInformation("second");
#pragma warning restore CA1848, CA1873

            await Assert.That(LogSnapshotRenderer.Render(collector)).DoesNotContain("\r");
        }
    }

    /// <summary>
    /// Verifies <see cref="LogSnapshotRenderer.Render(Microsoft.Extensions.Logging.Testing.FakeLogCollector, LogSnapshotOptions?)"/>
    /// rejects a null collector argument.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NullCollectorThrowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(() => LogSnapshotRenderer.Render(null!)).Throws<ArgumentNullException>();
    }
}
