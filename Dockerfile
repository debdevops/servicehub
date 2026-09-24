# ServiceHub 4.1.0 — one image, one process, one origin.
#
# The archive folder is excluded in .dockerignore and is never present in any layer
# (ADR-0013 D6, ADR-0014 D3).

# ── 1. The SPA ──────────────────────────────────────────────────────────────────────────────────
FROM node:22-alpine AS web
WORKDIR /src

COPY package.json package-lock.json ./
COPY apps/servicehub/package.json apps/servicehub/
RUN npm ci

COPY .version ./.version
COPY apps/servicehub apps/servicehub
# Build inside the image rather than into services/api: the runtime stage copies it where it needs
# it, and the build never depends on the host's working tree.
RUN npm run build -w apps/servicehub -- --outDir /out/wwwroot --emptyOutDir

# ── 2. The API ──────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
WORKDIR /src

# .version first: Directory.Build.props reads it for the assembly version (one source of truth).
COPY .version ./.version
COPY services/api/global.json services/api/Directory.Build.props services/api/ServiceHub.slnx services/api/
COPY services/api/src services/api/src
COPY services/api/tests services/api/tests
RUN dotnet restore services/api/ServiceHub.slnx

RUN dotnet publish services/api/src/ServiceHub.Api \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ── 3. Runtime ──────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Non-root, and a writable place for the SQLite file that is a volume in practice.
RUN addgroup -S servicehub && adduser -S -G servicehub servicehub \
    && mkdir -p /data && chown -R servicehub:servicehub /data

COPY --from=api  /app/publish ./
COPY --from=web  /out/wwwroot ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ServiceHub__DataDirectory=/data

EXPOSE 8080
VOLUME ["/data"]
USER servicehub

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ServiceHub.Api.dll"]
