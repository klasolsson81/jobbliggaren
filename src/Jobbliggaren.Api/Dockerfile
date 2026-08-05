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
# Ingen Docker HEALTHCHECK och ingen curl-installation. Skälet var ECS/ALB-format och
# stacken revs (ADR 0066); det som bär beslutet nu är att `deploy/docker-compose.yml`
# äger hälsokontrollen och probar `/api/ready` per tjänst. Att låta imagen förbli
# verktygsfri är mindre attack-yta — proben i compose använder bashens `/dev/tcp`
# just för att slippa installera curl här.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

WORKDIR /app
COPY --from=build /app/publish .

# Non-root run.
USER app

# Containerport (matchar ECS-SG ingress + ALB target-group).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Jobbliggaren.Api.dll"]
