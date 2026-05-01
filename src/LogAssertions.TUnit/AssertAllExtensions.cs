using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Core;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit;

/// <summary>
/// Batch-assertion entry point that runs multiple independent assertions against the same
/// <see cref="FakeLogCollector"/> in a single pass and reports all failures together,
/// rather than failing fast on the first one. Conceptually similar to TUnit's own
/// <c>Assert.Multiple</c>, scoped specifically to log assertions.
/// </summary>
public static class AssertAllExtensions
{
    /// <summary>
    /// Runs every <paramref name="assertions"/> against the underlying collector. If any
    /// throws, all failures are gathered and re-thrown as a single <see cref="AssertionException"/>
    /// whose message lists each failure in order. Useful when several independent invariants
    /// must all hold and the test author wants to see every violation in one CI run, not just
    /// the first.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <param name="assertions">The assertion lambdas; each receives the source and returns a Task.</param>
    /// <returns>A task that completes when all assertions have run.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="AssertionException">One or more inner assertions failed; the message aggregates all failures.</exception>
    public static async Task AssertAllAsync<TActual>(
        this IAssertionSource<TActual> source,
        params Func<IAssertionSource<TActual>, Task>[] assertions)
        where TActual : FakeLogCollector
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(assertions);

        List<Exception>? failures = null;
        for (var i = 0; i < assertions.Length; i++)
        {
            var assertion = assertions[i] ?? throw new ArgumentException(
                "AssertAll: assertion at index " + i + " is null", nameof(assertions));

            try
            {
                await assertion(source).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // catch general exception: AssertAll's contract is to gather every failure
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures ??= [];
                failures.Add(ex);
            }
        }

        if (failures is null)
            return;

        if (failures.Count == 1)
            throw failures[0];

        StringBuilder sb = new();
        sb.Append("AssertAll: ").Append(failures.Count).Append(" of ").Append(assertions.Length).Append(" assertions failed:");
        for (var i = 0; i < failures.Count; i++)
        {
            sb.AppendLine().AppendLine().Append("  [").Append(i + 1).Append("] ").Append(failures[i].Message);
        }

        throw new AssertionException(sb.ToString());
    }
}
