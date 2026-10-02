# Run ServiceHub on your own machine

> **In this article:** two complete, separate tracks — **A. Without Docker** (from source) and **B. With Docker** — each with install, start, verify,
> stop, data, settings and troubleshooting. Pick one track and follow it top to bottom. About ten minutes the first time.
>
> **In plain language:** ServiceHub is one program with one small database file. You do not need a database server or any cloud account to look
> around — there is a built-in demo with made-up data.

| | **A. Without Docker** (`./run.sh`) | **B. With Docker** (`docker compose`) |
|---|---|---|
| Best for | Trying it, changing it, developing | Running it for real on one machine |
| You need | .NET 10 SDK + Node.js + git | Docker + git |
| Opens at | <http://localhost:3000> | <http://localhost:8080> |
| First start | A minute or two (it compiles) | Several minutes (it builds the image) |
| Mode | `Development` (throw-away encryption key) | `Production` (your own encryption key, required) |
| Stop with | Ctrl-C | Ctrl-C, or `docker compose down` |
| Data lives in | `services/api/src/ServiceHub.Api/data/` | The `servicehub-data` Docker volume (`/data`) |

Putting it on a server for a team? That is the [Azure hosting guide](HOSTING-AZURE.md).

**Operating systems.** macOS and Linux work directly. On **Windows use WSL 2** (Ubuntu) and run everything inside it — `run.sh` is a bash script.
For track A install the tools *inside* WSL, not on the Windows side. For track B, Docker Desktop with its WSL 2 integration enabled works.

---

# A. Without Docker

## 1. What you need

| Tool | Version | Check with | Get it |
|---|---|---|---|
| **.NET SDK** | 10.0 or newer (`services/api/global.json` pins 10.0.x) | `dotnet --version` | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **Node.js** | 22 LTS recommended (20 is the minimum) | `node --version` | <https://nodejs.org> |
| **npm** | Comes with Node | `npm --version` | — |
| **git** | Any | `git --version` | <https://git-scm.com> |
| `lsof`, `curl` | Any | `lsof -v`, `curl --version` | Present on macOS; on Debian/Ubuntu `sudo apt install lsof curl` |

