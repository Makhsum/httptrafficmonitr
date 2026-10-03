# Security Policy

HTTP Traffic Monitor installs a root certificate into the Windows trust store, changes the system proxy and reads HTTPS traffic in plaintext. A weakness in it can expose everything a machine sends, so please report one privately and give us the chance to fix it before it becomes public.

## Supported versions

The project has not tagged a release yet. Security fixes go into the `main` branch and into the next release; once releases exist, only the latest one receives fixes.

## Reporting a vulnerability

**Do not report a vulnerability in a public issue, pull request or discussion.**

Report it privately through GitHub instead:

1. Open the repository's [**Security** tab](https://github.com/Makhsum/httptrafficmonitr/security).
2. Click **Report a vulnerability** — or go straight to the [private report form](https://github.com/Makhsum/httptrafficmonitr/security/advisories/new).
3. Describe the problem. Only you and the maintainer can see the report.

Please include:

- what an attacker can do and what they need for it (for example: another process on the same machine, a malicious website, a crafted response)
- the steps or a proof of concept that reproduces it
- the app version — the release you downloaded or the commit you built — and your Windows version

If the form is not available to you, open an issue that **only** asks for a private way to get in touch, with no details about the problem, and the maintainer will reply with one.

## What happens next

- We aim to answer within 7 days, to confirm the report or ask for more information.
- We agree with you on a fix and a date to publish it, and keep you informed while it is worked on.
- Once the fix is released, the advisory is published and credits you, unless you would rather stay anonymous.

## Scope

In scope is the code in this repository: the desktop app, its local API on `localhost:18081` and the MCP server — for example a way to reach the local API from another machine or a website, the root CA's private key leaking, or the proxy settings not being restored.

Out of scope are vulnerabilities in third-party packages that are already publicly known (please still tell us if we use an affected version), and attacks that need an administrator account on the machine already.
