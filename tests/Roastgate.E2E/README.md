# E2E tests

The unit and integration tests fake nginx. This scenario does not: it runs Roastgate next to a **real nginx** in Docker
and drives the real UI in a real browser, then checks the effect on the nginx files and on what nginx actually serves.
It found bugs nothing else could (a UI that never became interactive in the container, an editor with zero height, a
lost Blazor circuit), so run it before releasing.

## Requirements

- Docker
- Node 22.18 or later: it runs the TypeScript files as they are, with Node's type stripping, so there is no build step
- pnpm
- An installed browser: Edge or Chrome

## Run it

```bash
cd tests/Roastgate.E2E
pnpm install
pnpm e2e:build          # builds the application image, then the test image; again after each change to the application
pnpm test:fresh:chrome  # or test:fresh:edge
```

The scenario goes through the setup wizard, so it needs a **fresh container** every time: the `test:fresh*` scripts
replace the container, wait for its setup token, then run the scenario. `pnpm test` alone runs it against the container
that is already there.

Screenshots of every step are written to `out/screenshots`, numbered in the order they are taken.

### Scripts

| Script              | What it does                                                                  |
| ------------------- | ----------------------------------------------------------------------------- |
| `e2e:build`         | builds `roastgate:test` from the repository root, then `roastgate:e2e` on top |
| `e2e:up`            | replaces the `roastgate-e2e` container with a fresh one                       |
| `e2e:wait`          | waits (60 s at most) until the container has written its setup token          |
| `test`              | runs the scenario against the current container                               |
| `test:fresh`        | `e2e:up`, `e2e:wait`, then `test` with the browser from `E2E_CHANNEL`         |
| `test:fresh:chrome` | the same with Chrome                                                          |
| `test:fresh:edge`   | the same with Edge                                                            |
| `typecheck`         | checks the types with `tsc` (Node runs the files without checking them)       |
| `format`            | formats the files with Prettier                                               |
| `format:check`      | fails if a file is not formatted                                              |

### Variables

| Variable        | Default                 | Purpose                                      |
| --------------- | ----------------------- | -------------------------------------------- |
| `E2E_CONTAINER` | `roastgate-e2e`         | container to inspect with `docker exec`      |
| `E2E_BASE`      | `http://127.0.0.1:8199` | where Roastgate listens                      |
| `E2E_CHANNEL`   | `msedge`                | installed browser (`msedge`, `chrome`)       |
| `E2E_OUT`       | `./out`                 | screenshots are written to `out/screenshots` |

The `e2e:*` scripts use the default container name and port. `test:fresh:chrome` and `test:fresh:edge` set
`E2E_CHANNEL` themselves.

The container uses `ROASTGATE_HOST_EXEC=direct` with nginx installed in the same container, so `nginx -t` and
`nginx -s reload` are the real commands without needing the host namespaces.

## Layout

```
tests/Roastgate.E2E/
  src/
    e2e.ts             entry point: launches the browser, runs the sections in order, prints the summary
    lib/
      config.ts        the E2E_* variables and the passwords used by the scenario
      context.ts       what every section receives: the browser, the page, the page helpers, the setup token
      page.ts          helpers bound to the page (waitText, button, box, shot, the Monaco editor...)
      report.ts        check(), the browser problems and trace, the final summary
      shell.ts         commands in the container (sh, httpGet, writeHostFile...) and the setup token
      totp.ts          an authenticator app's TOTP, written independently of the server
    sections/          one function per part of the scenario
    globals.d.ts       the few parts of Monaco the scenario reaches through window.monaco
  Dockerfile           the test image: Roastgate with nginx, the fancyindex module and fake-certbot.sh
  scripts/
    fake-certbot.sh    a stand-in for certbot that keeps its command line and file layout but never reaches Let's Encrypt
  out/                 screenshots of the last run (not committed)
```

It is **one scenario, not independent tests**: each section relies on what the previous ones left on the server
(127.0.0.1 in the whitelist for the wizard's HTTPS, files to back up and restore, the password changed in the settings
for two-factor...). Keep the order of the `sections` array in `src/e2e.ts`.

### Adding a check or a section

- A check is a `check('what is true', condition, detail)` call: its name is what the run prints, so write it as a
  sentence.
- A section is an `async ({ helpers, page, ... }: Context): Promise<void>` function in `src/sections/`, added to
  the `sections` array in `src/e2e.ts` where its place in the scenario is.
- Take screenshots with `helpers.shot('name')`, without a number: `shot` numbers them itself.
- Run `pnpm typecheck` and `pnpm format` before committing.

## In the IDE

`Roastgate.E2E.esproj` adds the suite to `Roastgate.slnx`, under `tests`. Building the solution does not run pnpm, and
the project's test command is `pnpm test:fresh`.

On Linux the JavaScript SDK writes a folder literally named `obj\Debug` (the backslash is part of the name), which is
why `.gitignore` also has `obj\\*`.
