using System;
using System.Threading;
using System.Threading.Tasks;

namespace LogAssertions.TUnit.Tests;

/// <summary>Runs code where no test is current.</summary>
internal static class OffContext
{
    /// <summary>Runs <paramref name="action"/> on a thread-pool thread without the execution context, so
    /// <c>TestContext.Current</c> is <see langword="null"/> there (no flowed context, no <c>Activity</c> baggage).</summary>
    /// <param name="action">The code to run.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when <paramref name="action"/> has run, faulted when it threw.</returns>
    public static Task RunAsync(Action action, CancellationToken cancellationToken)
    {
        Task run;
        using (ExecutionContext.SuppressFlow())
        {
            run = Task.Run(action, cancellationToken);
        }

        return run.WaitAsync(cancellationToken);
    }

    /// <summary>Runs <paramref name="action"/> as <see cref="RunAsync(Action, CancellationToken)"/> does, for code that
    /// awaits: its continuations keep the empty context.</summary>
    /// <param name="action">The code to run.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when <paramref name="action"/> has completed, faulted when it threw.</returns>
    public static Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        Task run;
        using (ExecutionContext.SuppressFlow())
        {
            run = Task.Run(action, cancellationToken);
        }

        return run.WaitAsync(cancellationToken);
    }
}
