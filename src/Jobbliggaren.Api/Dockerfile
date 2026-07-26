# Jobbliggaren.Api Dockerfile — multi-stage .NET 10 build, non-root, healthcheck.
# Build context: repo root (`docker build -f src/Jobbliggaren.Api/Dockerfile .`).

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Centralt versions-filer + sln (kopieras först för bättre layer-cache).
COPY global.json Directory.Build.props Directory.Packages.props Jobbliggaren.sln ./

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
# Ingen Docker HEALTHCHECK och ingen curl-installation:
#   - Fargate respekterar inte Docker HEALTHCHECK (per AWS-docs — bara EC2-launch-type)
#   - ECS task-def har egen healthCheck-block som är den auktoritativa health-check
#   - ALB target-group har egen health-check mot /api/ready
#   - Lokal `docker run` kan testas via `curl` från host istället
# Mindre image-storlek + mindre attack-yta.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# AWS-residual: RDS CA-bundle för TLS-validering mot Postgres (TD-38-eran).
# Motiveringen som stod här — "täcker alla AWS-regions för portability
# dev→staging→prod" — gäller inte längre: AWS-stacken revs 2026-05-26 (ADR 0066)
# och Secrets Manager-vägen är borta med den. COPY:n är kvar tills #196 (TD-106)
# bygger om imagen för Hetzner, där cert-strategin avgörs (privat bridge utan TLS,
# eller intern CA + VerifyFull) — samma ägare och samma villkor som Migrate-imagens.
# Rör den inte i en städ-PR: #808 verifierade att alla tre Dockerfiles COPY:ar den
# och att .dockerignore bär negationer för just det, medan den enda docker build i
# CI pekar på pre-rename-sökvägar. Skyddet är dessutom dubbelt: den workflowen är
# `workflow_dispatch`-only sedan 2026-06-28, så ingen `docker build` körs i CI
# överhuvudtaget. En radering går alltså GRÖN av två oberoende skäl och faller
# först vid Hetzner-cutovern.
COPY infra/certs/rds-global-bundle.pem /etc/ssl/certs/rds-global-bundle.pem

WORKDIR /app
COPY --from=build /app/publish .

# Non-root run.
USER app

# Containerport (matchar ECS-SG ingress + ALB target-group).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Jobbliggaren.Api.dll"]
