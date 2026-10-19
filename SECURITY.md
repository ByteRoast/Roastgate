# Security policy

## Supported versions

Roastgate is in alpha. Only the most recent release and the `dev` image receive security fixes; older releases are not
patched; update to the latest one instead.

| Version                 | Supported |
|-------------------------|-----------|
| Latest release          | yes       |
| `dev`                   | yes       |
| Any older release       | no        |

## Reporting a vulnerability

**Please do not open a public issue, discussion or pull request for a vulnerability.**

Report it privately through GitHub instead: go to the
[Security tab](https://github.com/ByteRoast/Roastgate/security) and click **Report a vulnerability**, or open
[the form](https://github.com/ByteRoast/Roastgate/security/advisories/new) directly.

A useful report contains:

- the Roastgate version (image tag) and how it is installed;
- what an attacker can do, and what they need first (network access to the port, a signed-in session...);
- the steps to reproduce it, or a proof of concept;
- any idea you have of a fix.

## What to expect

Roastgate is maintained on a best-effort basis. We aim to:

1. acknowledge the report within 7 days;
2. confirm or dismiss it, and agree with you on a disclosure date;
3. publish a fixed release and a GitHub security advisory, crediting you unless you prefer otherwise.

Please keep the details private until the advisory is published.

## Scope

Roastgate runs as a `privileged` container with `pid: host` so it can drive the nginx of the host: anyone who controls
Roastgate controls the server. Everything that lets someone get past the sign-in, the two-factor authentication or the
passkeys, run commands, or read or write files outside what Roastgate is meant to manage is in scope.

These are not considered vulnerabilities on their own:

- exposing the port to the internet against the advice of the [Security notes](README.md#security-notes);
- an attacker who already has root access to the host or to the Docker socket;
- issues in nginx, certbot or Docker themselves (report them to their projects).
