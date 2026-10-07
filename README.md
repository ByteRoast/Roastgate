# Roastgate

A web UI that drives the nginx installed on your server. It runs in a Docker container next to nginx and manages
reverse proxies, hosted sites, IP whitelists and Let's Encrypt certificates, without ever leaving nginx's own files:
your `/etc/nginx` stays the source of truth.

Built with C# / .NET 10, Blazor Server, MudBlazor and SQLite (EF Core).

> Status: phase 1 of 6 (setup, sign-in, host access). Config editing, whitelists, the creation wizard and certificates
> come next.

## What it manages

| Area | Content | Activation |
|---|---|---|
| `conf.d/` | reverse proxies | `.conf` and `.conf.disabled` |
| `sites-available/` | hosted sites | symlink in `sites-enabled/` |
| `whitelists/<name>.conf` | `allow <ip>;` lines | included from your configs |

Every change follows the same cycle: backup, write, `nginx -t`, automatic rollback if invalid, reload if valid.

## Quick start

```bash
git clone https://github.com/ByteRoast/Roastgate.git
cd Roastgate
docker compose up -d --build
docker logs roastgate        # prints the setup token
```

Open <http://127.0.0.1:8181>. The first visit goes to `/setup`:

1. Enter the **setup token** from the logs (or the one you set in `ROASTGATE_SETUP_TOKEN`).
2. Create the **administrator** account (the only account).
3. Choose the **paths and options** (nginx directory, whitelists directory, ACME webroot, certbot mode).
4. Review the **host diagnostics** (host access, nginx, `nginx -t`, directories, certbot).
5. Finish: you are signed in automatically, and `/setup` no longer exists.

Forgot the password?

```bash
docker exec -it roastgate roastgate reset-password
```

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
- Every sign-in, failure and (soon) config change is written to the audit log.
- Two-factor authentication is planned.

## Development

Requires the .NET 10 SDK.

```bash
dotnet build Roastgate.slnx
dotnet test Roastgate.slnx
dotnet run --project src/Roastgate.Web          # http://localhost:5009, host commands run locally
```

On Windows or macOS there is no nginx to drive, so the dashboard reports it as unreachable; everything else works.

The database schema is managed with EF Core migrations:

```bash
dotnet dotnet-ef migrations add <Name> --project src/Roastgate.Core --startup-project src/Roastgate.Web --output-dir Data/Migrations
```

### Conventions (enforced by the build and the tests)

- Everything is written in English, with no em dashes anywhere.
- No `var`: types are always explicit (`IDE0007` and `IDE0008` are errors).
- Routes are lowercase with hyphens (`/audit-log`).
- No ASP.NET Identity: sign-in is a small cookie authentication of our own.

## Layout

```
src/Roastgate.Core/   host execution, SQLite (EF Core), security, setup, diagnostics
src/Roastgate.Web/    Blazor Server UI, authentication, middlewares, CLI
tests/Roastgate.Tests/
```
