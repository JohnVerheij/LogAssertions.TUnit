namespace LogAssertions.Render;

/// <summary>
/// Controls how <see cref="LogSnapshotRenderer.Render(Microsoft.Extensions.Logging.Testing.FakeLogCollector, LogSnapshotOptions?)"/>
/// formats each captured log record. Construct with an object initializer, or take
/// <see cref="Default"/> and <c>with</c>-mutate only the knobs you care about.
/// </summary>
/// <remarks>
/// Every property defaults to the value that produces the most compact, most deterministic
/// rendering. The default rendering is fully stable run-to-run and cross-platform; only
/// <see cref="ExceptionStyle.Full"/> introduces run-to-run volatility (a stack trace), and
/// that choice is opt-in.
/// </remarks>
public sealed record LogSnapshotOptions
{
    /// <summary>
    /// How the log level is rendered in each record's header line.
    /// Default <see cref="LogLevelStyle.Abbreviation"/>.
    /// </summary>
    public LogLevelStyle LevelStyle { get; init; } = LogLevelStyle.Abbreviation;

    /// <summary>
    /// How the logger category is rendered in each record's header line.
    /// Default <see cref="CategoryStyle.LeafOnly"/>.
    /// </summary>
    public CategoryStyle CategoryStyle { get; init; } = CategoryStyle.LeafOnly;

    /// <summary>
    /// Whether a record's active logging scopes are rendered.
    /// Default <see cref="ScopeStyle.Include"/>.
    /// </summary>
    public ScopeStyle ScopeStyle { get; init; } = ScopeStyle.Include;

    /// <summary>
    /// How an exception attached to a record is rendered.
    /// Default <see cref="ExceptionStyle.TypeAndMessage"/>.
    /// </summary>
    public ExceptionStyle ExceptionStyle { get; init; } = ExceptionStyle.TypeAndMessage;

    /// <summary>
    /// The default options: abbreviation level, leaf-only category, scopes included,
    /// type-and-message exceptions. Fully deterministic and cross-platform stable.
    /// </summary>
    public static LogSnapshotOptions Default { get; } = new();
}
