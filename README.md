# Roastgate

A web UI that drives the nginx installed on your server. It runs in a Docker container next to nginx and manages
reverse proxies, hosted sites, IP whitelists and Let's Encrypt certificates, without ever leaving nginx's own files:
your `/etc/nginx` stays the source of truth.

Built with [.NET 10](https://github.com/dotnet/core) and [MudBlazor](https://github.com/MudBlazor/MudBlazor), with the
[Monaco editor](https://github.com/microsoft/monaco-editor) (through [BlazorMonaco](https://github.com/serdarciplak/BlazorMonaco)) for raw editing.

## Installation

### Requirements

- A Linux server with **nginx installed on the host** (Roastgate drives it, it does not ship one). Debian and Ubuntu layouts
  (`conf.d/`, `sites-available/`, `sites-enabled/`) are supported.
- **Docker** with the Compose plugin, and `root` (or the `docker` group) on that server.
- Optional: `certbot` on the host. Without it, Roastgate uses the one embedded in its image.

### Install

```bash
git clone https://github.com/ByteRoast/Roastgate.git
cd Roastgate
docker compose up -d --build
docker logs roastgate        # prints the setup token
```

The provided `docker-compose.yml` runs the container as `privileged` with `pid: host` (so it can run `nginx` and `certbot` in the host
namespaces) and mounts what it needs:

| Host path | Purpose |
|---|---|
| `/etc/nginx` | the nginx configuration Roastgate reads and writes |
| `/etc/letsencrypt` | certificates (read, and written by the embedded certbot) |
| `/var/www/certbot` | ACME webroot used by the Let's Encrypt challenge |
| `roastgate-data` (named volume, `/data`) | the SQLite database and the keys that encrypt session cookies |

If your nginx or webroot live elsewhere, change the left side of these mounts and enter the same paths during the setup.
To pick the setup token yourself, uncomment `ROASTGATE_SETUP_TOKEN` in the compose file (16+ characters).

### First start

Open <http://127.0.0.1:8181> (from your own machine, `ssh -L 8181:127.0.0.1:8181 user@server` gives you that address). The first
visit goes to `/setup`:

1. Enter the **setup token** from the logs (or the one you set in `ROASTGATE_SETUP_TOKEN`).
2. Create the **administrator** account (the only account).
3. Choose the **paths and options** (nginx directory, whitelists directory, ACME webroot, certbot mode).
4. Review the **host diagnostics** (host access, nginx, `nginx -t`, directories, certbot).
5. Finish: you are signed in automatically, and `/setup` no longer exists.

### Update

```bash
cd Roastgate
git pull
docker compose up -d --build
```

The database lives in the `roastgate-data` volume, so your account and backups survive the rebuild and `docker compose down`.
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

Your nginx files stay where they are: Roastgate never moves them.

## What it manages

| Area | Content | Activation |
|---|---|---|
| `conf.d/` | reverse proxies | `.conf` and `.conf.disabled` |
| `sites-available/` | hosted sites | symlink in `sites-enabled/` |
| `whitelists/<name>.conf` | `allow <ip>;` lines | included from your configs |

Every change follows the same cycle: backup, write, `nginx -t`, automatic rollback if invalid, reload if valid.

- Edit any file in a Monaco editor with nginx syntax highlighting; the file name is part of the URL as a query string
  (`/reverse-proxies/edit?name=app.conf`), so every route stays lowercase and hyphenated.
- Enable or disable a proxy or a site with one switch. Roastgate only touches symlinks that point at the site itself, and
  understands both relative and absolute links (the Debian default site uses an absolute one).
- Every overwrite and deletion keeps a backup in the database (the 30 most recent per file).
- If `nginx -t` already fails for a reason unrelated to your change, the change is undone and you are told so.
- Writes are atomic (temporary file, then rename) and keep the original file permissions.

### Importing a hand-written config

Open a file that the wizard did not create in the wizard (the "import into the wizard" link of the raw editor, or `/reverse-proxies/wizard?name=app.conf`) and its
settings are **read from the file**: names, ports, upstream, certificate, whitelists, upload limit, WebSocket headers, timeouts, redirect.
Everything the form has no field for (other locations, `gzip`, `proxy_buffering`...) is kept as free directives in the Advanced tab, so
nothing is lost. Saving regenerates the file (a backup is kept), so check the Preview tab first.

A file is **refused, with the reason**, when the wizard could not reproduce it faithfully: a top-level `upstream` or `map`, a `listen` bound to an
address, `default_server`, a regex `server_name`, a different `proxy_set_header Host`, an HTTP server that does more than redirect, more than two server
blocks. Those stay editable as raw text. Comments between directives are not kept, and the wizard lists the defaults it adds (for example the
`X-Forwarded-*` headers) when your file did not have them.

### Two-factor authentication

In **Settings**, scan the QR code with any authenticator app ([Google Authenticator](https://support.google.com/accounts/answer/1066447), [Aegis](https://github.com/beemdevelopment/Aegis), [1Password](https://1password.com), [Bitwarden](https://bitwarden.com)...) and confirm with a code. You get ten
**recovery codes**, shown once (each signs you in once; they are stored hashed). Sign-in then has two steps, and the second step only appears after a correct password.

- Codes follow RFC 6238 (SHA-1, 30 s, 6 digits). One step of clock drift is tolerated; a code that was used cannot be used again (the check is atomic in the database).
- Five wrong codes end the attempt, and a client address is locked out after five failures, whatever the password.
- Lost the phone and the codes? From the host: `docker exec roastgate roastgate disable-2fa`.

### Settings

The **Settings** page edits the paths and options chosen at setup (validated the same way), checks the host with the values you typed
before you rely on them, and changes the password (current password required, with a throttle against guessing).

### Backups and restore

Every overwrite and every deletion keeps the previous content (the 30 most recent per file). The **Backups** page (also reachable
from the "history" link of each file) lists them, compares any version with the current file line by line (what restoring would add
and remove), and restores it. A restore goes through the same backup, `nginx -t`, rollback and reload cycle as a save. A change that
nginx refused leaves no backup behind, since it would only repeat the current file.

### Creation wizard

Describe a reverse proxy or a static site in a form (essentials, HTTPS, access, advanced) and Roastgate writes the nginx config:
WebSockets, HTTPS with an optional redirect, HTTP/2, HSTS, upload limit, timeouts, security headers, whitelists, the Let's Encrypt
challenge, and free directives for anything else. A preview tab shows exactly what will be written.

- **Safe by construction.** Every value is validated against a strict pattern before it reaches the file (domains, URLs, paths, sizes),
  and the free directive boxes must keep their braces balanced so they cannot leave their block.
- **Aware of your nginx.** HTTP/2 is written as `http2 on;` from nginx 1.25.1 and as `listen ... ssl http2;` before (the first form
  makes older versions fail). IPv6 listeners can be turned off for hosts without IPv6.
- **Reopenable.** The settings are stored in a comment at the top of the generated file, with a hash of the body. Reopening the file
  restores the form. If you edited the file by hand, the wizard tells you and asks before it would replace your changes; files
  the wizard did not create are never replaced silently.
- The proxy destination path must end with a slash (`http://backend/app/`): without it nginx glues the path to the request.

### Certificates (Let's Encrypt)

Get, list, renew and delete certificates from the **Certificates** page.

- **Two ways to run certbot**, chosen at setup: the **embedded** one (shipped in the image, it works on the `/etc/letsencrypt` you mount,
  and Roastgate renews twice a day by itself), or the one **installed on the host** (driven through `nsenter`, renewed by the host's
  own timer). Never both renewing at once: the setup diagnostics warn you if certbot also exists on the host.
- **Certificates are read from the files** certbot maintains, not from its text output: names, domains, issuer, expiry, staging or not,
  and which of your configs use each one.
- **Renewal reloads nginx** only when a certificate really changed (the file is compared before and after), so nginx never keeps
  serving an old one. A failed reload is reported, not hidden.
- **Dry run first**: validates your domains against Let's Encrypt's staging server and issues nothing.
- A certificate that a config still uses **cannot be deleted**. Every value passed to certbot is validated, and it never goes through a shell.
- The wizard lists your certificates and fills in the paths, and warns when the chosen certificate does not cover the domains.

For a validation to work, nginx must answer on port 80 for the domain and serve the ACME webroot under `/.well-known/acme-challenge/`:
tick "Let the Let's Encrypt validation through" in the wizard (it keeps working behind a whitelist). Wildcard certificates need a DNS
challenge and are not supported.

### Whitelists

- A list editor (address and comment per line) with live validation of IPv4, IPv6 and CIDR ranges. Validation is strict:
  `1.2`, `0x7f.0.0.1` or `10.0.0.5/24` (host bits set, which nginx silently ignores) are flagged instead of being passed on.
- Paste many addresses at once, or add the address you are connecting from with one click.
- Each whitelist shows **where it is used** (the `include` lines of your proxies and sites, relative or absolute paths,
  quoted or not, wildcard includes), and gives you the `include` and `deny all;` lines to copy.
- A whitelist that is still included **cannot be deleted**. `nginx -t` remains the last safety net for includes in files
  Roastgate does not read (such as `nginx.conf`).
- A file that holds anything other than `allow` lines (a `deny`, a comment in the middle...) is never rewritten by the list
  editor: Roastgate opens it as raw text instead, so nothing you wrote is lost.

Remember that nginx evaluates `return` before `allow` and `deny`: a `location` that only does `return 200;` ignores the whitelist.

## Configuration

Environment variables, all optional:

| Variable | Default | Purpose |
|---|---|---|
| `ROASTGATE_DATA_DIR` | `/data` | where `roastgate.db` lives (mount a volume here) |
| `ROASTGATE_HOST_EXEC` | `nsenter` | `nsenter` runs commands in the host namespaces, `direct` runs them locally (development) |
| `ROASTGATE_SETUP_TOKEN` | random at each start | fixed setup token, 16+ characters |
| `ROASTGATE_SECURE_COOKIES` | `false` | mark the session cookie `Secure` (enable behind HTTPS) |
| `ROASTGATE_TRUST_FORWARDED_HEADERS` | `false` | trust `X-Forwarded-For` / `-Proto` (enable only behind your own reverse proxy) |

## Security notes

Roastgate controls your web server, and its container is privileged with `pid: host`. Whoever controls Roastgate is,
in practice, root on the host. Treat it accordingly:

- Keep the port on `127.0.0.1` and reach it through an SSH tunnel, a VPN, or an nginx server block protected by a whitelist and HTTPS.
- Use a long, unique administrator password. Sign-in is rate limited (5 failures per 15 minutes per client address).
- The session cookie is `HttpOnly` and `SameSite=Strict`; set `ROASTGATE_SECURE_COOKIES=true` when served over HTTPS.
- **Changing the password ends every open session**, on every device (sessions carry a security stamp that is renewed with the password,
  and open browser tabs are rechecked every 30 seconds). `roastgate reset-password` does the same, so it is also the answer to a stolen session.
- Every sign-in, failure and (soon) config change is written to the audit log.
- The `/data` volume holds the database **and the keys that encrypt session cookies**. On Linux those keys are stored unencrypted, so keep `/data` readable by root only.
- **Two-factor authentication** (TOTP, any authenticator app) is available in the settings: a code is asked after the password, a code can be used only once, and
  wrong codes are throttled separately from passwords. Turning it on or off ends every other session.

## Development

Requires the .NET 10 SDK.

```bash
dotnet build Roastgate.slnx
dotnet test Roastgate.slnx
dotnet run --project src/Roastgate.Web          # http://localhost:5009, host commands run locally
```

On Windows or macOS there is no nginx to drive, so the dashboard reports it as unreachable; everything else works.

The tests that fake nginx live in `tests/Roastgate.Tests`. A second suite drives the real UI against a real nginx in Docker, see
[tests/e2e](tests/e2e/README.md).

The database schema is managed with EF Core migrations:

```bash
dotnet dotnet-ef migrations add <Name> --project src/Roastgate.Core --startup-project src/Roastgate.Web --output-dir Data/Migrations
```
