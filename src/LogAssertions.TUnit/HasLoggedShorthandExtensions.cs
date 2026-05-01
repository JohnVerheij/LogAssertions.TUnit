using LogAssertions.TUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Core;

namespace TUnit.Assertions.Extensions;

/// <summary>
/// Top-level shorthand entry points that wrap the most common <c>HasLogged()...</c> chains.
/// Each shorthand is equivalent to spelling out the underlying chain — they exist purely to
/// reduce ceremony for high-frequency assertions.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>TUnit.Assertions.Extensions</c> (where TUnit's source generator emits the
/// core entry-point extension methods like <c>HasLogged()</c>) so consumers do not need a
/// second <c>using</c> directive to discover these shorthands. If you can call
/// <c>Assert.That(collector).HasLogged()</c> in a file, you can also call
/// <c>Assert.That(collector).HasLoggedOnce()</c> there — same auto-import path.
/// </para>
/// <para>
/// All shorthands return a <see cref="HasLoggedAssertion"/>, so additional filters can still
/// be chained after the shorthand: <c>HasLoggedExactly(3).AtLevel(LogLevel.Warning)</c>
/// asserts "exactly 3 records, all matching the supplied filter set".
/// </para>
/// </remarks>
public static class HasLoggedShorthandExtensions
{
    /// <summary>
    /// Shorthand for <c>HasLogged().Once()</c>: exactly one matching record.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <returns>The assertion, configured for an exact count of 1.</returns>
    public static HasLoggedAssertion HasLoggedOnce<TActual>(this IAssertionSource<TActual> source)
        where TActual : FakeLogCollector
        => source.HasLogged().Once();

    /// <summary>
    /// Shorthand for <c>HasLogged().Exactly(count)</c>: exactly <paramref name="count"/> matching records.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <param name="count">The required match count. Must be non-negative.</param>
    /// <returns>The assertion, configured for the exact count.</returns>
    public static HasLoggedAssertion HasLoggedExactly<TActual>(this IAssertionSource<TActual> source, int count)
        where TActual : FakeLogCollector
        => source.HasLogged().Exactly(count);

    /// <summary>
    /// Shorthand for <c>HasLogged().AtLeast(count)</c>: at least <paramref name="count"/> matching records.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <param name="count">The minimum match count. Must be non-negative.</param>
    /// <returns>The assertion, configured for the minimum count.</returns>
    public static HasLoggedAssertion HasLoggedAtLeast<TActual>(this IAssertionSource<TActual> source, int count)
        where TActual : FakeLogCollector
        => source.HasLogged().AtLeast(count);

    /// <summary>
    /// Shorthand for <c>HasLogged().AtMost(count)</c>: at most <paramref name="count"/> matching records.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <param name="count">The maximum match count. Must be non-negative.</param>
    /// <returns>The assertion, configured for the maximum count.</returns>
    public static HasLoggedAssertion HasLoggedAtMost<TActual>(this IAssertionSource<TActual> source, int count)
        where TActual : FakeLogCollector
        => source.HasLogged().AtMost(count);

    /// <summary>
    /// Shorthand for <c>HasLogged().Between(min, max)</c>: between <paramref name="min"/> and
    /// <paramref name="max"/> matching records (inclusive).
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <param name="min">The minimum match count (inclusive). Must be non-negative.</param>
    /// <param name="max">The maximum match count (inclusive). Must be greater than or equal to <paramref name="min"/>.</param>
    /// <returns>The assertion, configured for the range.</returns>
    public static HasLoggedAssertion HasLoggedBetween<TActual>(this IAssertionSource<TActual> source, int min, int max)
        where TActual : FakeLogCollector
        => source.HasLogged().Between(min, max);

    /// <summary>
    /// Shorthand for <c>HasNotLogged()</c> with no filters: asserts the collector has no records
    /// at all. Reads cleaner than <c>HasNotLogged()</c> for the "produced zero log output" case.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <returns>The negative assertion (no further filters needed).</returns>
    public static HasNotLoggedAssertion HasLoggedNothing<TActual>(this IAssertionSource<TActual> source)
        where TActual : FakeLogCollector
        => source.HasNotLogged();

    /// <summary>
    /// Shorthand for <c>HasLogged().AtLevelOrAbove(LogLevel.Warning)</c>: any record at Warning,
    /// Error, or Critical. Pre-configured for the most common "did anything go wrong" assertion.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <returns>The assertion, with the level filter applied.</returns>
    public static HasLoggedAssertion HasLoggedWarningOrAbove<TActual>(this IAssertionSource<TActual> source)
        where TActual : FakeLogCollector
        => source.HasLogged().AtLevelOrAbove(LogLevel.Warning);

    /// <summary>
    /// Shorthand for <c>HasLogged().AtLevelOrAbove(LogLevel.Error)</c>: any record at Error or
    /// Critical. Pre-configured for the most common "did anything fail" assertion.
    /// </summary>
    /// <typeparam name="TActual">The actual type carried by the assertion source.</typeparam>
    /// <param name="source">The assertion source over a <see cref="FakeLogCollector"/>.</param>
    /// <returns>The assertion, with the level filter applied.</returns>
    public static HasLoggedAssertion HasLoggedErrorOrAbove<TActual>(this IAssertionSource<TActual> source)
        where TActual : FakeLogCollector
        => source.HasLogged().AtLevelOrAbove(LogLevel.Error);
}
