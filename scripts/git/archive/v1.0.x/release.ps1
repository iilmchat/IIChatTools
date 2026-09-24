<#
.SYNOPSIS
    Готовит релиз: обновляет CHANGELOG.md, версию, создаёт тег.
.DESCRIPTION
    Переносит записи из [Unreleased] в [X.Y.Z] с датой.
    Обновляет AppVersion и Directory.Build.props.
    Создаёт git-тег.
.PARAMETER Version
    Версия релиза (например, "1.2.0").
.PARAMETER SkipTag
    Не создавать git-тег (только правки файлов).
.EXAMPLE
    pwsh release.ps1 -Version 1.1.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$SkipTag
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    $date = Get-Date -Format 'yyyy-MM-dd'
    $versionHeader = "## [$Version] — $date"
    $unreleasedHeader = "## [Unreleased]"

    # --- 1. Обновить CHANGELOG ---
    $changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
    if (-not (Test-Path $changelogPath)) { throw "CHANGELOG.md не найден" }

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $text = [System.IO.File]::ReadAllText($changelogPath, $utf8)

    if ($text -match [regex]::Escape($versionHeader)) {
        Write-Host "Версия $Version уже есть в CHANGELOG" -ForegroundColor Yellow
    } else {
        # Заменяем первую секцию [Unreleased] на [Unreleased]\n\n---\n\n## [Version]
        $pattern = "(?s)($unreleasedHeader\r?\n.*?\r?\n---\r?\n)"
        $replacement = "`$1`r`n$versionHeader`r`n"
        $text = [regex]::Replace($text, $pattern, $replacement, 1)
        [System.IO.File]::WriteAllText($changelogPath, $text, $utf8)
        Write-Host "CHANGELOG.md: добавлена секция $versionHeader" -ForegroundColor Green
    }

    # --- 2. Обновить Directory.Build.props ---
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    $props = [System.IO.File]::ReadAllText($propsPath, $utf8)
    $props = $props -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    $props = $props -replace '<Copyright>[^<]+</Copyright>', "<Copyright>© 2026 RuChating (iilmchat) · IIChatTools v$Version</Copyright>"
    [System.IO.File]::WriteAllText($propsPath, $props, $utf8)
    Write-Host "Directory.Build.props: Version = $Version" -ForegroundColor Green

    # --- 3. Коммит ---
    git add CHANGELOG.md Directory.Build.props
    git commit -m "chore(release): v$Version — CHANGELOG и версия"

    # --- 4. Тег ---
    if (-not $SkipTag) {
        git tag -a "v$Version" -m "IIChatTools v$Version"
        Write-Host "Тег v$Version создан" -ForegroundColor Green
        Write-Host "Push: git push origin main && git push origin v$Version" -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}