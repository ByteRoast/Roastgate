# Contributing to Roastgate

Thanks for your interest in Roastgate! Bug reports, ideas, documentation fixes and code are all welcome.

Roastgate is in alpha and maintained on a best-effort basis: answers may take a few days.

## Before you start

- **Found a bug?** Open a [bug report](https://github.com/ByteRoast/Roastgate/issues/new/choose). Remove any secret
  (passwords, tokens, private domains or IP addresses) from what you paste.
- **Found a vulnerability?** Do not open an issue: follow the [security policy](SECURITY.md).
- **Want a feature?** Open a feature request first. For anything bigger than a small fix, wait for a reply before
  writing code, so your work is not lost on something that does not fit the project.

## Branches

| Branch | Role                                                                         |
|--------|------------------------------------------------------------------------------|
| `dev`  | where work happens; published as the `ghcr.io/byteroast/roastgate:dev` image |
| `main` | the stable branch; `dev` is merged into it for each release                  |

Fork the repository, create a branch from **`dev`**, and open your pull request **against `dev`**, not `main`.

## Setting up

You need the .NET 10 SDK, and Docker to try your changes against a real nginx.

```bash
dotnet build Roastgate.slnx
dotnet test Roastgate.slnx
dotnet run --project src/Roastgate.Web          # http://localhost:5009
```

The [Development](README.md#development) section of the README explains how to run Roastgate against a real nginx in a
container, and [tests/e2e](tests/e2e/README.md) how to run the end-to-end suite.

## Code style

The build enforces most of the rules, so a green `dotnet build` already covers them:

- **No `var`**: every type is spelled out (target-typed `new()` is allowed). See `.editorconfig`.
- **Nullable reference types** are on, and nullable warnings are errors.
- **No redundant `@using`** in `.razor` files: each build runs `scripts/check-razor-imports.sh` (or the `.ps1` on
  Windows). Each feature folder keeps its own `_Imports.razor` on purpose, to keep same-named types from colliding:
  add a namespace to the `_Imports.razor` of the folder that needs it, not to the root one.
- UTF-8, LF line endings, 4-space indentation (2 for JSON, YAML and project files).

The build does not enforce these, but please follow them:

- **One top-level type per file, named after the type** (`FancyIndexSort.cs` holds `FancyIndexSort`). Enums, records,
  interfaces and other models get their own file (`ISiteWizardService.cs` next to `SiteWizardService.cs`). Nested types
  stay where they are.

Beyond that, write code that looks like the code around it.

## Tests

- Add or update tests in `tests/Roastgate.Tests` for every behaviour change. These tests fake nginx and must pass with
  `dotnet test`.
- If your change touches the UI or what is written to nginx, also run the [end-to-end suite](tests/e2e/README.md),
  which drives the real UI against a real nginx.
- A change to the database schema comes with its EF Core migration (the command is in the README).

## Commits

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add HTTP/3 to the reverse proxy form
fix(certificates): keep the webroot when renewing
docs(readme): explain the passkey requirements
```

Common types are `feat`, `fix`, `docs`, `refactor`, `test`, `ci` and `chore`. Keep each commit focused on one change.

## Pull requests

The pull request template lists what is expected. In short:

- describe what changes and why, and link the issue (`Closes #123`);
- `dotnet build` and `dotnet test` pass;
- the README is updated if the behaviour, the configuration or the installation changed.

## License

Roastgate is licensed under the [GNU Affero General Public License v3.0 or later](LICENSE). By contributing, you agree
that your contribution is released under the same license.

### Contributor license agreement

So that the project can keep evolving, including under other licensing terms, every contribution is also covered by
the following agreement. Opening a pull request, and ticking the matching box of its template, means you accept it.

1. **You keep your copyright.** Your contribution stays yours; this agreement only grants rights, it does not transfer
   them.
2. **License grant.** You grant ByteRoast and the recipients of the software a perpetual, worldwide, non-exclusive,
   royalty-free and irrevocable license to use, reproduce, modify, distribute, sublicense and publicly perform your
   contribution, and to release it under the AGPL-3.0-or-later or **any other license**, open source or not.
3. **Patent grant.** You grant the same parties a perpetual, worldwide, non-exclusive, royalty-free and irrevocable
   license under any patent you hold that your contribution would otherwise infringe, to make, use, sell and
   distribute it as part of Roastgate.
4. **You have the right to contribute.** The contribution is your own original work, or you have the right to submit
   it under these terms. If your employer has rights over what you create, you have their permission.
5. **Third-party code.** If your contribution includes work that is not yours, you say so in the pull request, with its
   origin and license.
6. **No warranty.** Your contribution is provided "as is", without warranty of any kind, and you are not expected to
   support it.
