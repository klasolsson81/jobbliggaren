# Jobbliggaren.Worker Dockerfile — HTTP-fri (ADR 0023), runtime-image utan ASP.NET.
# Build context: repo root (`docker build -f src/Jobbliggaren.Worker/Dockerfile .`).

# ---------------------------------------------------------------------------
# Stage 1: build + publish
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props Jobbliggaren.sln .editorconfig ./

COPY src/Jobbliggaren.Domain/Jobbliggaren.Domain.csproj src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/Jobbliggaren.Application.csproj src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/Jobbliggaren.Infrastructure.csproj src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Worker/Jobbliggaren.Worker.csproj src/Jobbliggaren.Worker/

RUN dotnet restore src/Jobbliggaren.Worker/Jobbliggaren.Worker.csproj

COPY src/Jobbliggaren.Domain/ src/Jobbliggaren.Domain/
COPY src/Jobbliggaren.Application/ src/Jobbliggaren.Application/
COPY src/Jobbliggaren.Infrastructure/ src/Jobbliggaren.Infrastructure/
COPY src/Jobbliggaren.Worker/ src/Jobbliggaren.Worker/

RUN --mount=type=secret,id=sixlabors_license,required=true dotnet publish src/Jobbliggaren.Worker/Jobbliggaren.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:SixLaborsLicenseFile=/run/secrets/sixlabors_license

# ---------------------------------------------------------------------------
# Stage 2: runtime
# Worker startar ingen HTTP-server (ADR 0023) trots aspnet-runtime.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends --only-upgrade libssl3t64 openssl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
COPY licenses/SixLabors.ImageSharp/ /app/licenses/SixLabors.ImageSharp/

USER app

ENTRYPOINT ["dotnet", "Jobbliggaren.Worker.dll"]
