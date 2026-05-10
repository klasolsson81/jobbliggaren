# JobbPilot.Worker Dockerfile — HTTP-fri (ADR 0023), runtime-image utan ASP.NET.
# Build context: repo root (`docker build -f src/JobbPilot.Worker/Dockerfile .`).

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props JobbPilot.sln ./

COPY src/JobbPilot.Domain/JobbPilot.Domain.csproj src/JobbPilot.Domain/
COPY src/JobbPilot.Application/JobbPilot.Application.csproj src/JobbPilot.Application/
COPY src/JobbPilot.Infrastructure/JobbPilot.Infrastructure.csproj src/JobbPilot.Infrastructure/
COPY src/JobbPilot.Worker/JobbPilot.Worker.csproj src/JobbPilot.Worker/

RUN dotnet restore src/JobbPilot.Worker/JobbPilot.Worker.csproj

COPY src/JobbPilot.Domain/ src/JobbPilot.Domain/
COPY src/JobbPilot.Application/ src/JobbPilot.Application/
COPY src/JobbPilot.Infrastructure/ src/JobbPilot.Infrastructure/
COPY src/JobbPilot.Worker/ src/JobbPilot.Worker/

RUN dotnet publish src/JobbPilot.Worker/JobbPilot.Worker.csproj \
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

WORKDIR /app
COPY --from=build /app/publish .

USER app

# Inga portar. Inga HEALTHCHECK — Hangfire-process är sin egen liveness-signal.
# ECS task-def avgör health via container exit code (process-crash → restart).

# SIGTERM-handling: Hangfire ShutdownTimeoutSeconds=25 (per appsettings.Production.json),
# Fargate stopTimeout=30s (ECS task-def) → 5s margin för task-state-commit.

ENTRYPOINT ["dotnet", "JobbPilot.Worker.dll"]
