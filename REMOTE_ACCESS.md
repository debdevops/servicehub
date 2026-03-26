# ServiceHub — Remote Server Access Guide

This guide explains how to run ServiceHub on a Linux server and access it
from a different machine (laptop, another server, etc.).

---

## How It Works

ServiceHub uses a Vite development proxy to route browser API calls:

```
Your Browser → http://linuxhost:3000/api/v1/...
                       ↓  (Vite proxy, same server)
               http://localhost:5153/api/v1/...  (ASP.NET Core API)
```

By keeping the proxy on the server, the browser never needs to reach port 5153
directly — it just calls port 3000 (the UI port) for everything. This means:

- **No CORS issues** — the browser sees all requests as same-origin (port 3000).
- **Firewall simplicity** — you only need to open one port (3000) to the outside world.
- **No hardcoded IP addresses** in client code.

---

## Quick Start (Remote Server)

### 1. Start ServiceHub

```bash
cd /path/to/servicehub
bash run.sh
```

The startup banner will show all access URLs automatically:

```
🌐 Web UI:
  • http://localhost:3000        ← from this machine
  • http://192.168.1.50:3000     ← from remote machines (by IP)
  • http://linuxhost:3000        ← from remote machines (by hostname)

📍 API Endpoints:
  • HTTP:    http://localhost:5153
  • Remote:  http://192.168.1.50:5153
  • API Docs: http://localhost:5153/scalar/v1
```

### 2. Access from Your Laptop

Open a browser on your laptop and navigate to:

```
http://linuxhost:3000
```

Replace `linuxhost` with the server's hostname or IP address.

---

## Firewall Configuration

If the connection is refused, open the UI port on the server's firewall.
You only need port **3000** (API calls are proxied through it).

**Ubuntu / Debian (ufw):**
```bash
sudo ufw allow 3000/tcp
sudo ufw reload
```

**RHEL / CentOS / Fedora (firewalld):**
```bash
sudo firewall-cmd --add-port=3000/tcp --permanent
sudo firewall-cmd --reload
```

**Quick test** (from your laptop):
```bash
curl http://linuxhost:3000/health
```

---

## Supporting Additional CORS Origins (Advanced)

The Vite proxy eliminates CORS for browser calls. However, if you are making
**direct API calls** from another backend service or tool (e.g. curl, Postman,
another server-side app), you need to tell the .NET API which origins to allow.

Use the `SERVICEHUB_ALLOWED_ORIGINS` environment variable — no config-file edits needed:

```bash
export SERVICEHUB_ALLOWED_ORIGINS="http://192.168.1.50:3000,http://linuxhost:3000"
bash run.sh
```

Multiple origins are comma-separated. This variable is read at startup by
`CorsConfiguration.cs` and merged into the allowed origins list alongside any
values already in `appsettings.json`.

---

## Environment Variable Reference

| Variable | Purpose | Example |
|---|---|---|
| `SERVICEHUB_ALLOWED_ORIGINS` | Extra CORS origins (comma-separated) | `http://192.168.1.50:3000` |
| `VITE_API_BASE_URL` | Override API base URL in the browser client | `http://192.168.1.50:5153/api/v1` |

> **Note:** `VITE_API_BASE_URL` is only needed if you want the browser to call the API
> **directly** (bypassing the proxy). With the default proxy configuration you do
> not need to set this.

---

## Troubleshooting

### "Connection Refused" on port 3000

1. Confirm Vite started with `--host 0.0.0.0`:
   ```bash
   grep "host" /tmp/servicehub_ui_startup.log
   ```
2. Check the firewall rules (see above).
3. Verify the process is listening:
   ```bash
   ss -tlnp | grep 3000
   ```

### "Network Error" / API Can't Be Reached

The browser is talking to the Vite proxy on port 3000, which forwards to the API
on port 5153 (loopback). If you see API errors:

1. Check the API is running:
   ```bash
   curl http://localhost:5153/health
   ```
2. Check API logs:
   ```bash
   tail -50 /tmp/servicehub_api_startup.log
   ```

### CORS Errors in Browser Dev Tools

If you see `Access-Control-Allow-Origin` errors, it means requests are bypassing
the Vite proxy and hitting the API directly. Ensure:

- `VITE_API_BASE_URL` is **not** set to an absolute URL pointing at a different
  host. Clear it or leave it unset to use the proxy.
- The browser is loading the UI from the same origin it's making API calls to.

---

## Production Deployment

For a production Linux deployment (no Vite dev server), build the React app and
serve it from the .NET API's wwwroot folder:

```bash
# Build the React app (outputs to services/api/src/ServiceHub.Api/wwwroot)
cd apps/web
npm run build

# Run the .NET API (it serves both the SPA and the API)
cd services/api
dotnet run --project src/ServiceHub.Api/ServiceHub.Api.csproj \
  --urls "http://0.0.0.0:5153" \
  --environment Production
```

Then open `http://linuxhost:5153` — only one port needed in production.
