# syntax=docker/dockerfile:1

# Build context is the repo root (docker-compose.prod.yml) so this stage can see
# both frontend/ and backend/. Three stages: build the SPA, publish the API with
# the SPA baked into wwwroot, then a slim runtime (docs/13-deployment.md).

# 1. Build the SPA. Its dist is served by the API as static files.
FROM node:22-alpine AS spa
WORKDIR /spa
RUN corepack enable
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend/ ./
RUN pnpm build

# 2. Publish the API. Restore on the csproj set first so the layer caches across
#    source-only changes, then copy the SPA dist into wwwroot.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Directory.Build.props backend/Directory.Packages.props ./
COPY backend/src/BillSplitter.Domain/*.csproj src/BillSplitter.Domain/
COPY backend/src/BillSplitter.Infrastructure/*.csproj src/BillSplitter.Infrastructure/
COPY backend/src/BillSplitter.Api/*.csproj src/BillSplitter.Api/
RUN dotnet restore src/BillSplitter.Api/BillSplitter.Api.csproj
COPY backend/ ./
COPY --from=spa /spa/dist src/BillSplitter.Api/wwwroot
RUN dotnet publish src/BillSplitter.Api/BillSplitter.Api.csproj -c Release -o /app --no-restore

# 3. Runtime. curl backs the container healthcheck; Kestrel listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app ./
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=20s \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

CMD ["dotnet", "BillSplitter.Api.dll"]
