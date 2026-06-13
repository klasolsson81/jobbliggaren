# Jobbliggaren.Migrate Dockerfile — one-shot DDL-init console-app (STEG 14b).
# Build context: repo root (`docker build -f src/Jobbliggaren.Migrate/Dockerfile .`).
#
# Image-strategi: aspnet:10.0-noble runtime (ej minimal runtime) sedan F2-P8a.5
# ADR 0033 — Jobbliggaren.Infrastructure har transitiva ASP.NET-deps (Mediator/
# Hangfire/EF pipeline-behaviors). Tidigare runtime:10.0 (minimal) gav exit 150
# "Microsoft.AspNetCore.App not found". Samma image-bas som Worker.
#
# Project-dependencies (ADR 0033 amendment 2026-05-12 — Phase E EF Core MigrateAsync):
#   Jobbliggaren.Migrate -> Jobbliggaren.Infrastructure -> Jobbliggaren.Application -> Jobbliggaren.Domain
# Alla csproj + källfiler för transitiva refs måste kopieras till build-context för
# `dotnet restore` + `dotnet publish` ska lyckas.

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props Jobbliggaren.sln ./

COPY src/Jobbliggaren.Domain/Jobbliggaren.Domain.csproj src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/Jobbliggaren.Application.csproj src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/Jobbliggaren.Infrastructure.csproj src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Migrate/Jobbliggaren.Migrate.csproj src/Jobbliggaren.Migrate/

RUN dotnet restore src/Jobbliggaren.Migrate/Jobbliggaren.Migrate.csproj

COPY src/Jobbliggaren.Domain/ src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/ src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/ src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Migrate/ src/Jobbliggaren.Migrate/

RUN dotnet publish src/Jobbliggaren.Migrate/Jobbliggaren.Migrate.csproj \
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
# persisted CS från Secrets Manager (jobbliggaren_app-creds) som har
# SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/rds-global-bundle.pem
# per ConnectionStringFactory.ForPersisted. Bundle delas med Api/Worker
# (samma COPY-pattern i deras Dockerfiles).
COPY infra/certs/rds-global-bundle.pem /etc/ssl/certs/rds-global-bundle.pem

WORKDIR /app
COPY --from=build /app/publish .

# Non-root run (default `app`-user uid 1654 sedan .NET 8).
USER app

# One-shot — task körs en gång och exitar (ECS task-def utan service-restart).
ENTRYPOINT ["dotnet", "Jobbliggaren.Migrate.dll"]
