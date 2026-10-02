# Run ServiceHub on your own machine

> **In this article:** install the tools, start ServiceHub with one command, check it is really working, connect your first cloud, and fix the
> things that most often go wrong. About ten minutes the first time.
>
> **In plain language:** ServiceHub is one program with one small database file. You do not need Docker, a database server, or any cloud account to
> look around — there is a built-in demo with made-up data.

Two ways to run it. Pick one:

| | **From source** (`./run.sh`) | **Docker** (`docker compose`) |
|---|---|---|
| Best for | Trying it, changing it, developing | Running it for real on one machine |
| You need | .NET 10 SDK + Node.js | Docker only |
| Opens at | <http://localhost:3000> | <http://localhost:8080> |
| First start | A minute or two (it compiles) | Several minutes (it builds the image) |

Putting it on a server for a team? That is the [Azure hosting guide](HOSTING-AZURE.md).

---

## 1. What you need

| Tool | Version | Check with | Get it |
|---|---|---|---|
| **.NET SDK** | 10.0 or newer (`services/api/global.json` pins 10.0.x) | `dotnet --version` | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **Node.js** | 22 LTS recommended (20 is the minimum) | `node --version` | <https://nodejs.org> |
| **npm** | Comes with Node | `npm --version` | — |
| **git** | Any | `git --version` | <https://git-scm.com> |
| `lsof`, `curl` | Any | `lsof -v`, `curl --version` | Present on macOS; on Debian/Ubuntu `sudo apt install lsof curl` |

**Operating systems.** macOS and Linux work directly. On **Windows use WSL 2** (Ubuntu) and run everything inside it — `run.sh` is a bash script. Install
the tools *inside* WSL, not on the Windows side.

