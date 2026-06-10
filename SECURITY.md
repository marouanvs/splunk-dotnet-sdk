# Security Policy

This is an unofficial Splunk SDK. It is not affiliated with, endorsed by, or
supported by Splunk Inc. Vulnerabilities in Splunk products themselves should
be reported to Splunk, not to this repository.

## Reporting A Vulnerability

Report vulnerabilities in this SDK privately through GitHub security
advisories:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability** to create a private advisory draft.

Do not open public issues or pull requests for security reports, and never
include Splunk tokens, session keys, private hostnames, or real search
results in a report — use redacted placeholders.

If you cannot use GitHub security advisories, open a minimal public issue
that only asks for a private contact channel, without any vulnerability
details.

## Supported Versions

| Version              | Supported                                          |
| -------------------- | -------------------------------------------------- |
| Latest `0.x` release | Yes — fixes ship in the next release               |
| Older `0.x` releases | No — upgrade to the latest release before reporting |

Until `1.0.0`, only the most recent release receives security fixes.

## Response Expectations

- Acknowledgement of a private report within 7 days.
- Triage outcome and severity assessment within 14 days.
- Confirmed vulnerabilities are fixed in the next release; the changelog and
  the advisory describe the impact without disclosing exploit details until
  a fixed version is available.
- Coordinated disclosure is preferred: please allow a fix to be released
  before publishing details.

## Scope Notes

- The SDK treats Splunk tokens as credentials: they stay behind
  `ISplunkTokenProvider`, are never logged, and never appear in exceptions
  or telemetry. Reports that show a token, raw SPL, full URL, search ID, or
  event payload leaking into logs, exceptions, activity tags, or metric
  attributes are in scope and welcome.
- Certificate-validation bypass is intentionally restricted to the
  dependency-injection package and documented for disposable local labs
  only; reports that show it reachable in production paths are in scope.
