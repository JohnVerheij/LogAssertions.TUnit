namespace LogAssertions;

/// <summary>
/// Verbosity level for <c>DumpTo(TextWriter, DumpVerbosity)</c> and the TUnit-side
/// <c>DumpToTestOutput(DumpVerbosity)</c> overload. Controls how much detail is rendered per
/// record: just the headline (Compact), the standard one-line-each detail (Default), or the
/// full exception stack trace alongside the rest (Verbose).
/// </summary>
/// <remarks>
/// The exact text produced by each level is documented as <b>not stable</b>; tests that pin
/// dump output should rely on broad markers (e.g. <c>"[warn]"</c>) rather than exact whitespace
/// or punctuation. The verbosity contract is:
/// <list type="bullet">
/// <item><see cref="Compact"/> — one line per record, headline only.</item>
/// <item><see cref="Default"/> — one line per record + indented detail lines for structured
/// state, scopes, and a one-line exception summary.</item>
/// <item><see cref="Verbose"/> — same as <see cref="Default"/> plus the full exception
/// <c>ToString()</c> (including stack trace and inner-exception chain) for any record that
/// carries an exception.</item>
/// </list>
/// </remarks>
public enum DumpVerbosity
{
    /// <summary>One line per record: <c>[lvl] category: message</c>. No properties, scopes, or
    /// exception details. Use when the captured-records list is only needed as an at-a-glance
    /// sanity check.</summary>
    Compact,

    /// <summary>The standard rendering used by failure messages: headline plus one-line-each
    /// summaries of structured state, scopes, and exception. Default for the no-arg overloads
    /// of <c>DumpTo</c> / <c>DumpToTestOutput</c>.</summary>
    Default,

    /// <summary>Default rendering plus the full exception <c>ToString()</c> (stack trace + inner
    /// exceptions). Use when an exception's stack is the diagnostic signal.</summary>
    Verbose,
}
