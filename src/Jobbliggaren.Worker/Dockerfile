# Jobbliggaren.Worker Dockerfile — HTTP-fri (ADR 0023), runtime-image utan ASP.NET.
# Build context: repo root (`docker build -f src/Jobbliggaren.Worker/Dockerfile .`).

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props Jobbliggaren.sln ./

COPY src/Jobbliggaren.Domain/Jobbliggaren.Domain.csproj src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/Jobbliggaren.Application.csproj src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/Jobbliggaren.Infrastructure.csproj src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Worker/Jobbliggaren.Worker.csproj src/Jobbliggaren.Worker/

RUN dotnet restore src/Jobbliggaren.Worker/Jobbliggaren.Worker.csproj

COPY src/Jobbliggaren.Domain/ src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/ src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/ src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Worker/ src/Jobbliggaren.Worker/

RUN dotnet publish src/Jobbliggaren.Worker/Jobbliggaren.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Stage 2: runtime
# Worker använder aspnet-runtime tills TD-19 stänger Hangfire.AspNetCore-
# dependency. När TD-19 löst → byt till `mcr.microsoft.com/dotnet/runtime:10.0`
# (mindre image, ~80 MB sparat per image, mindre attack-yta).
# Worker startar ingen HTTP-server (ADR 0023) trots aspnet-runtime.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# RDS CA-bundle för TLS-validering mot Postgres (TD-38).
# global-bundle.pem täcker alla AWS-regions för portability dev→staging→prod.
# Persisterade CS:er (skrivna av Migrate till Secrets Manager) använder
# SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/rds-global-bundle.pem.
COPY infra/certs/rds-global-bundle.pem /etc/ssl/certs/rds-global-bundle.pem

WORKDIR /app
COPY --from=build /app/publish .

USER app

# Inga portar. Inga HEALTHCHECK — Hangfire-process är sin egen liveness-signal.
# ECS task-def avgör health via container exit code (process-crash → restart).

# SIGTERM-handling: Hangfire ShutdownTimeoutSeconds=25 (per appsettings.Production.json),
# Fargate stopTimeout=30s (ECS task-def) → 5s margin för task-state-commit.

ENTRYPOINT ["dotnet", "Jobbliggaren.Worker.dll"]
