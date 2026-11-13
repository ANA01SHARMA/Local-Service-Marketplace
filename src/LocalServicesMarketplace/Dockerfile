# syntax=docker/dockerfile:1
# Multi-stage build for the Local Services Marketplace web app.
# Build context is the repository root (see compose.yaml).

# ---- build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between code changes.
COPY global.json ./
COPY src/LocalServicesMarketplace/LocalServicesMarketplace.csproj src/LocalServicesMarketplace/
RUN dotnet restore src/LocalServicesMarketplace/LocalServicesMarketplace.csproj

COPY src/LocalServicesMarketplace/ src/LocalServicesMarketplace/
RUN dotnet publish src/LocalServicesMarketplace/LocalServicesMarketplace.csproj \
        -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime -----------------------------------------------------------------
# Debian-based ASP.NET runtime: includes ICU (SqlClient, en-IN formatting) and tzdata (Asia/Kolkata).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Storage__Root=/app/storage

COPY --from=build /app/publish .

# Uploads, private verification documents and Data Protection keys live on a volume.
RUN mkdir -p /app/storage && chown -R $APP_UID /app/storage
VOLUME ["/app/storage"]

USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=10 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "LocalServicesMarketplace.dll"]
