# JobbPilot.Api Dockerfile — multi-stage .NET 10 build, non-root, healthcheck.
# Build context: repo root (`docker build -f src/JobbPilot.Api/Dockerfile .`).

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Centralt versions-filer + sln (kopieras först för bättre layer-cache).
COPY global.json Directory.Build.props Directory.Packages.props JobbPilot.sln ./

# csproj först (cache restore om bara kod ändras).
COPY src/JobbPilot.Domain/JobbPilot.Domain.csproj src/JobbPilot.Domain/
COPY src/JobbPilot.Application/JobbPilot.Application.csproj src/JobbPilot.Application/
COPY src/JobbPilot.Infrastructure/JobbPilot.Infrastructure.csproj src/JobbPilot.Infrastructure/
COPY src/JobbPilot.Api/JobbPilot.Api.csproj src/JobbPilot.Api/

RUN dotnet restore src/JobbPilot.Api/JobbPilot.Api.csproj

# Källkod
COPY src/JobbPilot.Domain/ src/JobbPilot.Domain/
COPY src/JobbPilot.Application/ src/JobbPilot.Application/
COPY src/JobbPilot.Infrastructure/ src/JobbPilot.Infrastructure/
COPY src/JobbPilot.Api/ src/JobbPilot.Api/

RUN dotnet publish src/JobbPilot.Api/JobbPilot.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Stage 2: runtime
# .NET 10 ASP.NET-image. Default `app`-user (uid 1654) är non-root sedan .NET 8.
#
# Ingen Docker HEALTHCHECK och ingen curl-installation:
#   - Fargate respekterar inte Docker HEALTHCHECK (per AWS-docs — bara EC2-launch-type)
#   - ECS task-def har egen healthCheck-block som är den auktoritativa health-check
#   - ALB target-group har egen health-check mot /api/ready
#   - Lokal `docker run` kan testas via `curl` från host istället
# Mindre image-storlek + mindre attack-yta.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# RDS CA-bundle för TLS-validering mot Postgres (TD-38).
# global-bundle.pem täcker alla AWS-regions för portability dev→staging→prod.
# Persisterade CS:er (skrivna av Migrate till Secrets Manager) använder
# SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/rds-global-bundle.pem.
COPY infra/certs/rds-global-bundle.pem /etc/ssl/certs/rds-global-bundle.pem

WORKDIR /app
COPY --from=build /app/publish .

# Non-root run.
USER app

# Containerport (matchar ECS-SG ingress + ALB target-group).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "JobbPilot.Api.dll"]
