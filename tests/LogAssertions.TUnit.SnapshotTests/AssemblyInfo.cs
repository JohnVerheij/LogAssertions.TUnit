using System.Diagnostics.CodeAnalysis;

// The whole assembly is excluded from coverage measurement: it exists only to host
// Verify-based snapshot tests, which run in a separate CI step without --coverage to
// dodge the Verify.props Deterministic=false x Microsoft.CodeCoverage interaction
// that produces empty cobertura on Linux runners. See the project's csproj header
// comment for the full rationale.
[assembly: ExcludeFromCodeCoverage]
