# End to end tests

The unit and integration tests fake nginx. This scenario does not: it runs Roastgate next to a **real nginx** in Docker and
drives the real UI in a real browser, then checks the effect on the nginx files and on what nginx actually serves.
It found bugs nothing else could (a UI that never became interactive in the container, an editor with zero height, a lost
Blazor circuit), so run it before releasing.

## Run it

```bash
docker build -t roastgate:test .                              # from the repository root
docker build -f tests/e2e/Dockerfile -t roastgate:e2e tests/e2e
docker rm -f roastgate-e2e
docker run -d --name roastgate-e2e -p 127.0.0.1:8199:8181 roastgate:e2e   # a fresh container: the scenario runs the setup wizard

cd tests/e2e
npm install
npm test
```

| Variable | Default | Purpose |
|---|---|---|
| `E2E_CONTAINER` | `roastgate-e2e` | container to inspect with `docker exec` |
| `E2E_BASE` | `http://127.0.0.1:8199` | where Roastgate listens |
| `E2E_CHANNEL` | `msedge` | installed browser (`msedge`, `chrome`) |
| `E2E_OUT` | `./out` | screenshots are written to `out/screenshots` |

The container uses `ROASTGATE_HOST_EXEC=direct` with nginx installed in the same container, so `nginx -t` and
`nginx -s reload` are the real commands without needing the host namespaces.