**Ports.** `5153` (API) and `3000` (web). Both can be changed — see [A5](#a5-settings-you-can-change).

Optional: `./run.sh --check` (after cloning, below) verifies .NET, Node and the ports and starts nothing.

## 2. Start it

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
./run.sh
```

`run.sh` checks your tools, installs the web dependencies the first time (and again after a `git pull` that changed them), starts the API and the
web server, waits until both answer, and then tells you:

```
✔ ServiceHub is ready
   Open http://localhost:3000      (try /demo/azure first: made-up data, nothing is sent anywhere)
   Ctrl-C to stop.
```

The first start compiles the API and can take a minute or two; later starts take a few seconds.

Other modes:

```bash
./run.sh --check        # verify your machine, start nothing
./run.sh --api-only     # just the API on :5153
./run.sh --web-only     # just the web server (expects an API already running)
./run.sh --help
```

If a previous ServiceHub from this folder is still running on the port, `run.sh` stops *that* one and carries on. If something *else* holds the
port it refuses and tells you which process — it never kills a program it does not recognise.

## 3. Check it works

1. **Health.** <http://localhost:3000/health/ready> should say `Healthy`. (`/health` only says the process is up; `/health/ready` also proves the
   database answers.) From a terminal: `curl http://localhost:3000/health/ready`.
2. **Demo.** Open <http://localhost:3000/demo/azure>. Everything works on made-up data; nothing is sent anywhere.
3. **Your own cloud.** See [Connect a cloud](#connect-a-cloud) below.

## 4. Stop it, and where your data lives

**Ctrl-C stops everything `run.sh` started** — nothing is left running in the background.

Everything ServiceHub remembers — the clouds you connected, the replay history, the tamper-evident ledger — is **one SQLite file**, `servicehub.db`,
in `services/api/src/ServiceHub.Api/data/` (git-ignored), or wherever `SERVICEHUB_DATA_DIR` points. Next to it are `-wal` / `-shm` files (SQLite's
write-ahead log — normal, never delete them while ServiceHub runs), `.instance.lock`, and `backups/`.

**One instance per data directory.** A second ServiceHub on the same directory exits at once with *"Another ServiceHub instance already holds the data
directory"*. That is deliberate: two writers would corrupt the file.

**Start over** by stopping ServiceHub and deleting the data directory (you will have to connect your clouds again).

**The development encryption key.** Cloud credentials are encrypted before they are stored. `./run.sh` runs as `Development`, which supplies a
**throw-away key from `appsettings.Development.json`**. That is fine for trying things out and **wrong for anything you care about**: the key is public in
the repository. Do not connect production credentials to a `./run.sh` instance, and never copy its database to a server. For real use, run track B.

## A5. Settings you can change

Environment variables, set on the same line:

```bash
SERVICEHUB_API_PORT=5200 SERVICEHUB_WEB_PORT=3300 ./run.sh
SERVICEHUB_DATA_DIR="$HOME/servicehub-data" ./run.sh
```

| Variable | Default | Meaning |
|---|---|---|
| `SERVICEHUB_API_PORT` | `5153` | Port the API listens on |
| `SERVICEHUB_WEB_PORT` | `3000` | Port you open in the browser |
| `SERVICEHUB_DATA_DIR` | `services/api/src/ServiceHub.Api/data` | Where `servicehub.db` lives |
| `SERVICEHUB_READY_TIMEOUT` | `180` | Seconds to wait for startup before giving up |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Leave it. `Production` refuses to start without an encryption key |

To run a **second, separate copy** (say, to try a branch beside your usual one), give it its own ports *and* its own `SERVICEHUB_DATA_DIR`.

## A6. Run the tests

```bash
./runtest.sh --quick      # backend unit tests only — fastest
./runtest.sh              # backend + frontend
./runtest.sh --all        # everything CI runs
```

See [tests/README.md](../tests/README.md) and [CONTRIBUTING.md](../CONTRIBUTING.md).

## A7. Troubleshooting (without Docker)

| You see | Why | Do this |
|---|---|---|
| `✖ The .NET SDK is not installed` / `does not satisfy services/api/global.json` | No .NET 10 SDK | Install .NET 10 (link above). `dotnet --list-sdks` shows what you have |
| `✖ Node … is too old` | Node below 20 | Install Node 22 LTS (a version manager such as `nvm` or `fnm` makes this painless) |
| `✖ Port 5153 is held by something else (pid …)` | Another program, not a previous ServiceHub, uses the port | Stop it, or `SERVICEHUB_API_PORT=5200 ./run.sh` |
| `Another ServiceHub instance already holds the data directory` | A ServiceHub (maybe a forgotten one, or the Docker container) is using the same data folder | Stop it, or give this one its own `SERVICEHUB_DATA_DIR` |
| `The database at '…' is not a ServiceHub 4.1.0 database` | The folder holds a 4.0.0 (or other) database. **There is no upgrade path from 4.0.0** | Point `SERVICEHUB_DATA_DIR` at an empty folder |
| `✖ Not ready after 180s` | The API did not come up | Read the error just above it; for a slow machine raise `SERVICEHUB_READY_TIMEOUT` |
| Page loads but requests fail (proxy errors in the terminal), especially just after start | The web server is up but the API is not — still compiling, or you used `--web-only` | Wait for `✔ ServiceHub is ready`; or start the API too with `./run.sh` |
| `npm ci` fails | Half-installed `node_modules` | `rm -rf node_modules && ./run.sh` |
| `Security:EncryptionKey … not configured`, API exits | Started as `Production` without a key | Use plain `./run.sh` (Development), or set `SECURITY__ENCRYPTIONKEY` |

Still stuck? Run `./run.sh --check` and open an issue with its output and the last 30 lines of the terminal.

---

# B. With Docker

## B1. What you need

| Tool | Check with | Get it |
|---|---|---|
| **Docker** with the Compose plugin (`docker compose`, v2) | `docker --version`, `docker compose version` | <https://docs.docker.com/get-docker/> |
| **git** | `git --version` | <https://git-scm.com> |
| `openssl`, `curl` | `openssl version`, `curl --version` | Present on macOS and most Linux |

You do **not** need .NET or Node — the image build does that work inside Docker. Make sure the Docker daemon is running (`docker info` answers).

**Port.** `8080`, published on `127.0.0.1` only (this machine). To use another host port, edit the `ports:` line in `docker-compose.yml`.

## B2. Make an encryption key (once)

ServiceHub **refuses to start in Production without an encryption key**: it protects every cloud connection string it stores.

```bash
export SERVICEHUB_ENCRYPTION_KEY="$(openssl rand -hex 32)"
echo "$SERVICEHUB_ENCRYPTION_KEY"      # copy this into a password manager NOW
```

Use the **same key every time** you start it — a different key cannot read credentials stored with the old one, and losing it makes them unreadable.
Keep it in a password manager or secret store, not in a file in the repository. In every new terminal, export it again before `docker compose` (or put it
in your shell profile or a git-ignored `.env` file next to `docker-compose.yml`, which Compose reads automatically).

## B3. Start it

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
docker compose up --build            # foreground; add -d to run in the background
```

The first build takes several minutes (it compiles the API and builds the web app); later starts take seconds. When the logs settle, open
<http://localhost:8080>.

## B4. Check it works

```bash
curl http://localhost:8080/health/ready      # → Healthy
docker compose ps                            # → servicehub is "healthy" after about a minute
```

Then open <http://localhost:8080/demo/azure> for made-up data, and see [Connect a cloud](#connect-a-cloud) below.

## B5. Stop, restart, update, and where your data lives

| To | Run |
|---|---|
| Stop (foreground) | Ctrl-C |
| Stop a background copy | `docker compose down` — **your data stays** in the volume |
| Start again | `docker compose up -d` (same key exported) |
| See logs | `docker compose logs -f servicehub` |
| Update to newer code | `git pull && docker compose up -d --build` |
| Delete everything, data included | `docker compose down -v` — **irreversible**; you will connect your clouds again |

Everything ServiceHub remembers is **one SQLite file** in the `servicehub-data` Docker volume, mounted at `/data` in the container. Back it up from
Settings → Backup ([Backup & restore](BACKUP-RESTORE.md)). One instance per data directory: do not mount the same volume into two containers.

**Do not copy a `./run.sh` (track A) database into the volume.** It was encrypted with the public development key, not yours.

**Security.** ServiceHub does not log the browser in: whoever can reach the port is the owner (an Administrator). Compose publishes `127.0.0.1:8080`
only. To expose it, put an authenticating reverse proxy or a private network in front — see [Azure hosting](HOSTING-AZURE.md) and [SECURITY.md](../SECURITY.md).

## B6. Settings you can change

Set these under `environment:` in `docker-compose.yml`, then `docker compose up -d`:

| Setting | Default | Meaning |
|---|---|---|
| `SECURITY__ENCRYPTIONKEY` | from `SERVICEHUB_ENCRYPTION_KEY` (required) | The key that protects stored credentials; see [Encryption key rotation](ENCRYPTION-KEY-ROTATION.md) |
| `ServiceHub__DataDirectory` | `/data` | Set in the image; change it only together with the volume mount |
| `ports:` | `127.0.0.1:8080:8080` | Host address and port you open in the browser |

## B7. Troubleshooting (with Docker)

| You see | Why | Do this |
|---|---|---|
| `set SERVICEHUB_ENCRYPTION_KEY first` | The variable is not exported in this shell | Run the `export` line from B2, then `docker compose up --build` |
| `Cannot connect to the Docker daemon` | Docker is not running | Start Docker Desktop (or `sudo systemctl start docker`) |
| `docker: 'compose' is not a docker command` | Old Docker without the Compose v2 plugin | Install the Compose plugin or a current Docker |
| `port is already allocated` / `address already in use` on 8080 | Something else (or an old container) uses 8080 | `docker compose down`, or change the host port in `ports:` |
| Container restarts in a loop; logs say the encryption key is missing or wrong | Key not exported, or a different key than the one that made the data | Export the original key. If it is truly lost, the stored credentials are unrecoverable: `docker compose down -v` and start over |
| `Another ServiceHub instance already holds the data directory` | Two containers share the volume | Stop the other one |
| `The database … is not a ServiceHub 4.1.0 database` | The volume holds a 4.0.0 database. **There is no upgrade path from 4.0.0** | Use a fresh volume (`docker compose down -v`, or rename the volume) |
| Build fails or is very slow | Not enough disk or memory for Docker | Free space (`docker system prune`), give Docker Desktop at least 4 GB RAM |
| Page does not load, but `docker compose ps` says healthy | Wrong address | Use <http://localhost:8080> (not 3000 — that is track A) |

Still stuck? Open an issue with `docker compose ps` and the last 50 lines of `docker compose logs servicehub`.

---

# Connect a cloud

Both tracks end the same way:

1. In the sidebar choose **Add a cloud**, then follow the guide for yours:
   [Azure Service Bus](clouds/azure.md) · [AWS SQS/SNS](clouds/aws.md) · [Google Pub/Sub](clouds/gcp.md).
2. Give it only the permissions you need: read/receive to look at dead letters; send and delete only if you want to replay and purge.
3. If Home shows nothing after adding a cloud, the credential may lack permission, or there are simply no dead letters — re-read the permissions
   section of that cloud's guide.

> **Security on your own machine.** ServiceHub does not log the browser in: whoever can reach the port is the owner (an Administrator). Both tracks
> listen on `localhost` only, so that means you. Do not forward these ports or bind them to `0.0.0.0` on a shared network.
