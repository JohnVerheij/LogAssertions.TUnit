// Mirrors the GlobalUsings.cs recommendation documented in LogAssertions.TUnit's README.
// The smoke-test project deliberately uses <ImplicitUsings>disable</ImplicitUsings> so a
// failure to wire up these usings — or a future change that breaks the auto-discovery of
// LogAssertions.TUnit's [AssertionExtension]-emitted entry points — surfaces as a build
// failure here rather than silently passing in our own test project (which lives in the
// LogAssertions.TUnit.Tests namespace and gets parent-namespace visibility for free).

global using System;                                // StringComparison
global using System.Threading;                      // CancellationToken
global using System.Threading.Tasks;                // Task
global using LogAssertions;                         // LogCollectorBuilder, LogFilter
global using Microsoft.Extensions.Logging;          // LogLevel, ILogger, ILoggerFactory, LoggerFactory
global using Microsoft.Extensions.Logging.Testing;  // FakeLogCollector, FakeLoggerProvider, FakeLogRecord
