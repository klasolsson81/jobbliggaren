# JobbPilot.Migrate Dockerfile — one-shot DDL-init console-app (STEG 14b).
# Build context: repo root (`docker build -f src/JobbPilot.Migrate/Dockerfile .`).
#
# Image-strategi: minimal runtime (dotnet/runtime:10.0, ej aspnet) — Migrate har
# inget HTTP-bagage. Sparar ~80 MB jämfört med worker-image.
#
# Project-dependencies (ADR 0033 amendment 2026-05-12 — Phase E EF Core MigrateAsync):
#   JobbPilot.Migrate -> JobbPilot.Infrastructure -> JobbPilot.Application -> JobbPilot.Domain
# Alla csproj + källfiler för transitiva refs måste kopieras till build-context för
# `dotnet restore` + `dotnet publish` ska lyckas.

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props JobbPilot.sln ./

COPY src/JobbPilot.Domain/JobbPilot.Domain.csproj src/JobbPilot.Domain/
COPY src/JobbPilot.Application/JobbPilot.Application.csproj src/JobbPilot.Application/
COPY src/JobbPilot.Infrastructure/JobbPilot.Infrastructure.csproj src/JobbPilot.Infrastructure/
COPY src/JobbPilot.Migrate/JobbPilot.Migrate.csproj src/JobbPilot.Migrate/

RUN dotnet restore src/JobbPilot.Migrate/JobbPilot.Migrate.csproj

COPY src/JobbPilot.Domain/ src/JobbPilot.Domain/
COPY src/JobbPilot.Application/ src/JobbPilot.Application/
COPY src/JobbPilot.Infrastructure/ src/JobbPilot.Infrastructure/
COPY src/JobbPilot.Migrate/ src/JobbPilot.Migrate/

RUN dotnet publish src/JobbPilot.Migrate/JobbPilot.Migrate.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Stage 2: runtime — minimal .NET 10 (ej aspnet, ingen HTTP-server)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime

WORKDIR /app
COPY --from=build /app/publish .

# Non-root run (default `app`-user uid 1654 sedan .NET 8).
USER app

# One-shot — task körs en gång och exitar (ECS task-def utan service-restart).
ENTRYPOINT ["dotnet", "JobbPilot.Migrate.dll"]
