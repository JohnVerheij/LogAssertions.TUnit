namespace LogAssertions.Render;

/// <summary>Whether <see cref="LogSnapshotRenderer"/> renders a record's active scopes.</summary>
public enum ScopeStyle
{
    /// <summary>Active scopes are rendered on an indented <c>scope:</c> detail line.</summary>
    Include,

    /// <summary>Scopes are omitted entirely.</summary>
    Exclude,
}
