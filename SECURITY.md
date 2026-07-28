# Security Policy

## Reporting a vulnerability

If you believe you have found a security vulnerability in Bump — including any issue affecting authentication, sessions, CSRF, two-factor authentication, password reset, email change, rate limiting, the SSRF guard on monitor probes, or any other security-sensitive surface — please report it privately.

**Email:** [daniel@millerdatabases.com](mailto:daniel@millerdatabases.com)

Please include, where possible:

- A description of the issue and its impact.
- Steps to reproduce, or a proof of concept.
- The affected version, commit, or deployment.
- Any suggested mitigation.

If you would like to encrypt your report, request a PGP key in the first message and one will be provided.

## What to expect

- Acknowledgement within **3 business days**.
- A triage assessment and target remediation timeline within **10 business days**.
- Coordinated disclosure: please give a reasonable window (typically 90 days, or sooner if a fix ships earlier) before publishing details.
- Credit in the release notes for the fix, unless you prefer to remain anonymous.

## Scope

In scope:

- The code in this repository (`Bump.Api`, `Bump.Sdk`, `Bump.Worker`, and the `web/` SPA).
- The HTTP API surface under `/api/**`, including authentication, session, and CSRF behavior.
- The monitor probe path, including SSRF and DNS-rebinding considerations.
- Subscriber confirmation, unsubscribe, and email-change flows.

Out of scope:

- Third-party services Bump integrates with (Mailgun). Report those to their respective vendors.
- Issues that require a pre-compromised host, account, or network position with no realistic path from an external attacker.
- Self-XSS, missing security headers without a demonstrated impact, or volumetric DoS without an amplification primitive.
- Findings produced solely by automated scanners without a working proof of concept.

## Please do not

- Publicly disclose the issue before a fix is available.
- Run automated scans against production deployments you do not own.
- Access, modify, or exfiltrate data that does not belong to you. If you stumble across user data while testing, stop and include that fact in your report.
