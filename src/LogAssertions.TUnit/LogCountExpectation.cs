using System.Globalization;

namespace LogAssertions.TUnit;

/// <summary>The count expectation of a <c>HasLogged()</c> or <c>WillLog(...)</c> chain: an inclusive range of matching
/// records and its description for the expectation line. The default is at least one.</summary>
internal sealed class LogCountExpectation
{
    /// <summary>Gets the minimum number of matching records (inclusive).</summary>
    internal int Min { get; private set; } = 1;

    /// <summary>Gets the maximum number of matching records (inclusive).</summary>
    internal int Max { get; private set; } = int.MaxValue;

    /// <summary>Gets the description of the range, for example <c>exactly 1</c>.</summary>
    internal string Description { get; private set; } = "at least 1";

    /// <summary>Gets a value indicating whether the expectation is exactly one matching record.</summary>
    internal bool IsExactlyOne => Min is 1 && Max is 1;

    /// <summary>Returns <see langword="true"/> when <paramref name="count"/> lies in the range.</summary>
    /// <param name="count">The number of matching records.</param>
    /// <returns><see langword="true"/> when the count satisfies the expectation.</returns>
    internal bool IsSatisfiedBy(int count) => count >= Min && count <= Max;

    /// <summary>Expects exactly one.</summary>
    internal void SetOnce() => Set(1, 1, "exactly 1");

    /// <summary>Expects exactly <paramref name="count"/>.</summary>
    /// <param name="count">The count.</param>
    internal void SetExactly(int count) => Set(count, count, string.Create(CultureInfo.InvariantCulture, $"exactly {count}"));

    /// <summary>Expects at least <paramref name="count"/>.</summary>
    /// <param name="count">The minimum.</param>
    internal void SetAtLeast(int count) => Set(count, int.MaxValue, string.Create(CultureInfo.InvariantCulture, $"at least {count}"));

    /// <summary>Expects at most <paramref name="count"/>.</summary>
    /// <param name="count">The maximum.</param>
    internal void SetAtMost(int count) => Set(0, count, string.Create(CultureInfo.InvariantCulture, $"at most {count}"));

    /// <summary>Expects none.</summary>
    internal void SetNever() => Set(0, 0, "exactly 0");

    /// <summary>Expects between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <param name="min">The minimum (inclusive).</param>
    /// <param name="max">The maximum (inclusive).</param>
    internal void SetBetween(int min, int max) => Set(min, max, string.Create(CultureInfo.InvariantCulture, $"between {min} and {max}"));

    private void Set(int min, int max, string description)
    {
        Min = min;
        Max = max;
        Description = description;
    }
}
