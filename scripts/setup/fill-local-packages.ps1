<#
.SYNOPSIS
    Наполняет LocalPackages/ пакетами из глобального NuGet-кэша.
.DESCRIPTION
    Проходит по всем .nupkg в глобальном кэше NuGet (~/.nuget/packages)
    и копирует их в LocalPackages/ с сохранением иерархии <id>/<version>/.
    Результат — локальный источник пакетов для offline-restore.

    Сначала нужно выполнить online-restore, чтобы все пакеты попали
    в глобальный кэш:
        1. pwsh scripts\setup\enable-online-restore.ps1
        2. dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force
        3. pwsh scripts\setup\fill-local-packages.ps1

.PARAMETER Clean
    Очистить LocalPackages/ перед копированием (снести все подпапки).
    ВНИМАНИЕ: деструктивная операция, делает резервную копию.

.PARAMETER DryRun
    Только показать, что будет скопировано, без реального копирования.

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts\setup\fill-local-packages.ps1

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts\setup\fill-local-packages.ps1 -Clean

.NOTES
    © 2026 RuChating (iilmchat) · IIChatTools v1.1.1
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    $global = Join-Path $env:USERPROFILE '.nuget\packages'
    $local  = Join-Path $repoRoot 'LocalPackages'

    if (-not (Test-Path $global)) {
        throw "Глобальный NuGet-кэш не найден: $global"
    }

    # Проверка .gitignore
    if (Test-Path (Join-Path $repoRoot '.gitignore')) {
        $ignored = git check-ignore $local 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[!] LocalPackages/ НЕ в .gitignore — добавьте правило!" -ForegroundColor Red
        }
    }

    # Clean: снести содержимое с резервной копией
    if ($Clean -and (Test-Path $local) -and -not $DryRun) {
        $backup = "$local`_backup_$(Get-Date -f yyyyMMdd_HHmmss)"
        Write-Host "[i] Резервная копия: $backup" -ForegroundColor Yellow
        Move-Item $local $backup
    }

    if (-not (Test-Path $local) -and -not $DryRun) {
        New-Item -ItemType Directory -Path $local -Force | Out-Null
    }

    # Сбор .nupkg из глобального кэша
    $nupkgs = Get-ChildItem $global -Recurse -Filter *.nupkg -File -ErrorAction SilentlyContinue
    Write-Host "[i] Найдено .nupkg в глобальном кэше: $($nupkgs.Count)" -ForegroundColor Cyan

    $copied = 0
    $skipped = 0
    foreach ($nupkg in $nupkgs) {
        # Относительный путь: <id>/<version>/<id>.<version>.nupkg
        $rel = $nupkg.FullName.Substring($global.Length).TrimStart('\', '/')
        $target = Join-Path $local $rel

        if (Test-Path $target) {
            $skipped++
            continue
        }

        if ($DryRun) {
            Write-Host "  [dry] $rel" -ForegroundColor DarkGray
            $copied++
            continue
        }

        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item $nupkg.FullName $target -Force
        $copied++
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "Скопировано: $copied" -ForegroundColor Green
    Write-Host "Уже было:    $skipped" -ForegroundColor Gray
    Write-Host "Всего:       $($nupkgs.Count)" -ForegroundColor Cyan
    Write-Host "Каталог:     $local" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan

    if (-not $DryRun) {
        # Размер итоговой папки
        $sizeMb = [math]::Round((Get-ChildItem $local -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object Length -Sum).Sum / 1MB, 1)
        Write-Host "Размер LocalPackages: $sizeMb МБ" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Следующий шаг — проверка offline-restore:" -ForegroundColor Cyan
        Write-Host "  dotnet restore IIChatTools.sln --verbosity minimal"
    }
}
finally {
    Pop-Location
}