# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f apps/api/Dockerfile -t clearcase-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY apps/api/src/ClearCase.Domain/*.csproj apps/api/src/ClearCase.Domain/packages.lock.json apps/api/src/ClearCase.Domain/
COPY apps/api/src/ClearCase.Application/*.csproj apps/api/src/ClearCase.Application/packages.lock.json apps/api/src/ClearCase.Application/
COPY apps/api/src/ClearCase.Infrastructure/*.csproj apps/api/src/ClearCase.Infrastructure/packages.lock.json apps/api/src/ClearCase.Infrastructure/
COPY apps/api/src/ClearCase.Api/*.csproj apps/api/src/ClearCase.Api/packages.lock.json apps/api/src/ClearCase.Api/
RUN dotnet restore apps/api/src/ClearCase.Api/ClearCase.Api.csproj --locked-mode
COPY apps/api/src apps/api/src
RUN dotnet publish apps/api/src/ClearCase.Api/ClearCase.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# SQLite database and data-protection keys live on a volume mounted at /data.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    Persistence__DatabasePath=/data/clearcase.db \
    DataProtection__KeyPath=/data/keys
EXPOSE 8080
# Run as the image's non-root user; `dotnet ClearCase.Api.dll --migrate-only` applies migrations and exits.
USER $APP_UID
ENTRYPOINT ["dotnet", "ClearCase.Api.dll"]
