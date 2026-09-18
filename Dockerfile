# syntax=docker/dockerfile:1.7
#
# IIChatTools — мультистейдж Dockerfile
# =====================================
# Stage 1 (build):   SDK 10.0 — restore + publish
# Stage 2 (runtime): ASP.NET Core Runtime 10.0 — только рантайм + приложение
#
# Опциональный Chromium для PuppeteerSharp — через build-arg INSTALL_BROWSER=true
# (по умолчанию false — образ ~250 МБ; с Chromium — ~650 МБ).
#
# © 2026 RuChating (iilmchat) · IIChatTools v1.1.1

# ============================================================
# Stage 1: Build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файлы решения и общих свойств (для кеша restore)
COPY IIChatTools.sln Directory.Build.props global.json ./

# Копируем .csproj всех проектов (для кеша restore)
COPY IIChatTools.API/IIChatTools.API.csproj           IIChatTools.API/
COPY IIChatTools.Data/IIChatTools.Data.csproj         IIChatTools.Data/
COPY IIChatTools.Services/IIChatTools.Services.csproj IIChatTools.Services/
COPY IIChatTools.Tests/IIChatTools.Tests.csproj       IIChatTools.Tests/

# Временный NuGet.Config с nuget.org.
# Корневой NuGet.Config репозитория ссылается на LocalPackages (offline),
# которых в контексте сборки нет. Для Docker-сборки используем nuget.org.
RUN printf '%s\n' \
    '<?xml version="1.0" encoding="utf-8"?>' \
    '<configuration>' \
    '  <packageSources>' \
    '    <clear />' \
    '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' \
    '  </packageSources>' \
    '</configuration>' > NuGet.Config

# Restore — отдельным слоем (кешируется при неизменных .csproj)
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore IIChatTools.sln --configfile NuGet.Config

# Копируем остальные исходники
COPY . .

# Publish только API (остальные проекты подключены через ProjectReference)
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish IIChatTools.API/IIChatTools.API.csproj \
        --configuration Release \
        --output /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ============================================================
# Stage 2: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Включать ли Chromium для браузерных инструментов.
# Управляется build-arg из docker-compose (INSTALL_BROWSER=false по умолчанию).
ARG INSTALL_BROWSER=false

# ca-certificates + curl — нужны для HTTPS и healthcheck
# При INSTALL_BROWSER=true — добавляем нативные библиотеки для Chromium
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
    && if [ "$INSTALL_BROWSER" = "true" ]; then \
        apt-get install -y --no-install-recommends \
            libglib2.0-0 \
            libnss3 \
            libnspr4 \
            libatk1.0-0 \
            libatk-bridge2.0-0 \
            libcups2 \
            libdrm2 \
            libdbus-1-3 \
            libxcb1 \
            libxkbcommon0 \
            libx11-6 \
            libxcomposite1 \
            libxdamage1 \
            libxext6 \
            libxfixes3 \
            libxrandr2 \
            libgbm1 \
            libpango-1.0-0 \
            libcairo2 \
            libasound2 \
            fonts-liberation ; \
    fi \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Непривилегированный пользователь (UID/GID 1000)
RUN groupadd --gid 1000 app \
    && useradd --uid 1000 --gid 1000 --shell /usr/sbin/nologin --no-create-home --home-dir /app app

# Копируем артефакты publish
COPY --from=build --chown=app:app /app/publish ./

# Каталоги для данных, логов, workspace и Chromium-кеша
RUN mkdir -p /app/Data /app/logs/audit /app/Workspace /app/.puppeteer \
    && chown -R app:app /app/Data /app/logs /app/Workspace /app/.puppeteer

USER app

# Значения по умолчанию (переопределяются docker-compose / env)
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Database__Provider=Sqlite \
    Database__SqliteConnectionString="Data Source=/app/Data/iichattools.db" \
    Workspace__RootPath=/app/Workspace \
    Audit__LogDirectory=/app/logs/audit \
    Audit__ToDatabase=true \
    Audit__ToFile=true \
    PUPPETEER_CACHE_DIR=/app/.puppeteer

EXPOSE 8080

# Healthcheck временно на / — переключим на /health/live в v1.2 (Health checks)
HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
    CMD curl -fsS http://localhost:8080/ >/dev/null || exit 1

ENTRYPOINT ["dotnet", "IIChatTools.API.dll"]