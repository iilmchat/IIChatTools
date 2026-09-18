<#
.SYNOPSIS
    Создаёт временный NuGet.Config.online из шаблона NuGet.Config.example.online.
.DESCRIPTION
    Копирует NuGet.Config.example.online в NuGet.Config.online (не коммитится).
    Нужен, когда требуется restore с nuget.org (например, при наполнении
    LocalPackages или на машине с интернетом).

    Проверяет, что NuGet.Config.online указан в .gitignore.

.PARAMETER Force
    Перезаписать существующий NuGet.Config.online.

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts\setup\enable-online-restore.ps1

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts\setup\enable-online-restore.ps1 -Force

.NOTES
    © 2026 RuChating (iilmchat) · IIChatTools v1.1.1
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Корень репозитория — на 2 уровня выше scripts/setup/
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    $template = Join-Path $repoRoot 'NuGet.Config.example.online'
    $target   = Join-Path $repoRoot 'NuGet.Config.online'

    if (-not (Test-Path $template)) {
        throw "Не найден шаблон: $template"
    }

    if ((Test-Path $target) -and -not $Force) {
        Write-Host "[i] NuGet.Config.online уже существует. Используйте -Force для перезаписи." -ForegroundColor Yellow
        exit 0
    }

    Copy-Item $template $target -Force
    Write-Host "[OK] Создан NuGet.Config.online" -ForegroundColor Green

    # Проверка .gitignore
    $ignored = git check-ignore $target 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[!] NuGet.Config.online НЕ в .gitignore — добавьте правило!" -ForegroundColor Red
    } else {
        Write-Host "[OK] NuGet.Config.online игнорируется git" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Следующий шаг:" -ForegroundColor Cyan
    Write-Host "  dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force"
}
finally {
    Pop-Location
}