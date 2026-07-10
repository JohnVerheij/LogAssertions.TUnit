using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// Shared base class for <see cref="HasLoggedAssertion"/>, <see cref="HasNotLoggedAssertion"/>,
/// and <see cref="HasLoggedSequenceAssertion"/>. Implements the filter chain (level, message,
/// exception, structured-state, scope, event, and arbitrary-predicate filters) and the
/// failure-message snapshot rendering. Derived classes own count-expectation semantics and
/// the <c>[AssertionExtension]</c> attribute that registers the entry-point name.
/// </summary>
/// <typeparam name="TSelf">The derived assertion type, returned from filter methods to enable fluent chaining.</typeparam>
/// <remarks>
/// <para>
/// <b>Not for external derivation.</b> This type is public only because the curiously-recurring
/// template pattern (CRTP) used here requires public visibility wherever the public sealed
/// derived classes (<see cref="HasLoggedAssertion"/> etc.) appear. The shape of this base
/// class: protected members, virtual hooks, internal helpers: is implementation detail
/// and may change in any release. Do not derive from it; do not reference its protected
/// members from outside this assembly. The supported public surface is the entry-point
/// extension methods on <c>FakeLogCollector</c> plus the fluent chain methods returning
/// <typeparamref name="TSelf"/>.
/// </para>
/// <para>
/// See the README "Stability promise" section for the full surface-stability contract.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class LogAssertionBase<TSelf> : Assertion<FakeLogCollector>
    where TSelf : LogAssertionBase<TSelf>
{
    private readonly List<ILogRecordFilter> _filters = [];

    /// <summary>
    /// The exact set of log levels a record could carry and still satisfy the chain's level filters.
    /// Starts at every capturable level (<see cref="LogLevel.None"/> is not a loggable level, so it is
    /// excluded) and is narrowed by each level filter: intersection for the inclusive filters (exact /
    /// at-or-above / at-or-below / any-of) and removal for the exclusion filters (not-at-level). Used to
    /// detect a vacuous below-capture-floor assertion (G5): when this set is non-empty but every level
    /// in it sits below the capture floor, no matching record could have been captured. An empty set
    /// means the level filters are contradictory (empty for their own reason), so the floor guard stays
    /// silent.
    /// </summary>
    private readonly HashSet<LogLevel> _matchableLevels =
        [LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical];

    /// <summary>Initialises the base assertion with the supplied TUnit context.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    protected LogAssertionBase(AssertionContext<FakeLogCollector> context) : base(context) { }

    /// <summary>Intersects <see cref="_matchableLevels"/> with the levels an inclusive level filter
    /// admits (the chain's matchable set is the intersection of its level filters).</summary>
    /// <param name="admits">Returns <see langword="true"/> for a level the filter keeps.</param>
    private void RestrictMatchableLevels(Func<LogLevel, bool> admits)
        => _matchableLevels.RemoveWhere(level => !admits(level));

    /// <summary>
    /// Detects a vacuous assertion: one whose level filters restrict it to records at or below a level
    /// the collector's capture floor filtered out, so no such record was ever captured and a
    /// "not logged" check would pass for the wrong reason. Returns the explanatory message when so.
    /// </summary>
    /// <param name="collector">The collector under assertion.</param>
    /// <param name="message">The failure message when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the assertion is vacuous against the registered floor.</returns>
    private protected bool TryDescribeVacuousFloor(
        FakeLogCollector collector, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? message)
    {
        // Only blame the floor when the matchable set is non-empty (the level filters are not
        // contradictory) and its highest level still sits below the floor, so every level the chain
        // could match was filtered out by the floor. An empty set is empty for its own reason
        // (contradictory or exclusionary filters), so reporting a below-floor vacuity there would be
        // the wrong explanation.
        if (LogCaptureFloorRegistry.TryGetFloor(collector, out var floor)
            && _matchableLevels.Count > 0
            && _matchableLevels.Max() < floor)
        {
            message = string.Format(
                CultureInfo.InvariantCulture,
                "this assertion only matches records at level {0} or below, but the collector's capture floor is {1}, "
                + "so records below {1} were never captured and the check is vacuously true. "
                + "Lower the floor (LogCollectorBuilder.Create with a lower minimumLevel) or assert at a captured level.",
                _matchableLevels.Max(),
                floor);
            return true;
        }

        message = null;
        return false;
    }

    /// <summary>
    /// Returns <see langword="this"/> typed as <typeparamref name="TSelf"/> for fluent
    /// chaining. The CRTP constraint <c>where TSelf : LogAssertionBase&lt;TSelf&gt;</c>
    /// makes the cast safe in well-formed consumer code; an <see cref="InvalidCastException"/>
    /// surfaces at the first chain call if a deriving type violates the constraint, so the
    /// failure is loud and immediate rather than a silent reinterpretation. A single
    /// <c>[SuppressMessage]</c> on this property satisfies Meziantou's MA0181 for every
    /// fluent-chain method returning <typeparamref name="TSelf"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("MeziantouAnalyzer", "MA0181:Do not use cast", Justification = "CRTP self-reference: the cast is fail-fast on a misconfigured derived type; a runtime InvalidCastException is preferable to a silent Unsafe.As reinterpretation.")]
    private TSelf Self => (TSelf)this;

    /// <summary>
    /// Records a filter. Default implementation appends to the shared filter chain used by
    /// single-match assertions; <see cref="HasLoggedSequenceAssertion"/> overrides this to
    /// route filters into the current sequence step.
    /// </summary>
    /// <param name="filter">The filter to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    protected virtual void AddFilter(ILogRecordFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filters.Add(filter);
    }

    /// <summary>Filters to records at the specified <paramref name="level"/>.</summary>
    /// <param name="level">The exact log level to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevel(LogLevel level)
    {
        AddFilter(LogFilter.AtLevel(level));
        RestrictMatchableLevels(matchable => matchable == level);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevel({level})");
        return Self;
    }

    /// <summary>Filters to records whose level is greater than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The minimum log level to match (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevelOrAbove(LogLevel level)
    {
        AddFilter(LogFilter.AtLevelOrAbove(level));
        RestrictMatchableLevels(matchable => matchable >= level);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevelOrAbove({level})");
        return Self;
    }

    /// <summary>Filters to records whose level is less than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The maximum log level to match (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevelOrBelow(LogLevel level)
    {
        AddFilter(LogFilter.AtLevelOrBelow(level));
        RestrictMatchableLevels(matchable => matchable <= level);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevelOrBelow({level})");
        return Self;
    }

    /// <summary>Filters to records whose level is one of <paramref name="levels"/>.</summary>
    /// <param name="levels">The set of log levels to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is <see langword="null"/>.</exception>
    public TSelf AtAnyLevel(params LogLevel[] levels)
    {
        AddFilter(LogFilter.AtLevel(levels));
        RestrictMatchableLevels(level => levels.Contains(level));
        Context.ExpressionBuilder.Append(".AtAnyLevel(...)");
        return Self;
    }

    /// <summary>
    /// Filters to records whose message contains <paramref name="substring"/> using the specified
    /// <paramref name="comparison"/>. The comparison is explicit by design: pass
    /// <see cref="StringComparison.Ordinal"/> for the most common case.
    /// </summary>
    /// <param name="substring">The substring to search for. Must be non-null.</param>
    /// <param name="comparison">The string comparison to apply.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf Containing(string substring, StringComparison comparison)
    {
        AddFilter(LogFilter.Containing(substring, comparison));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Containing(\"{substring}\", {comparison})");
        return Self;
    }

    /// <summary>Filters to records whose message contains every one of <paramref name="substrings"/>.</summary>
    /// <param name="comparison">The string comparison to apply.</param>
    /// <param name="substrings">The substrings; the message must contain all of them.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substrings"/> is <see langword="null"/>.</exception>
    public TSelf ContainingAll(StringComparison comparison, params string[] substrings)
    {
        AddFilter(LogFilter.ContainingAll(comparison, substrings));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ContainingAll({comparison}, ...)");
        return Self;
    }

    /// <summary>Filters to records whose message contains at least one of <paramref name="substrings"/>.</summary>
    /// <param name="comparison">The string comparison to apply.</param>
    /// <param name="substrings">The substrings; the message must contain at least one.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substrings"/> is <see langword="null"/>.</exception>
    public TSelf ContainingAny(StringComparison comparison, params string[] substrings)
    {
        AddFilter(LogFilter.ContainingAny(comparison, substrings));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ContainingAny({comparison}, ...)");
        return Self;
    }

    /// <summary>
    /// Filters to records produced by the given <paramref name="definition"/>: equal event ID
    /// (numeric ID plus name) and equal message template. Argument values and level are not
    /// compared: chain <c>WithProperty</c> to pin specific placeholder values and <c>AtLevel</c>
    /// when the level matters. Capture the definition once in a <c>static readonly</c> field via
    /// <see cref="LogDefinition.Capture"/> (the capture lambda's argument values are throwaway).
    /// </summary>
    /// <param name="definition">The captured definition. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public TSelf Matching(LogDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AddFilter(LogFilter.Matching(definition));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Matching({definition})");
        return Self;
    }

    /// <summary>Filters to records whose message matches the regular expression <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The compiled regex. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public TSelf Matching(Regex pattern)
    {
        AddFilter(LogFilter.Matching(pattern));
        Context.ExpressionBuilder.Append(".Matching(/regex/)");
        return Self;
    }

    /// <summary>
    /// Filters to records matching the log call performed by <paramref name="call"/> exactly:
    /// the definition's identity plus every placeholder value plus the exception (both absent,
    /// or same runtime type and equal message). The lambda is invoked once against a probe
    /// logger at chain-build time; pass the exact argument values the production code is
    /// expected to have logged. Level is not compared. Prefer
    /// <see cref="Matching(LogDefinition)"/> plus <c>WithProperty</c> when only some argument
    /// values are deterministic.
    /// </summary>
    /// <param name="call">A delegate performing exactly one log call with the expected argument
    /// values. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="call"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="call"/> logged zero or multiple records.</exception>
    public TSelf MatchingCall(Action<ILogger> call)
    {
        var captured = LogDefinition.Capture(call);
        AddFilter(LogFilter.MatchingCall(captured));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".MatchingCall({captured})");
        return Self;
    }

    /// <summary>Filters to records whose message satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">A predicate applied to the log message. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf WithMessage(Func<string, bool> predicate)
    {
        AddFilter(LogFilter.WithMessage(predicate));
        Context.ExpressionBuilder.Append(".WithMessage(predicate)");
        return Self;
    }

    /// <summary>
    /// Filters to records whose original message template (the pre-substitution form, e.g.
    /// <c>"Order {OrderId} processed"</c>) equals <paramref name="template"/> exactly (ordinal).
    /// </summary>
    /// <param name="template">The exact message template to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
    public TSelf WithMessageTemplate(string template)
    {
        AddFilter(LogFilter.WithMessageTemplate(template));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithMessageTemplate(\"{template}\")");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is assignable to
    /// <typeparamref name="TException"/>.
    /// </summary>
    /// <typeparam name="TException">The exception type to match.</typeparam>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithException<TException>() where TException : Exception
    {
        AddFilter(LogFilter.WithException<TException>());
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithException<{typeof(TException).Name}>()");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is non-null (any type).
    /// </summary>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithException()
    {
        AddFilter(LogFilter.WithException());
        Context.ExpressionBuilder.Append(".WithException()");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="predicate">A predicate over the (non-null) exception. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf WithException(Func<Exception, bool> predicate)
    {
        AddFilter(LogFilter.WithException(predicate));
        Context.ExpressionBuilder.Append(".WithException(predicate)");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is <see langword="null"/>.
    /// </summary>
    /// <returns>This assertion for chaining.</returns>
    /// <remarks>The complement of <see cref="WithException()"/>. Use when a code path logs at a
    /// warning/error level but deliberately omits the exception object, and the test needs to
    /// assert that no exception was attached.</remarks>
    public TSelf WithoutException()
    {
        AddFilter(LogFilter.WithoutException());
        Context.ExpressionBuilder.Append(".WithoutException()");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is non-null and whose
    /// <see cref="Exception.Message"/> contains <paramref name="substring"/> under the supplied
    /// <paramref name="comparison"/>.
    /// </summary>
    /// <param name="substring">The substring to search for in the exception's message. Must be non-null.</param>
    /// <param name="comparison">The string comparison rules. Project convention: pass explicitly.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf WithExceptionMessage(string substring, StringComparison comparison)
    {
        AddFilter(LogFilter.WithExceptionMessage(substring, comparison));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithExceptionMessage(\"{substring}\", {comparison})");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> wraps an
    /// <see cref="Exception.InnerException"/> assignable to <typeparamref name="TInner"/>. Walks
    /// only one level (does not search deeper inner exceptions). Designed for the gRPC / RPC
    /// pattern where a transport exception (e.g. <c>RpcException</c>) wraps the underlying
    /// domain exception once.
    /// </summary>
    /// <typeparam name="TInner">The inner-exception type to match.</typeparam>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithInnerException<TInner>() where TInner : Exception
    {
        AddFilter(LogFilter.WithInnerException<TInner>());
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithInnerException<{typeof(TInner).Name}>()");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="Exception.InnerException"/>'s
    /// <see cref="Exception.Message"/> contains <paramref name="substring"/> under the supplied
    /// <paramref name="comparison"/>. Walks only one level.
    /// </summary>
    /// <param name="substring">The substring to search for in the inner exception's message. Must be non-null.</param>
    /// <param name="comparison">The string comparison rules. Project convention: pass explicitly.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf WithInnerExceptionMessage(string substring, StringComparison comparison)
    {
        AddFilter(LogFilter.WithInnerExceptionMessage(substring, comparison));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithInnerExceptionMessage(\"{substring}\", {comparison})");
        return Self;
    }

    /// <summary>
    /// Filters to records containing a structured-state entry with the specified
    /// <paramref name="key"/> and <paramref name="value"/> (ordinal string comparison on the
    /// formatted value).
    /// </summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="value">The expected string value (ordinal comparison); may be <see langword="null"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithProperty(string key, string? value)
    {
        AddFilter(LogFilter.WithProperty(key, value));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", \"{value}\")");
        return Self;
    }

    /// <summary>
    /// Filters to records containing a structured-state entry with the specified <paramref name="key"/>
    /// whose formatted string value satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="predicate">A predicate applied to the formatted string value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithProperty(string key, Func<string?, bool> predicate)
    {
        AddFilter(LogFilter.WithProperty(key, predicate));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", predicate)");
        return Self;
    }

    /// <summary>
    /// Filters to records whose structured-state value at <paramref name="key"/> parses to a
    /// <typeparamref name="T"/> equal to <paramref name="value"/> (compared via
    /// <see cref="EqualityComparer{T}.Default"/>).
    /// </summary>
    /// <typeparam name="T">The parsable value type. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="value">The expected typed value.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <remarks>FakeLogRecord stores structured-state values as their formatted strings; this
    /// overload parses the stored string back to <typeparamref name="T"/> using
    /// <see cref="CultureInfo.InvariantCulture"/>, removing the manual <c>int.TryParse(...)</c>
    /// boilerplate at the call site. A value that is absent or does not parse never matches.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithProperty<T>(string key, T value) where T : IParsable<T>
    {
        AddFilter(LogFilter.WithProperty(key, value));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", {value})");
        return Self;
    }

    /// <summary>
    /// Filters to records whose structured-state value at <paramref name="key"/> parses to a
    /// <typeparamref name="T"/> satisfying <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="T">The parsable value type. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="predicate">A predicate over the parsed typed value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <remarks>The stored string is parsed back to <typeparamref name="T"/> using
    /// <see cref="CultureInfo.InvariantCulture"/>; a value that is absent or does not parse never matches.</remarks>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithProperty<T>(string key, Func<T, bool> predicate) where T : IParsable<T>
    {
        AddFilter(LogFilter.WithProperty(key, predicate));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty<{typeof(T).Name}>(\"{key}\", predicate)");
        return Self;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property
    /// with the specified <paramref name="key"/> and <paramref name="value"/> (compared via
    /// <see cref="object.Equals(object?, object?)"/>).
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="value">The expected scope-property value; may be <see langword="null"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key, object? value)
    {
        AddFilter(LogFilter.WithScopeProperty(key, value));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\", {value ?? "null"})");
        return Self;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property with the
    /// specified <paramref name="key"/>, regardless of its value.
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <remarks>Asserts only that the scope key is present, not its value. Use when the value is set
    /// internally and is not known to the test (for example a caller-info scope). Pairs with
    /// <c>HasNotLogged()</c> to assert a scope key was never attached. A scope property whose value is
    /// <see langword="null"/> still counts as present.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key)
    {
        AddFilter(LogFilter.WithScopeProperty(key));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\")");
        return Self;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property
    /// with the specified <paramref name="key"/> whose value satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="predicate">A predicate applied to the scope-property value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key, Func<object?, bool> predicate)
    {
        AddFilter(LogFilter.WithScopeProperty(key, predicate));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\", predicate)");
        return Self;
    }

    /// <summary>
    /// Filters to records whose active scopes contain a property at <paramref name="key"/> whose
    /// value is a <typeparamref name="T"/> equal to <paramref name="value"/> (compared via
    /// <see cref="EqualityComparer{T}.Default"/>).
    /// </summary>
    /// <typeparam name="T">The scope-property value type.</typeparam>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="value">The expected typed value.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <remarks>Scope values keep their runtime type, so this compares typed-to-typed and avoids
    /// the boxing-comparison boilerplate of the <see cref="WithScopeProperty(string, object?)"/>
    /// object overload. A scope value of a different runtime type never matches.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty<T>(string key, T value)
    {
        AddFilter(LogFilter.WithScopeProperty(key, value));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\", {value})");
        return Self;
    }

    /// <summary>
    /// Filters to records whose active scopes contain a property at <paramref name="key"/> whose
    /// value is a <typeparamref name="T"/> satisfying <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="T">The scope-property value type.</typeparam>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="predicate">A predicate over the typed scope value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <remarks>A scope value whose runtime type is not <typeparamref name="T"/> never reaches the predicate.</remarks>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty<T>(string key, Func<T, bool> predicate)
    {
        AddFilter(LogFilter.WithScopeProperty(key, predicate));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty<{typeof(T).Name}>(\"{key}\", predicate)");
        return Self;
    }

    /// <summary>
    /// Filters to records whose active scopes collectively contain every key/value pair in
    /// <paramref name="required"/> (subset match: each pair must match in some scope; different
    /// pairs may match in different scopes).
    /// </summary>
    /// <param name="required">The required scope-property key/value pairs. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="required"/> is <see langword="null"/>.</exception>
    public TSelf WithScopeProperties(IReadOnlyDictionary<string, object?> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        AddFilter(LogFilter.WithScopeProperties(required));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperties({{{required.Count} pairs}})");
        return Self;
    }

    /// <summary>
    /// Filters to records emitted by a logger whose category name equals <paramref name="category"/>
    /// (ordinal comparison).
    /// </summary>
    /// <param name="category">The full category name (typically the logger name) to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public TSelf WithCategory(string category)
    {
        AddFilter(LogFilter.WithCategory(category));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithCategory(\"{category}\")");
        return Self;
    }

    /// <summary>Alias for <see cref="WithCategory(string)"/> using the more colloquial name.</summary>
    /// <param name="loggerName">The full logger name (the category). Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerName"/> is <see langword="null"/>.</exception>
    public TSelf WithLoggerName(string loggerName)
    {
        AddFilter(LogFilter.WithCategory(loggerName));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithLoggerName(\"{loggerName}\")");
        return Self;
    }

    /// <summary>Filters to records whose <see cref="FakeLogRecord.Id"/> ID equals <paramref name="eventId"/>.</summary>
    /// <param name="eventId">The numeric event ID to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithEventId(int eventId)
    {
        AddFilter(LogFilter.WithEventId(eventId));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventId({eventId})");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Id"/> ID is within the inclusive range
    /// <paramref name="min"/>..<paramref name="max"/>.
    /// </summary>
    /// <param name="min">The minimum event ID (inclusive).</param>
    /// <param name="max">The maximum event ID (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public TSelf WithEventIdInRange(int min, int max)
    {
        AddFilter(LogFilter.WithEventIdInRange(min, max));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventIdInRange({min}, {max})");
        return Self;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Id"/> name equals <paramref name="eventName"/> (ordinal).
    /// </summary>
    /// <param name="eventName">The event name (the second argument of <see cref="EventId"/>) to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is <see langword="null"/>.</exception>
    public TSelf WithEventName(string eventName)
    {
        AddFilter(LogFilter.WithEventName(eventName));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventName(\"{eventName}\")");
        return Self;
    }

    /// <summary>
    /// Filters to records emitted while a scope of type <typeparamref name="TScope"/> was active
    /// on the calling logger (matched against <see cref="FakeLogRecord.Scopes"/>).
    /// </summary>
    /// <typeparam name="TScope">The scope state type to match.</typeparam>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithScope<TScope>()
    {
        AddFilter(LogFilter.WithScope<TScope>());
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScope<{typeof(TScope).Name}>()");
        return Self;
    }

    /// <summary>
    /// Escape-hatch filter that applies an arbitrary <paramref name="predicate"/> to each record.
    /// Use only when no other filter expresses the constraint cleanly.
    /// </summary>
    /// <param name="predicate">A predicate applied to each <see cref="FakeLogRecord"/>. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf Where(Func<FakeLogRecord, bool> predicate)
    {
        AddFilter(LogFilter.Where(predicate));
        Context.ExpressionBuilder.Append(".Where(predicate)");
        return Self;
    }

    /// <summary>
    /// Adds a user-supplied <see cref="ILogRecordFilter"/> to the chain. Use this to plug in
    /// composable filter objects built via <see cref="LogFilter"/> factory methods, or
    /// implementations of <see cref="ILogRecordFilter"/> shared across many tests.
    /// </summary>
    /// <param name="filter">The filter. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    public TSelf WithFilter(ILogRecordFilter filter)
    {
        AddFilter(filter);
        Context.ExpressionBuilder.Append(".WithFilter(...)");
        return Self;
    }

    /// <summary>
    /// Adds a disjunction (OR) of <paramref name="filters"/> as a single composite filter on
    /// the chain. The chain itself is AND-combined; this method composes a sub-disjunction
    /// inside that AND, enabling expressions such as
    /// <c>.AtLevel(Warning).MatchingAny(LogFilter.Containing("a", Ordinal), LogFilter.Containing("b", Ordinal))</c>
    /// = <c>level == Warning AND (msg contains "a" OR msg contains "b")</c>.
    /// </summary>
    /// <param name="filters">The disjunction's children. May be empty (matches no record).</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    public TSelf MatchingAny(params ILogRecordFilter[] filters)
    {
        AddFilter(LogFilter.Any(filters));
        Context.ExpressionBuilder.Append(".MatchingAny(...)");
        return Self;
    }

    /// <summary>
    /// Adds a conjunction (AND) of <paramref name="filters"/> as a single composite filter
    /// on the chain. Equivalent to chaining the filters individually but useful when composing
    /// pre-built reusable filters.
    /// </summary>
    /// <param name="filters">The conjunction's children. May be empty (matches every record).</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    public TSelf MatchingAll(params ILogRecordFilter[] filters)
    {
        AddFilter(LogFilter.All(filters));
        Context.ExpressionBuilder.Append(".MatchingAll(...)");
        return Self;
    }

    /// <summary>
    /// Adds the negation of <paramref name="filter"/> to the chain. A record matches when the
    /// inner filter does <em>not</em>.
    /// </summary>
    /// <param name="filter">The filter to negate. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    public TSelf Not(ILogRecordFilter filter)
    {
        AddFilter(LogFilter.Not(filter));
        Context.ExpressionBuilder.Append(".Not(...)");
        return Self;
    }

    /// <summary>Filters to records whose message does not contain <paramref name="substring"/>.</summary>
    /// <param name="substring">The substring that must not appear. Must be non-null.</param>
    /// <param name="comparison">The string comparison.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf NotContaining(string substring, StringComparison comparison)
    {
        AddFilter(LogFilter.Not(LogFilter.Containing(substring, comparison)));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".NotContaining(\"{substring}\", {comparison})");
        return Self;
    }

    /// <summary>Filters to records whose level is not <paramref name="level"/>.</summary>
    /// <param name="level">The log level to exclude.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf NotAtLevel(LogLevel level)
    {
        AddFilter(LogFilter.Not(LogFilter.AtLevel(level)));
        _matchableLevels.Remove(level);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".NotAtLevel({level})");
        return Self;
    }

    /// <summary>Filters to records whose category is not <paramref name="category"/>.</summary>
    /// <param name="category">The category name to exclude. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public TSelf ExcludingCategory(string category)
    {
        AddFilter(LogFilter.Not(LogFilter.WithCategory(category)));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ExcludingCategory(\"{category}\")");
        return Self;
    }

    /// <summary>Filters to records whose level is not <paramref name="level"/> (alias for <see cref="NotAtLevel"/>).</summary>
    /// <param name="level">The log level to exclude.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf ExcludingLevel(LogLevel level) => NotAtLevel(level);

    /// <summary>
    /// Conditionally applies <paramref name="apply"/> to this assertion. When
    /// <paramref name="condition"/> is <see langword="true"/>, runs the configurator and
    /// returns this for chaining; when <see langword="false"/>, returns this unchanged.
    /// Useful in parameterised tests to avoid branching the entire assertion chain on a
    /// boolean: <c>.HasLogged().AtLevel(Warning).When(expectRetry, b =&gt; b.Containing("retry", Ordinal)).AtLeast(1)</c>.
    /// </summary>
    /// <param name="condition">When <see langword="true"/>, applies the configurator.</param>
    /// <param name="apply">The configurator. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="apply"/> is <see langword="null"/>.</exception>
    public TSelf When(bool condition, Action<TSelf> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (condition)
            apply(Self);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".When({condition}, ...)");
        return Self;
    }

    /// <summary>
    /// Counts records in <paramref name="snapshot"/> that satisfy every filter in the chain.
    /// </summary>
    /// <param name="snapshot">The captured records to evaluate.</param>
    /// <returns>The number of matching records.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected int CountMatches(IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Count(r => _filters.Count is 0 || _filters.TrueForAll(f => f.Matches(r)));
    }

    /// <summary>
    /// Returns the matching records from <paramref name="snapshot"/> as a snapshot list (a defensive
    /// copy not bound to the live collector).
    /// </summary>
    /// <param name="snapshot">The captured records to evaluate.</param>
    /// <returns>The matched records in original order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected IReadOnlyList<FakeLogRecord> GetMatches(IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. snapshot.Where(r => _filters.Count is 0 || _filters.TrueForAll(f => f.Matches(r)))];
    }

    /// <summary>
    /// Appends the human-readable filter chain (e.g. <c> matching: Level = Warning, Message contains "x"</c>)
    /// to <paramref name="sb"/>. Emits nothing if no filters have been added.
    /// </summary>
    /// <param name="sb">The target string builder for the expectation message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sb"/> is <see langword="null"/>.</exception>
    protected void AppendFilterSummary(StringBuilder sb)
    {
        ArgumentNullException.ThrowIfNull(sb);
        if (_filters.Count > 0)
        {
            sb.Append(" matching: ")
                .AppendJoin(", ", _filters.Select(f => f.Description));
        }
    }

    /// <summary>
    /// Renders the matching summary plus a snapshot of every captured record (level, category,
    /// message, structured properties, scopes, exception) for use in failure messages.
    /// </summary>
    /// <param name="matchCount">The number of matching records.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <returns>The multi-line failure message body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected static string BuildFailureMessage(int matchCount, IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture, $"{matchCount} record(s) matched")
            .AppendLine()
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"Captured records ({snapshot.Count} total):")
            .AppendLine();

        AppendCapturedRecords(sb, snapshot);
        return sb.ToString();
    }

    /// <summary>
    /// Appends the captured-records section to <paramref name="sb"/>; delegates to
    /// <c>LogAssertionRendering.AppendCapturedRecords</c> so the same rendering is
    /// available to the public <c>FakeLogCollector.DumpTo(...)</c> extension.
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    protected static void AppendCapturedRecords(StringBuilder sb, IReadOnlyList<FakeLogRecord> snapshot)
        => LogAssertionRendering.AppendCapturedRecords(sb, snapshot);
}
