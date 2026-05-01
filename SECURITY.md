# Security Policy

## Supported versions

Only the latest published version of `LogAssertions.TUnit` receives fixes. Earlier versions are not supported.

| Version | Supported |
|---------|-----------|
| latest  | ✅        |
| older   | ❌        |

## Reporting a vulnerability

If you discover a security vulnerability, **please do not open a public GitHub issue.** Instead, report it privately via [GitHub's private security reporting](https://github.com/JohnVerheij/LogAssertions.TUnit/security/advisories/new).

Reports are acknowledged within seven days. After a fix is prepared, a coordinated disclosure timeline is agreed with the reporter before public release.

## Scope

This package is a TUnit-targeting test-only library. Realistic attack surface is small: it consumes `Microsoft.Extensions.Logging.Testing.FakeLogCollector` data and renders it into TUnit assertion failure messages. Issues that may qualify:

- Unbounded memory or CPU consumption from a crafted `FakeLogCollector` snapshot
- Information disclosure through assertion failure messages that escapes intended scope
- Supply-chain concerns about the package itself

Issues that do not qualify:

- Bugs in dependent packages (TUnit, Microsoft.Extensions.Diagnostics.Testing) — report those upstream
- Issues in test-runner integration that are TUnit-side
