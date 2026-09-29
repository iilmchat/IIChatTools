<#
.SYNOPSIS
    Проверяет транзитивные NuGet-зависимости решения на известные уязвимости.

.DESCRIPTION
    Обёртка над `dotnet list package --vulnerable --include-transitive`.
    Парсит вывод, при наличии уязвимых пакетов — завершается кодом 1
    (для использования в CI / pre-commit hook).

    Прецедент: KI-105 (System.IO.Packaging 8.0.0 → 10.0.0 через
    DocumentFormat.OpenXml 3.1.0), KI-022 (SQLitePCLRaw 2.1.11 → 2.1.13).

.PARAMETER Solution
    Путь к .sln или .csproj. По умолчанию — IIChatTools.sln в корне репо.

.PARAMETER ConfigFile
    NuGet.Config для restore. Если файл не существует — restore идёт без
    явного конфига (используется NuGet.Config из корня).

.EXAMPLE
    pwsh -File scripts/setup/check-vulnerabilities.ps1
    # Проверяет IIChatTools.sln, exit 0 — чисто, exit 1 — есть уязвимости.

.EXAMPLE
    pwsh -File scripts/setup/check-vulnerabilities.ps1 -Solution IIChatTools.Services/IIChatTools.Services.csproj
    # Проверяет только один проект.

.NOTES
    © 2026 RuChating (iilmchat) · IIChatTools v1.7.1
    RULES § 5.x — безопасность. См. KI-022, KI-105.
#>
[CmdletBinding()]
param(
    [string]$Solution = "IIChatTools.sln",
    [string]$ConfigFile = "NuGet.Config.online"
)

$ErrorActionPreference = "Stop"

# --- 1. Валидация входных данных ---
if (-not (Test-Path $Solution)) {
    Write-Error "Не найден файл решения: $Solution"
    exit 2
}

# --- 2. Restore (NuGet Audit = только после restore) ---
$restoreArgs = @("restore", $Solution, "--force", "--verbosity", "quiet")
if (Test-Path $ConfigFile) {
    $restoreArgs += "--configfile", $ConfigFile
    Write-Host "[INFO] Restore с конфигом: $ConfigFile" -ForegroundColor Cyan
} else {
    Write-Host "[INFO] Restore без явного конфига (NuGet.Config из корня)" -ForegroundColor Cyan
}

Write-Host "[INFO] dotnet $($restoreArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @restoreArgs | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Error "Restore завершился с ошибкой (exit $LASTEXITCODE)."
    exit 2
}

# --- 3. Проверка уязвимостей ---
Write-Host "[INFO] Проверка уязвимых пакетов (--vulnerable --include-transitive)..." -ForegroundColor Cyan
$vulnerableOutput = & dotnet list $Solution package --vulnerable --include-transitive 2>&1 | Out-String

# --- 4. Парсинг вывода ---
# Признак наличия уязвимостей — строка "содержит уязвимые пакеты" (RU)
# или "has the following vulnerable packages" (EN).
$hasVulnerabilities = $vulnerableOutput -match "содержит уязвимые пакеты" `
    -or $vulnerableOutput -match "has the following vulnerable packages"

if ($hasVulnerabilities) {
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Red
    Write-Host "║  ОБНАРУЖЕНЫ УЯЗВИМЫЕ ПАКЕТЫ                                    ║" -ForegroundColor Red
    Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Red
    Write-Host ""
    Write-Host $vulnerableOutput
    Write-Host ""
    Write-Host "[FAIL] Есть уязвимые пакеты. См. RULES § 5.x, KI-022, KI-105." -ForegroundColor Red
    Write-Host "[INFO] Как исправить:" -ForegroundColor Yellow
    Write-Host "  1. Найти источник (транзитивный?) → 'dotnet list package --include-transitive | Select-String <пакет>'"
    Write-Host "  2. Явный PackageReference с безопасной версией в .csproj."
    Write-Host "  3. Версия → Directory.Build.props (по образцу SQLitePCLRawVersion / SystemIOPackagingVersion)."
    exit 1
}

Write-Host ""
Write-Host "[OK] Уязвимых пакетов не найдено." -ForegroundColor Green
exit 0