<#
.SYNOPSIS
    Фиксирует изменения миграции на .NET 10 LTS в ветке feature/net10-migration.
.DESCRIPTION
    Проверяет состояние репозитория, добавляет файлы, создаёт коммит
    с подробным сообщением. Не выполняет push — push делается отдельно.
.NOTES
    Запускать из корня репозитория (G:\AI\IIChatTools).
#>

[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    Write-Host "== Проверка состояния ==" -ForegroundColor Cyan

    $branch = git branch --show-current
    Write-Host "Ветка: $branch" -ForegroundColor Yellow
    if ($branch -ne 'feature/net10-migration') {
        throw "Ожидалась ветка 'feature/net10-migration', текущая: '$branch'. Переключитесь: git checkout feature/net10-migration"
    }

    Write-Host "`n== Проверка .gitignore ==" -ForegroundColor Cyan
    foreach ($pattern in @('NuGet.Config.online', 'LocalPackages', 'logs/')) {
        $result = git check-ignore $pattern 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [!] $pattern НЕ в .gitignore — добавьте!" -ForegroundColor Red
        } else {
            Write-Host "  [OK] $pattern игнорируется" -ForegroundColor Green
        }
    }

    Write-Host "`n== Изменения ==" -ForegroundColor Cyan
    git status --short

    if ($DryRun) {
        Write-Host "`n[DryRun] git add и git commit не выполнены." -ForegroundColor Yellow
        return
    }

    Write-Host "`n== Добавление файлов ==" -ForegroundColor Cyan
    git add -A

    $commitMessage = @'
feat: миграция на .NET 10 LTS (v1.1.0)

Основное:
- TargetFramework: netcoreapp3.1 → net10.0, LangVersion latest
- EF Core 3.1.32 → 10.0.4, Microsoft.Extensions.* → 10.0.4
- Microsoft.AspNetCore.* → 10.0.4, IdentityModel → 8.14.0
- global.json → SDK 10.0.401 (latestFeature), перенесён в корень
- NuGet.Config перенесён в корень, LocalPackages как offline-source

Код и конфигурация:
- AppVersion.Current читается из AssemblyInformationalVersion
- AppVersion.Copyright формируется автоматически
- Двойной © в логе устранён (KI-030)
- Ключ локализации WelcomeTitle, версия вынесена в {0} (KI-031a)
- Версия синхронизирована: 1.0.2 → 1.1.0 везде (KI-031)
- favicon через data: URI — нет 404 (KI-033)

Удалено:
- Microsoft.AspNetCore.Identity 2.2.0 (устаревший)
- Microsoft.Extensions.Logging.* из API (NU1510)
- Resources/Как использовать в коде.txt

Тесты:
- TestDbContextFactory сидирует пользователей Id=1,2,3 (KI-035)
- 14/14 тестов проходят

Документация:
- README.md обновлён под .NET 10 LTS, v1.1.0
- docs/KNOWN_ISSUES.md обновлён (KI-015 … KI-037)

Closes: KI-015, KI-016, KI-017, KI-018, KI-019, KI-020, KI-021,
        KI-030, KI-031, KI-031a, KI-033, KI-034, KI-035
'@

    $commitMessage | Out-File -FilePath ".commit-msg.tmp" -Encoding utf8 -NoNewline
    try {
        git commit -F ".commit-msg.tmp"
    } finally {
        Remove-Item ".commit-msg.tmp" -ErrorAction SilentlyContinue
    }

    Write-Host "`n== Готово ==" -ForegroundColor Green
    Write-Host "Коммит создан. Для отправки на сервер: " -NoNewline
    Write-Host "git push origin feature/net10-migration" -ForegroundColor Yellow
}
finally {
    Pop-Location
}