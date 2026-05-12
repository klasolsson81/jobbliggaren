# JobbPilot.Migrate Dockerfile — one-shot DDL-init console-app (STEG 14b).
# Build context: repo root (`docker build -f src/JobbPilot.Migrate/Dockerfile .`).
#
# Image-strategi: aspnet:10.0-noble runtime (ej minimal runtime) sedan F2-P8a.5
# ADR 0033 — JobbPilot.Infrastructure har transitiva ASP.NET-deps (Mediator/
# Hangfire/EF pipeline-behaviors). Tidigare runtime:10.0 (minimal) gav exit 150
# "Microsoft.AspNetCore.App not found". Samma image-bas som Worker.
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
# Stage 2: runtime — aspnet:10.0-noble (krävs av Infrastructure-projektets
# transitiva ASP.NET-deps trots att Migrate själv är HTTP-fri console-app).
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# RDS CA-bundle för TLS-validering mot Postgres (TD-38). Phase E använder
# persisted CS från Secrets Manager (jobbpilot_app-creds) som har
# SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/rds-global-bundle.pem
# per ConnectionStringFactory.ForPersisted. Bundle delas med Api/Worker
# (samma COPY-pattern i deras Dockerfiles).
COPY infra/certs/rds-global-bundle.pem /etc/ssl/certs/rds-global-bundle.pem

WORKDIR /app
COPY --from=build /app/publish .

# Non-root run (default `app`-user uid 1654 sedan .NET 8).
USER app

# One-shot — task körs en gång och exitar (ECS task-def utan service-restart).
ENTRYPOINT ["dotnet", "JobbPilot.Migrate.dll"]
