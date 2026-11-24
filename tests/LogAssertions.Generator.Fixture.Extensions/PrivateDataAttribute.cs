using System;
using Microsoft.Extensions.Compliance.Classification;

namespace LogAssertions.Generator.Fixture.Extensions;

/// <summary>A data classification for the fixture's classified parameter.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class PrivateDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes the classification.</summary>
    public PrivateDataAttribute()
        : base(new DataClassification("Fixture", "Private"))
    {
    }
}
