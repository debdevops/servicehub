# Run ServiceHub with Docker

> **In this article:** pull the published image, start it, open the demo, connect a cloud, then stop, update, back up and remove it. About five
> minutes. You do not need .NET, Node.js or a cloud account to look around.
>
> **In plain language:** ServiceHub ships as one ready-made Docker image. You download it, start it with one command, and open it in your browser.
> Everything it remembers lives in one small database file in a Docker volume.

| | |
|---|---|
| **Image** | `ghcr.io/debdevops/servicehub:latest` (public, no sign-in to pull) |
| **Platforms** | `linux/amd64` and `linux/arm64` (Intel/AMD machines and Apple silicon; Docker picks the right one) |
| **Opens at** | <http://localhost:8080> |
| **Data** | The `servicehub-data` Docker volume, mounted at `/data` |
| **Runs as** | A non-root user, in `Production` mode |
| **Licence** | MIT |

Prefer to build it yourself, or to run it without Docker? See [Run ServiceHub on your own machine](LOCAL-SETUP.md).

## Before you begin

| You need | Check with | Get it |
|---|---|---|
| **Docker** (Docker Desktop on macOS and Windows, Docker Engine on Linux) | `docker --version` and `docker info` (it must answer, so the daemon is running) | <https://docs.docker.com/get-docker/> |
| **A bash-style shell** (the commands use `export` and `\` line continuations) | `bash --version` | macOS Terminal and Linux have one. On **Windows**, use a WSL 2 terminal (Ubuntu) with Docker Desktop's WSL 2 integration turned on; plain PowerShell and Command Prompt will not run the commands as written |
| **OpenSSL**, to make the random key | `openssl version` | Included on macOS and most Linux, and in Ubuntu on WSL 2. Any other way to make 64 random hex characters also works |

## Step 1. Make an encryption key (once)

ServiceHub encrypts the cloud credentials it stores, and it **refuses to start in Production without a key**. Make one and keep it.

```bash
export SERVICEHUB_ENCRYPTION_KEY="$(openssl rand -hex 32)"
echo "$SERVICEHUB_ENCRYPTION_KEY"        # copy it into your password manager now
```

> **Keep this key.** It must be the same every time you start ServiceHub on the same data. If you lose it, the cloud connections you saved
> become unreadable and you connect your clouds again. Keep it in a password manager or secret store, not in a file in a Git repository.

## Step 2. Download and start ServiceHub

```bash
docker run -d --name servicehub --restart unless-stopped \
  -p 127.0.0.1:8080:8080 -v servicehub-data:/data \
  -e SECURITY__ENCRYPTIONKEY="$SERVICEHUB_ENCRYPTION_KEY" \
  ghcr.io/debdevops/servicehub:latest
```

`docker run` downloads the image the first time (about 230 MB on disk). To download it without starting it, run `docker pull ghcr.io/debdevops/servicehub:latest`.

What each part does:

| Part | Meaning |
|---|---|
| `-d --name servicehub` | Run in the background, as a container named `servicehub` |
| `--restart unless-stopped` | Start again after a reboot, until you stop it yourself |
| `-p 127.0.0.1:8080:8080` | Publish port 8080 on **this machine only**. Do not remove `127.0.0.1` (see [Security](#security)) |
| `-v servicehub-data:/data` | Keep the database in a Docker volume, so it survives updates and restarts |
| `-e SECURITY__ENCRYPTIONKEY=…` | The key from Step 1 |

## Step 3. Check that it is running

```bash
docker ps --filter name=servicehub        # STATUS reads "healthy" after about 20-60 seconds
curl -s http://localhost:8080/health      # → Healthy
```

> `/health` answers on any host name, even one ServiceHub would refuse for everything else (see [Security](#security)). A passing health check proves the
> container is running, not that the address you reach it by is allowed. Check that by opening the app itself.

## Step 4. Open it

Go to **<http://localhost:8080>**. You will see the Welcome page.

- **Try it with sample data** opens a demo on made-up data. Nothing is connected and nothing is sent anywhere. Open a dead letter, read why it failed,
  and press **Replay** to see the preview. Choose **Leave the demo** in the purple bar when you are done.
- **Connect Azure / AWS / Google** connects a real cloud. Each cloud has a step-by-step guide:
  [Azure](clouds/azure.md) · [AWS](clouds/aws.md) · [Google Cloud](clouds/gcp.md).

## Daily use

| You want to | Run |
|---|---|
| See the logs | `docker logs -f servicehub` |
| Stop it (your data stays) | `docker stop servicehub` |
| Start it again | `docker start servicehub` |
| See its health | `docker ps --filter name=servicehub` |
| Use another port | Change the first `8080` in `-p`, for example `-p 127.0.0.1:9090:8080`, then open <http://localhost:9090> |

## Update to a newer version

Your data is in the volume, so it carries over to a newer **4.2.x** image. Use the same key.

```bash
docker pull ghcr.io/debdevops/servicehub:latest          # or an exact tag, e.g. :4.2.1
docker stop servicehub && docker rm servicehub           # removes the container, NOT the volume
# then run the Step 2 command again with the new tag, and the same SERVICEHUB_ENCRYPTION_KEY
```

> **Moving from 4.0.0 is different.** 4.1.0 and later are a from-scratch rewrite: they cannot open a 4.0.0 database and there is no upgrade path. Run 4.2.0 beside
> 4.0.0 with a different **container name**, port and volume, in case 4.0.0 already uses `servicehub` and 8080
> (for example `--name servicehub-41 -p 127.0.0.1:8081:8080 -v servicehub-data-41:/data`; use `servicehub-41` in place of `servicehub` in the commands on this page).
> Connect your clouds again and retire 4.0.0 when you are ready. See the [changelog](../CHANGELOG.md).

Tags: `latest` is what the commands above use. `4.2.0` is one exact release, `4.2` follows the newest 4.2.x patch, and `latest` follows the newest release overall. Pin an exact version for
anything you depend on.

## Back up your data

Everything ServiceHub remembers is **one SQLite file** in the volume. Take a backup from **Settings → Backup** inside the app. It is a consistent copy
that can be verified and restored: see [Backup and restore](BACKUP-RESTORE.md). Keep your encryption key with the backup, because a backup without its
key cannot decrypt the saved cloud connections.

## Use Docker Compose instead

From a clone of the repository, Compose builds the image for you and reads the key from your shell. See [Run ServiceHub on your own machine, track B](LOCAL-SETUP.md).

## Remove it completely

```bash
docker stop servicehub && docker rm servicehub
docker volume rm servicehub-data        # IRREVERSIBLE: deletes the database, the ledger and the saved cloud connections
docker image rm ghcr.io/debdevops/servicehub:latest
```

## Security

- **ServiceHub does not sign anyone in.** Whoever can reach its port is the server's owner, an Administrator. That is why the command publishes the port on
  `127.0.0.1`: only this machine can reach it. Do not publish it on `0.0.0.0` or a public address.
- **It answers only to `localhost`, `127.0.0.1` and `[::1]`.** Reach it by any other name (a proxy, a host name) and it answers `400` until you list that name,
  keeping `localhost` in the list: add `-e "AllowedHosts=localhost;your.host.name"`. Put an authenticating reverse proxy or a private network in front before
  you do. The one exception is `/health` and `/health/ready`, which answer on any name so load balancers and Docker's health check can probe it; a green
  probe through a proxy therefore does not prove `AllowedHosts` is set correctly. See [Host ServiceHub on Azure](HOSTING-AZURE.md) and [SECURITY.md](../SECURITY.md).
- **It stores part of each message.** For every dead letter it sees, ServiceHub keeps the first 500 characters of the body, the message properties and a hash
  in its database, in plain text. Cloud credentials and webhook URLs are encrypted; message content is not. Protect the volume and its backups like the messages.
- **One instance per volume.** Never mount the same volume into two containers; a second one exits on purpose.

## Troubleshooting

| What you see | Why | What to do |
|---|---|---|
| `Cannot connect to the Docker daemon` | Docker is not running | Start Docker Desktop (or `sudo systemctl start docker`) |
| `pull access denied` or `unauthorized` | The name or tag is misspelled, or an old sign-in is interfering | Check `ghcr.io/debdevops/servicehub:latest`; run `docker logout ghcr.io` and pull again. The image is public |
| Container exits at once; `docker logs servicehub` says `Neither Security:EncryptionKeyRegistry nor Security:EncryptionKey is configured` | The key was empty when you ran the command | Run Step 1 again in the **same terminal**, then Step 2 |
| `port is already allocated` or `address already in use` | Something else uses 8080 (or an old container) | `docker rm -f servicehub`, or choose another host port |
| Page shows `400` and `This address is not one ServiceHub answers to` | You opened it by a name other than `localhost`, `127.0.0.1` or `[::1]` | Use <http://localhost:8080>, or set `AllowedHosts` as in [Security](#security) |
| Container restarts in a loop; logs say the key is missing or wrong | A different key than the one that made the data | Start it with the original key. If it is truly lost, the saved connections are unrecoverable: remove the volume and start again |
| `Another ServiceHub instance already holds the data directory` | Two containers share the volume | Stop the other one |
| `The database … is not a ServiceHub 4.1.0 database` | The volume holds a 4.0.0 database. There is no upgrade path | Use a fresh volume name, for example `-v servicehub-data-41:/data` |
| Status stays `starting` for a minute | First start runs the database setup | Wait; check `docker logs -f servicehub` |

Still stuck? [Open an issue](https://github.com/debdevops/servicehub/issues/new/choose) with the output of `docker ps` and the last 50 lines of `docker logs servicehub`.
