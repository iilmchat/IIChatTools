<#
.SYNOPSIS
    Создаёт Pull Request для миграции на .NET 10 LTS.
.DESCRIPTION
    Требует установленный GitHub CLI (gh) и аутентификацию (gh auth login).
    Открывает PR из feature/net10-migration в main.
.NOTES
    Запускать из корня репозитория (G:\AI\IIChatTools).
#>

[CmdletBinding()]
param(
    [string]$BaseBranch = 'main',
    [string]$HeadBranch = 'feature/net10-migration',
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    # Проверка gh
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) не найден. Установите: winget install GitHub.cli"
    }

    # Проверка авторизации
    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Не выполнен вход в gh. Выполните: gh auth login"
    }

    # Push ветки (если ещё не запушена)
    Write-Host "== Push ветки ==" -ForegroundColor Cyan
    git push -u origin $HeadBranch

    $title = "Миграция на .NET 10 LTS (v1.1.0)"

    $body = @'
## Что сделано

Полная миграция с .NET Core 3.1 (снят с поддержки 13.12.2022) на .NET 10 LTS.

### Изменения
- **TargetFramework**: `netcoreapp3.1` → `net10.0`, `LangVersion latest`
- **Пакеты**: EF Core / ASP.NET Core / Extensions → `10.0.4`, IdentityModel → `8.14.0`
- **Инфраструктура**: `global.json` и `NuGet.Config` перенесены в корень, `LocalPackages` — offline-source
- **Версионирование**: `AppVersion.Current` читается из сборки (источник — `<Version>` в `Directory.Build.props`)
- **Локализация**: ключ `WelcomeTitle` с плейсхолдером `{0}` вместо версии в ключе
- **Тесты**: `TestDbContextFactory` сидирует пользователей для тестов ApprovalService

### Проверено
- [x] `dotnet build` — 0 warnings, 0 errors
- [x] `dotnet test` — 14/14 passed
- [x] Offline-restore из `LocalPackages` (без сети)
- [x] Запуск приложения на `https://localhost:5001`
- [x] Аутентификация (cookie) и `/api/status` (JWT)
- [x] Все 40 инструментов зарегистрированы
- [x] Электронная подпись: `list_directory` через `/test`
- [x] Переключение RU ↔ EN
- [x] Версия `1.1.0` во всех точках UI и логов

### Известные ограничения (Deferred / Open)
- KI-022: уязвимость `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (NU1903) — отложено до v1.1.x
- KI-036: хардкод прокси-credentials в `Program.cs` — требует исправления
- KI-037: дублирование строк подключения в `appsettings.json`
- Миграция `Program.cs` на top-level statements — отдельная задача (не блокер)

### Тег-снапшот
- `v1.0.2-pre-net10` — состояние до миграции
- Ветка `preNet10` — резервная

## Checklist
- [x] Версия обновлена (`AppVersion.Current` = 1.1.0)
- [x] `KNOWN_ISSUES.md` обновлён (KI-015 … KI-037)
- [x] `README.md` обновлён
- [x] Все тесты зелёные
- [x] Offline-развёртывание работает
- [x] RU/EN синхронны

Closes: KI-015, KI-016, KI-017, KI-018, KI-019, KI-020, KI-021,
        KI-030, KI-031, KI-031a, KI-033, KI-034, KI-035
'@

    $body | Out-File -FilePath ".pr-body.tmp" -Encoding utf8 -NoNewline
    try {
        $args = @('pr', 'create',
            '--base', $BaseBranch,
            '--head', $HeadBranch,
            '--title', $title,
            '--body-file', '.pr-body.tmp')
        if ($Draft) { $args += '--draft' }

        Write-Host "== Создание PR ==" -ForegroundColor Cyan
        & gh @args
    } finally {
        Remove-Item ".pr-body.tmp" -ErrorAction SilentlyContinue
    }

    Write-Host "`n== Готово ==" -ForegroundColor Green
}
finally {
    Pop-Location
}