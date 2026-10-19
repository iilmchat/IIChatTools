<#
.SYNOPSIS
    Проверка относительных Markdown-ссылок в README.md и docs/**/*.md.

.DESCRIPTION
    Ищет ссылки вида [текст](путь), пропускает http(s), mailto и якоря (#...),
    проверяет существование файла относительно каталога самого .md-файла.
    Якоря внутри файла не проверяются (только путь до #).

.PARAMETER Root
    Корень репозитория. По умолчанию — два уровня выше scripts/setup.

.PARAMETER IncludeScripts
    Дополнительно проверить scripts/**/*.md.

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/check-doc-links.ps1

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/check-doc-links.ps1 -IncludeScripts
#>

[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..\..'),
    [switch]$IncludeScripts
)

$rootPath = (Resolve-Path -LiteralPath $Root).Path

$files = @()
$readme = Join-Path $rootPath 'README.md'
if (Test-Path -LiteralPath $readme) { $files += $readme }
$files += Get-ChildItem -LiteralPath (Join-Path $rootPath 'docs') -Recurse -Filter '*.md' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName }
if ($IncludeScripts) {
    $files += Get-ChildItem -LiteralPath (Join-Path $rootPath 'scripts') -Recurse -Filter '*.md' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName }
}

$broken = New-Object System.Collections.Generic.List[object]
$checked = 0

foreach ($file in $files) {
    $dir = Split-Path -Path $file -Parent
    $text = Get-Content -LiteralPath $file -Raw
    if ($null -eq $text) { continue }

    foreach ($m in [regex]::Matches($text, '\[[^\]]*\]\(([^)]+)\)')) {
        $target = $m.Groups[1].Value.Trim()
        if ($target -match '^(https?:|mailto:|#)') { continue }

        $path = ($target -split '#')[0]
        if ([string]::IsNullOrWhiteSpace($path)) { continue }

        $checked++
        $full = Join-Path $dir $path
        if (-not (Test-Path -LiteralPath $full)) {
            $broken.Add([pscustomobject]@{
                File   = $file.Substring($rootPath.Length).TrimStart('\', '/')
                Target = $target
            })
        }
    }
}

Write-Host "Проверено файлов: $($files.Count), ссылок: $checked"
if ($broken.Count -eq 0) {
    Write-Host 'Битых относительных ссылок не найдено.' -ForegroundColor Green
    exit 0
}

Write-Host "Битых ссылок: $($broken.Count)" -ForegroundColor Red
$broken | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
exit 1