**Ports.** `5153` (API) and `3000` (web). Both can be changed — see [Settings you can change](#5-settings-you-can-change).

---

## 2. Start it

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
./run.sh
```

That is all. `run.sh` checks your tools, installs the web dependencies the first time (and again after a `git pull` that changed them), starts the
API and the web server, waits until both answer, and then tells you:

```
✔ ServiceHub is ready
   Open http://localhost:3000      (try /demo/azure first: made-up data, nothing is sent anywhere)
   Ctrl-C to stop.
```

Open that address. **Ctrl-C stops everything it started** — nothing is left running in the background.

The first start compiles the API and can take a minute or two; later starts take a few seconds.

### Check your machine first (optional)

```bash
./run.sh --check
```

Verifies .NET and Node and reports whether the ports are free. Starts nothing.

### Other modes

```bash
./run.sh --api-only     # just the API on :5153
./run.sh --web-only     # just the web server (expects an API already running)
./run.sh --help
```

If a previous ServiceHub from this folder is still running on the port, `run.sh` stops *that* one and carries on. If something *else* holds the
port it refuses and tells you which process — it never kills a program it does not recognise.

---

## 3. Look around, then connect a cloud

1. **Try the demo.** Open <http://localhost:3000/demo/azure>. Everything works on made-up data; nothing is sent anywhere.
2. **Check it is healthy.** <http://localhost:3000/health/ready> should say `Healthy`. (`/health` only says the process is up; `/health/ready` also
   proves the database answers.)
3. **Connect your own cloud.** In the sidebar choose **Add a cloud**, then follow the guide for yours:
   [Azure Service Bus](clouds/azure.md) · [AWS SQS/SNS](clouds/aws.md) · [Google Pub/Sub](clouds/gcp.md).
   Give it only the permissions you need: read/receive to look at dead letters; send and delete only if you want to replay and purge.

> **Security on your own machine.** ServiceHub does not log the browser in: whoever can reach the port is the owner (an Administrator). `run.sh`
> listens on `localhost` only, so that means you. Do not forward these ports or bind them to `0.0.0.0` on a shared network.

---

## 4. Where your data lives

Everything ServiceHub remembers — the clouds you connected, the replay history, the tamper-evident ledger — is **one SQLite file**, `servicehub.db`.

| Run mode | Data directory |
|---|---|
| `./run.sh` | `services/api/src/ServiceHub.Api/data/` (git-ignored) |
| `./run.sh` with `SERVICEHUB_DATA_DIR=/some/path` | That path |
| Docker Compose | The `servicehub-data` Docker volume, mounted at `/data` |

Next to it you will find `-wal` / `-shm` files (SQLite's write-ahead log — normal, never delete them while ServiceHub runs), `.instance.lock`, and
`backups/`.

**One instance per data directory.** A second ServiceHub on the same directory exits at once with *"Another ServiceHub instance already holds the data
directory"*. That is deliberate: two writers would corrupt the file.

**Back up** from Settings → Backup ([Backup & restore](BACKUP-RESTORE.md)). **Start over** by stopping ServiceHub and deleting the data directory (you
will have to connect your clouds again).

### The development encryption key

Cloud credentials are encrypted before they are stored. `./run.sh` runs as `Development`, which supplies a **throw-away key from
`appsettings.Development.json`** so you do not have to make one. That is fine for trying things out and **wrong for anything you care about**: the key
is public in the repository. Do not connect production credentials to a `./run.sh` instance, and never copy its database to a server. For real use
set your own key — see [Docker](#docker-on-your-machine) below and [Encryption key rotation](ENCRYPTION-KEY-ROTATION.md).

---

## 5. Settings you can change

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

---

## Docker, on your machine

For a copy that behaves like production (production mode, your own key, one container):

```bash
export SERVICEHUB_ENCRYPTION_KEY="$(openssl rand -hex 32)"   # once — and keep it somewhere safe
docker compose up --build                                       # → http://localhost:8080
```

Use the *same* key every time you start it — a different key cannot read credentials stored with the old one. Put the line in a password manager, not in
a file in the repository. Stop with Ctrl-C (or `docker compose down`; your data stays in the volume). Compose publishes the port on `127.0.0.1` only.

Check it: `curl http://localhost:8080/health/ready` → `Healthy`.

---

## 6. Run the tests

```bash
./runtest.sh --quick      # backend unit tests only — fastest
./runtest.sh              # backend + frontend
./runtest.sh --all        # everything CI runs
```

See [tests/README.md](../tests/README.md) and [CONTRIBUTING.md](../CONTRIBUTING.md).

---

## Troubleshooting

| You see | Why | Do this |
|---|---|---|
| `✖ The .NET SDK is not installed` / `does not satisfy services/api/global.json` | No .NET 10 SDK | Install .NET 10 (link above). `dotnet --list-sdks` shows what you have |
| `✖ Node … is too old` | Node below 20 | Install Node 22 LTS (a version manager such as `nvm` or `fnm` makes this painless) |
| `✖ Port 5153 is held by something else (pid …)` | Another program, not a previous ServiceHub, uses the port | Stop it, or `SERVICEHUB_API_PORT=5200 ./run.sh` |
| `Another ServiceHub instance already holds the data directory` | A ServiceHub (maybe a forgotten one, or Docker) is using the same data folder | Stop it, or give this one `SERVICEHUB_DATA_DIR` |
| `The database at '…' is not a ServiceHub 4.1.0 database` | The folder holds a 4.0.0 (or other) database. **There is no upgrade path from 4.0.0** | Point `SERVICEHUB_DATA_DIR` at an empty folder |
| `✖ Not ready after 180s` | The API did not come up | Read the error just above it; for a slow machine raise `SERVICEHUB_READY_TIMEOUT` |
| Page loads but requests fail (proxy errors in the terminal), especially just after start | The web server is up but the API is not — still compiling, or you used `--web-only` | Wait for `✔ ServiceHub is ready`; or start the API too with `./run.sh` |
| `npm ci` fails | Half-installed `node_modules` | `rm -rf node_modules && ./run.sh` |
| `Security:EncryptionKey … not configured`, API exits | Started as `Production` without a key | Use plain `./run.sh` (Development), or set `SECURITY__ENCRYPTIONKEY` |
| Docker: `set SERVICEHUB_ENCRYPTION_KEY first` | The variable is not exported in this shell | Run the `export` line above, then `docker compose up --build` |
| Added a cloud but Home shows nothing | The credential may lack permission, or there are no dead letters | Re-read the permissions section of that cloud's guide |

Still stuck? Run `./run.sh --check` and open an issue with its output and the last 30 lines of the terminal.
