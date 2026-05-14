namespace LogAssertions.Render;

/// <summary>How <see cref="LogSnapshotRenderer"/> renders an exception attached to a record.</summary>
public enum ExceptionStyle
{
    /// <summary>
    /// Render <c>{ExceptionTypeName}: {Message}</c> only. Fully deterministic.
    /// </summary>
    TypeAndMessage,

    /// <summary>
    /// Render <c>{ExceptionTypeName}: {Message} {STACKTRACE}</c>. The literal
    /// <c>{STACKTRACE}</c> token stands in for the volatile stack trace, so the line stays
    /// deterministic while still recording that a stack trace was present.
    /// </summary>
    StackTracePlaceholder,

    /// <summary>
    /// Render the exception's full <see cref="System.Exception.ToString()"/>, including the
    /// real stack trace and inner-exception chain. NOT deterministic across runs (JIT frame
    /// rotation, line-number drift): pair with a stack-trace scrubber when snapshotting.
    /// </summary>
    Full,
}
