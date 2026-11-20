# syntax=docker/dockerfile:1.7
#
# Aufgaben – Container-Image in drei Stufen:
#   1. Frontend (React) bauen
#   2. Backend (.NET 10) veröffentlichen
#   3. Schlankes Laufzeit-Image ohne Shell, ohne Paketmanager, ohne Root ("chiseled")

# ------------------------------------------------------------------ 1. Frontend
FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ------------------------------------------------------------------ 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Aufgaben.slnx ./
COPY src/Aufgaben.Api/Aufgaben.Api.csproj src/Aufgaben.Api/
RUN dotnet restore src/Aufgaben.Api/Aufgaben.Api.csproj
COPY src/ src/
COPY --from=frontend /src/src/Aufgaben.Api/wwwroot src/Aufgaben.Api/wwwroot
RUN dotnet publish src/Aufgaben.Api/Aufgaben.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ------------------------------------------------------------------ 3. Laufzeit
# "-extra" enthält ICU und Zeitzonendaten (nötig für Europe/Berlin und deutsche Formate).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD ["dotnet", "Aufgaben.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Aufgaben.Api.dll"]
