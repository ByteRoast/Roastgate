# Roastgate

A web UI that drives the nginx installed on your server. It runs in a Docker container next to nginx and manages
reverse proxies, hosted sites, IP whitelists and Let's Encrypt certificates, without ever leaving nginx's own files:
your `/etc/nginx` stays the source of truth.

Built with [.NET 10](https://github.com/dotnet/core) and [MudBlazor](https://github.com/MudBlazor/MudBlazor), with the
[Monaco editor](https://github.com/microsoft/monaco-editor)
(through [BlazorMonaco](https://github.com/serdarciplak/BlazorMonaco)) for raw editing.

> **Alpha.** Roastgate is still in its `0.x` alpha releases: settings, the database schema and the behaviour can change
> between
> versions. Keep a backup of the data volume before each update (see [Update](#update)).

## Installation

The full guide is in the [documentation](https://byteroast.fr/docs/roastgate/).  
The essentials are below.

### Requirements

- A Linux server with **nginx installed on the host** (Roastgate drives it, it does not ship one). Debian and Ubuntu
  layouts (`conf.d/`, `sites-available/`, `sites-enabled/`) are supported.
- **Docker** with the Compose plugin, and `root` (or the `docker` group) on that server.
- Optional: `certbot` on the host. Without it, Roastgate uses the one embedded in its image.

### Install

You only need the compose file, not the whole repository. In a folder of your choice:

```bash
mkdir roastgate && cd roastgate
curl -fsSL -o docker-compose.yml https://raw.githubusercontent.com/ByteRoast/Roastgate/refs/heads/main/docker-compose.yml
docker compose up -d
docker logs roastgate        # prints the setup token
```

Or with PowerShell (`pwsh`):

```powershell
New-Item -ItemType Directory roastgate | Set-Location
Invoke-WebRequest -Uri https://raw.githubusercontent.com/ByteRoast/Roastgate/refs/heads/main/docker-compose.yml -OutFile docker-compose.yml
docker compose up -d
docker logs roastgate        # prints the setup token
```

The compose file pulls `ghcr.io/byteroast/roastgate:latest`. The image is published with these tags:

| Tag                       | Follows                                                                                     |
|---------------------------|---------------------------------------------------------------------------------------------|
| `latest`                  | `main`: published when `dev` is merged into it                                              |
| `0.1.0-alpha.1`, `0.1.0`… | a release: published when its `v*` git tag is pushed                                        |
| `0.1`, `1.2`…             | the latest stable release of that minor version (pre-releases such as alphas don't move it) |
| `dev`                     | every push to the `dev` branch (unstable)                                                   |

To stay on one version, replace `latest` with a release tag in `docker-compose.yml`.

To build the image from the sources instead, clone the repository and build it there:

```bash
git clone https://github.com/ByteRoast/Roastgate.git
cd Roastgate
docker compose up -d --build
```

The provided `docker-compose.yml` runs the container as `privileged` with `pid: host` (so it can run `nginx` and
`certbot` in the host
namespaces) and mounts what it needs:

| Host path                                | Purpose                                                       |
|------------------------------------------|---------------------------------------------------------------|
| `/etc/nginx`                             | the nginx configuration Roastgate reads and writes            |
| `/etc/letsencrypt`                       | certificates (read, and written by the embedded certbot)      |
| `/var/www/certbot`                       | ACME webroot used by the Let's Encrypt challenge              |
| `roastgate-data` (named volume, `/data`) | the SQLite database and the keys that encrypt session cookies |

If your nginx or webroot live elsewhere, change the left side of these mounts and enter the same paths during the setup.
To pick the setup token yourself, uncomment `ROASTGATE_SETUP_TOKEN` in the compose file (16+ characters).

### First start

Open <http://127.0.0.1:8181> (from your own machine, `ssh -L 8181:127.0.0.1:8181 user@server` gives you that address).
The first
visit goes to `/setup`, where you enter the **setup token** from the logs (or the one you set in `ROASTGATE_SETUP_TOKEN`), create the **administrator**
account, choose the **paths and options**, and review the **host diagnostics**. Each step is detailed in the
[first start guide](https://byteroast.fr/docs/roastgate/first-start).

To create your first reverse proxy or site, see the [documentation](https://byteroast.fr/docs/roastgate/reverse-proxies-and-sites).

### Development image

`docker-compose.dev.yml` is the same file, pinned on the `dev` image: it follows every push to the `dev` branch, so it
gets the changes first and can break. Download it **as `docker-compose.yml`**, and every command of this README stays
the same:

```bash
curl -fsSL -o docker-compose.yml https://raw.githubusercontent.com/ByteRoast/Roastgate/refs/heads/dev/docker-compose.dev.yml
docker compose pull
docker compose up -d
```

```powershell
Invoke-WebRequest -Uri https://raw.githubusercontent.com/ByteRoast/Roastgate/refs/heads/dev/docker-compose.dev.yml -OutFile docker-compose.yml
docker compose pull
docker compose up -d
```

Both files use the same container and the same `roastgate-data` volume, so switching replaces the running container
and keeps your account. Going back from `dev` to `latest` is not guaranteed to work: `dev` may already have upgraded the
database to a schema that `latest` does not know. Back up the volume (see [Update](#update)) before you try `dev`.

### Update

From the folder that holds `docker-compose.yml`:

```bash
docker compose pull
docker compose up -d
```

Download the compose file again first (same `curl` or `Invoke-WebRequest` command as in [Install](#install)) only when
the release notes say it changed. It overwrites your copy, so redo your own edits afterwards (ports, mounts, setup
token…). If you built from the sources: `git pull`, then `docker compose up -d --build`.

The database lives in the `roastgate-data` volume, so your account and backups survive the rebuild and
`docker compose down`.
Only `docker compose down --volumes` (or `docker volume rm roastgate-data`) deletes it. To back it up:

```bash
docker run --rm -v roastgate-data:/data -v "$PWD":/backup alpine tar czf /backup/roastgate-data.tgz -C /data .
```

### Forgot the password?

```bash
docker exec -it roastgate roastgate reset-password
```

### Uninstall

```bash
docker compose down            # keeps the data volume
docker compose down --volumes  # also deletes the account, settings and backup history
```

Then delete the folder that holds `docker-compose.yml`, and the image with `docker image rm ghcr.io/byteroast/roastgate:latest` (or the tag you used).
Your nginx files stay where they are: Roastgate never moves them.

## What it manages

| Area                     | Content             | Activation                   |
|--------------------------|---------------------|------------------------------|
| `conf.d/`                | reverse proxies     | `.conf` and `.conf.disabled` |
| `sites-available/`       | hosted sites        | symlink in `sites-enabled/`  |
| `whitelists/<name>.conf` | `allow <ip>;` lines | included from your configs   |

Every change follows the same cycle: backup, write, `nginx -t`, automatic rollback if invalid, reload if valid. Writes
are atomic (temporary file, then rename) and keep the original file permissions.

## Features

- **Reverse proxies and sites**: enable or disable with one switch, edit in a Monaco editor with nginx syntax
  highlighting, or import a hand-written config into the wizard.
  [Read more](https://byteroast.fr/docs/roastgate/reverse-proxies-and-sites)
- **Creation wizard**: describe a proxy or a static site in a form (WebSockets, HTTPS, HTTP/2, HSTS, upload limit,
  security headers, whitelists...) and preview exactly what will be written. Every value is validated, and the form
  can be reopened from the generated file.
- **Whitelists**: strict IPv4, IPv6 and CIDR validation, and the list of configs that use each one. Visitors outside a
  whitelist get a 403 by default; the wizard can answer a 404, close the connection (444) or redirect (302) instead.
  [Read more](https://byteroast.fr/docs/roastgate/whitelists)
- **Certificates**: get, renew and delete Let's Encrypt certificates, with the embedded certbot or the one of the host,
  and a dry run before issuing. [Read more](https://byteroast.fr/docs/roastgate/certificates)
- **Backups and restore**: the 30 most recent versions of each file are kept, can be compared line by line and
  restored. [Read more](https://byteroast.fr/docs/roastgate/backups-and-audit-log)
- **Account security**: two-factor authentication (TOTP), passkeys, and rate-limited sign-in.
  [Read more](https://byteroast.fr/docs/roastgate/account-security)
- **Settings**: the paths and options chosen at setup, and the password. [Read more](https://byteroast.fr/docs/roastgate/settings-and-configuration)

## Configuration

The container is configured with optional environment variables (`ROASTGATE_SETUP_TOKEN`, `ROASTGATE_SECURE_COOKIES`,
`ROASTGATE_TRUST_FORWARDED_HEADERS`...). See the [full list](https://byteroast.fr/docs/roastgate/settings-and-configuration#environment-variables).


## Security notes

Roastgate controls your web server, and its container is privileged with `pid: host`. Whoever controls Roastgate is,
in practice, root on the host. Treat it accordingly:

- Keep the port on `127.0.0.1` and reach it through an SSH tunnel, a VPN, or an nginx server block protected by a
  whitelist and HTTPS.
- Use a long, unique administrator password. Sign-in is rate limited (5 failures per 15 minutes per client address).
- The session cookie is `HttpOnly` and `SameSite=Strict`; set `ROASTGATE_SECURE_COOKIES=true` when served over HTTPS.
- **Changing the password ends every open session**, on every device (sessions carry a security stamp that is renewed
  with the password,
  and open browser tabs are rechecked every 30 seconds). `roastgate reset-password` does the same, so it is also the
  answer to a stolen session.
- Every sign-in, failure and config change is written to the audit log.
- The `/data` volume holds the database **and the keys that encrypt session cookies**. On Linux those keys are stored
  unencrypted, so keep `/data` readable by root only.
- Turn on [two-factor authentication and passkeys](https://byteroast.fr/docs/roastgate/account-security) in the settings.


## Development

Requires the .NET 10 SDK.

```bash
dotnet build Roastgate.slnx
dotnet test Roastgate.slnx
dotnet run --project src/Roastgate.Web          # http://localhost:5009, host commands run locally
```

On Windows or macOS there is no nginx to drive, so the dashboard reports it as unreachable; everything else works.

Every build of `Roastgate.Web` first runs `scripts/check-razor-imports.sh` (Linux/macOS) or
`scripts/check-razor-imports.ps1` (Windows).
It fails the build when a `@using` is already provided by an `_Imports.razor` of the same or a parent folder, by the
implicit usings,
or is repeated in the same file. Each feature folder keeps its own `_Imports.razor`, so same-named types don't collide.
Run it by hand
with `scripts/check-razor-imports.sh src/Roastgate.Web`.

The tests that fake nginx live in `tests/Roastgate.Tests`. A second suite drives the real UI against a real nginx in
Docker, see
[tests/e2e](tests/e2e/README.md).

### Trying it against a real nginx

`dotnet run` drives the nginx of your own machine, if there is one. To try your changes against a real nginx without
touching it, run
the end-to-end image: Roastgate built from your working copy, with nginx installed **in the same container**
(`ROASTGATE_HOST_EXEC=direct`, so `nginx -t` and `nginx -s reload` are the real commands) and a fake certbot.

```bash
docker build -t roastgate:test .                                  # from the repository root, rebuild after each change
docker build -f tests/e2e/Dockerfile -t roastgate:e2e tests/e2e
docker rm -f roastgate-dev
docker run -d --name roastgate-dev \
  -p 127.0.0.1:8282:8181 \
  -p 127.0.0.1:8280:80 \
  roastgate:e2e
docker logs roastgate-dev                                         # prints the setup token
```

- <http://127.0.0.1:8282> is Roastgate. Keep the default paths during the setup: they exist in the container.
- <http://127.0.0.1:8280> is what nginx serves, so you can check the effect of a proxy or a site you just created.
- `docker exec roastgate-dev nginx -T` prints the whole configuration nginx loaded; `docker exec -it roastgate-dev bash`
  opens a shell.
- The container starts empty every time. Add `-v roastgate-dev-data:/data` to the `docker run` to keep the account
  between runs.
- The certbot inside is a stand-in that cannot reach Let's Encrypt: certificates go through the same commands and files,
  but are not real.
- The ports differ from the end-to-end suite (`8199`), so both can run at the same time.

The database schema is managed with EF Core migrations:

```bash
dotnet dotnet-ef migrations add <Name> --project src/Roastgate.Core --startup-project src/Roastgate.Web --output-dir Data/Migrations
```

## License

Copyright (C) 2026 Byte Roast

Roastgate is free software: you can redistribute it and/or modify it under the terms of the
[GNU Affero General Public License](LICENSE) as published by the Free Software Foundation, either version 3 of the
License,
or (at your option) any later version (`AGPL-3.0-or-later`).

Roastgate is distributed in the hope that it will be useful, but **without any warranty**; without even the implied
warranty of
merchantability or fitness for a particular purpose. See the [LICENSE](LICENSE) file for the full terms.

In short: you can use, study, change and share it freely. If you distribute a modified version, **or let other people
use a
modified version over a network**, you must offer them its source code under the same license.
