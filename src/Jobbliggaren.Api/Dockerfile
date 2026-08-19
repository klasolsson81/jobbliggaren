# Jobbliggaren.Api Dockerfile — multi-stage .NET 10 build, non-root, healthcheck.
# Build context: repo root (`docker build -f src/Jobbliggaren.Api/Dockerfile .`).

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Centralt versions-filer + sln (kopieras först för bättre layer-cache).
COPY global.json Directory.Build.props Directory.Packages.props Jobbliggaren.sln .editorconfig ./

# csproj först (cache restore om bara kod ändras).
COPY src/Jobbliggaren.Domain/Jobbliggaren.Domain.csproj src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/Jobbliggaren.Application.csproj src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/Jobbliggaren.Infrastructure.csproj src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Api/Jobbliggaren.Api.csproj src/Jobbliggaren.Api/

RUN dotnet restore src/Jobbliggaren.Api/Jobbliggaren.Api.csproj

# Källkod
COPY src/Jobbliggaren.Domain/ src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/ src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/ src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Api/ src/Jobbliggaren.Api/

RUN dotnet publish src/Jobbliggaren.Api/Jobbliggaren.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Stage 2: runtime
# .NET 10 ASP.NET-image. Default `app`-user (uid 1654) är non-root sedan .NET 8.
#
# Ingen Docker HEALTHCHECK och ingen curl-installation. Skälet var ECS/ALB-format och
# stacken revs (ADR 0066); det som bär beslutet nu är att `deploy/docker-compose.yml`
# äger hälsokontrollen och probar `/api/ready` per tjänst. Att låta imagen förbli
# verktygsfri är mindre attack-yta — proben i compose använder bashens `/dev/tcp`
# just för att slippa installera curl här.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

WORKDIR /app
COPY --from=build /app/publish .

# #1350 - mount point for the Data-Protection keyring, created and OWNED here. A named volume
# mounted over a path the image does not have is created root:root 0755, and Docker only copies
# and chowns when the image has content at the destination. uid 1654 then cannot write a key.
# Measured 2026-08-19 against a fresh empty volume: `drwxr-xr-x 2 root root` and
# `touch: Permission denied` without this line; `drwxr-xr-x 2 app app` and a successful write with
# it. The failure without it is invisible where anyone would look: the FileSystemXmlRepository
# constructor only calls Directory.Create(), which is a no-op on an existing directory, so boot and
# /api/ready are both green and the UnauthorizedAccessException lands on the FIRST key generation,
# i.e. the first registration or password reset. `deploy/docker-compose.yml` mounts the volume here
# and DeployComposeDataProtectionTests pins that this path and that mount stay the same string.
RUN mkdir -p /keys && chown app:app /keys

# Non-root run.
USER app

# Containerport (matchar ECS-SG ingress + ALB target-group).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Jobbliggaren.Api.dll"]
