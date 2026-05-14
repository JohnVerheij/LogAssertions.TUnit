namespace LogAssertions.Render;

/// <summary>How <see cref="LogSnapshotRenderer"/> renders a record's logger category.</summary>
public enum CategoryStyle
{
    /// <summary>
    /// Only the last dot-separated segment, e.g. <c>OrderService</c> for
    /// <c>MyApp.Services.OrderService</c>. Same diagnostic value, far less namespace noise
    /// in the snapshot.
    /// </summary>
    LeafOnly,

    /// <summary>The full category string verbatim.</summary>
    Full,
}
