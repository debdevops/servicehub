# syntax=docker/dockerfile:1
# ServiceHub — single self-contained image serving the React SPA and the ASP.NET Core API
# from one process (the SPA is copied into the API's wwwroot and served as static files).
#
# Build:  docker build -t servicehub:local .
# Run:    docker run --rm -p 8080:8080 servicehub:local           # bring your own credentials
# Demo:   docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Simulator servicehub:local
#
# Then open http://localhost:8080

# ---- Stage 1: build the React SPA ------------------------------------------------------
FROM node:22-bookworm-slim AS web
# Mirror the repo layout so vite.config.ts can resolve ../../.version (repo root) at build time.
WORKDIR /repo

# Install dependencies first for better layer caching.
COPY apps/web/package.json apps/web/package-lock.json ./apps/web/
RUN cd apps/web && npm ci --include=optional

# The .version file (read by vite.config.ts) must sit at the repo root relative to apps/web.
COPY .version ./.version
COPY apps/web/ ./apps/web/

# Build the SPA. Vite's configured outDir points into the API's wwwroot; override it to a
# local dist so this stage is self-contained and the output is easy to copy forward.
RUN cd apps/web && npm run build -- --outDir dist --emptyOutDir

# ---- Stage 2: publish the .NET API -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src

# The API project reads ../../../../.version at build time — copy it to the expected location.
COPY .version ./.version
COPY services/api/ ./services/api/

RUN dotnet restore services/api/ServiceHub.sln \
 && dotnet publish services/api/src/ServiceHub.Api/ServiceHub.Api.csproj \
      --configuration Release \
      --no-restore \
      -o /app/publish

# ---- Stage 3: runtime ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the container HEALTHCHECK (the aspnet image does not ship it).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Copy the published API, then drop the freshly built SPA into wwwroot.
COPY --from=api /app/publish ./
COPY --from=web /repo/apps/web/dist ./wwwroot/

# The namespace store (JSON) and the SQLite DLQ/audit database live under this directory.
# Pin BOTH data paths here for every environment (not just Production) — otherwise Simulator/
# Development default to /app/data, which the non-root user cannot create. The path is within
# the namespace repository's allow-list (/var) and is made writable below.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DlqDatabase__DataDirectory=/var/servicehub/data \
    NamespaceRepository__DataDirectory=/var/servicehub/data
RUN mkdir -p /var/servicehub/data && chown -R $APP_UID:$APP_UID /var/servicehub
VOLUME ["/var/servicehub/data"]

# Run as the non-root user shipped in the .NET runtime image.
USER $APP_UID

EXPOSE 8080

# Healthcheck hits the auth-exempt liveness probe.
HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
  CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "ServiceHub.Api.dll"]
