<#
.SYNOPSIS
    D14 / KI-254 / KI-275: генератор сводки статусов KNOWN_ISSUES.md.

.DESCRIPTION
    Считает уникальные номера KI по секциям ### KI-XXX, нормализует статусы
    (emoji, Implemented/Resolved → Fixed) и печатает Markdown-таблицу.
    В режиме -Check сравнивает таблицу с текущим содержимым реестра и
    возвращает ненулевой код, если сводка расходится (для CI).

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/generate-ki-summary.ps1

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/generate-ki-summary.ps1 -Check
#>

[CmdletBinding()]
param(
    [string]$File = (Join-Path $PSScriptRoot '..\..\docs\KNOWN_ISSUES.md'),
    [switch]$Check
)

$resolved = Resolve-Path -LiteralPath $File -ErrorAction SilentlyContinue
if (-not $resolved) {
    Write-Host "Файл не найден: $File" -ForegroundColor Red
    exit 1
}
$content = Get-Content $resolved.Path -Raw

function Get-CanonicalStatus([string]$line) {
    if (-not $line) { return $null }
    $m = [regex]::Match($line, '\*\*Статус:\*\*\s*([^|]*)')
    if (-not $m.Success) { return $null }
    $norm = $m.Groups[1].Value -replace '[\*✅✔️🟡🟠🔴🟢]', '' -replace '\s+', ' '
    $norm = $norm.Trim()
    switch -Regex ($norm) {
        '^Fixed'           { return 'Fixed' }
        '^Partially Fixed' { return 'Partially Fixed' }
        '^Planned'         { return 'Planned' }
        '^Documented'      { return 'Documented' }
        '^Deferred'        { return 'Deferred' }
        "^Won't Fix"       { return "Won't Fix" }
        '^In Progress'     { return 'In Progress' }
        '^Open'            { return 'Open' }
        '^Implemented'     { return 'Fixed' }
        '^Resolved'        { return 'Fixed' }
        default            { return 'Unknown' }
    }
}

$sections = [regex]::Split($content, '(?m)(?=^### KI-)') | Where-Object { $_ -match '^### KI-' }
$rows = @()
foreach ($sec in $sections) {
    $m = [regex]::Match($sec, '^### (KI-\d+)([a-z]?)')
    if (-not $m.Success) { continue }
    $number = $m.Groups[1].Value
    $statusLine = ($sec -split "`n" | Where-Object { $_ -match '\*\*Статус:\*\*' } | Select-Object -First 1)
    $rows += [pscustomobject]@{ Number = $number; Status = Get-CanonicalStatus $statusLine }
}

# Уникальные номера: KI-110a/b — одна запись.
$unique = @($rows | Group-Object Number | ForEach-Object { $_.Group[0] })
$order = @('Fixed', 'Planned', 'Documented', 'Deferred', 'Partially Fixed', 'In Progress', "Won't Fix", 'Open', 'Unknown')
$table = @('| Статус | Кол-во |', '| :--- | ---: |')
foreach ($status in $order) {
    $count = @($unique | Where-Object { $_.Status -eq $status }).Count
    # Все статусы из реестра выводим всегда — в т.ч. с нулём (In Progress).
    if ($status -in @('Fixed', 'Planned', 'Documented', 'Deferred', 'Partially Fixed', 'In Progress', "Won't Fix")) {
        $table += "| $status | $count |"
    }
}
$table += "| **Всего** | $($unique.Count) |"
$tableText = $table -join "`n"

if ($Check) {
    # Ищем текущую таблицу в реестре: строки вида "| Fixed (включая ...) | N |" и т.п.
    $actual = [regex]::Match($content, '(?ms)\| Статус \| Кол-во \|.*?\| \*\*Всего\*\* \| \*{0,2}\d+\*{0,2} \|')
    if (-not $actual.Success) {
        Write-Host 'Сводка не найдена в реестре' -ForegroundColor Red
        exit 2
    }
    # Сравниваем только числа по статусам.
    $expectedCounts = @{}
    foreach ($line in ($tableText -split "`n")) {
        $m = [regex]::Match($line, '^\| (.+?) \| (\d+) \|$')
        if ($m.Success) { $expectedCounts[$m.Groups[1].Value] = [int]$m.Groups[2].Value }
    }
    $mismatch = $false
    foreach ($line in ($actual.Value -split "`n")) {
        $m = [regex]::Match($line, '^\| (.+?) \| (\d+) \|$')
        if (-not $m.Success) { continue }
        $key = $m.Groups[1].Value
        $v = [int]$m.Groups[2].Value
        if ($key -eq 'Статус' -or $key -eq ':---' -or $key -like '**Всего**') { continue }
        # Fixed в реестре имеет пояснение в скобках — сравниваем по префиксу.
        $matchKey = @($expectedCounts.Keys | Where-Object { $key.StartsWith($_) } | Select-Object -First 1)
        if (-not $matchKey -or $expectedCounts[$matchKey[0]] -ne $v) {
            Write-Host ("Расхождение: {0} = {1}, ожидается {2}" -f $key, $v, $(if ($matchKey) { $expectedCounts[$matchKey[0]] } else { '?' })) -ForegroundColor Red
            $mismatch = $true
        }
    }
    if ($mismatch) { exit 3 }
    Write-Host "Сводка актуальна: $($unique.Count) KI" -ForegroundColor Green
    exit 0
}

Write-Output $tableText
