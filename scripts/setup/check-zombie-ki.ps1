<#
.SYNOPSIS
    Аудит «Зомби-KI»: сверяет количество KI с количеством распознанных статусов.

.DESCRIPTION
    Скрипт считает **уникальные номера** `KI-XXX` (суффиксные секции
    `KI-110a` / `KI-110b` относятся к одному номеру `KI-110`) и сверяет их
    с суммой статусов.

    Основной проход — толерантный:
      * emoji-префиксы перед статусом (`✅ Fixed`, `🟡 In Progress`);
      * legacy-статусы `Implemented` и `Resolved` приравниваются к `Fixed`.

    Дополнительно (ключ -Detailed) выводится разбор по секциям и проверка
    дублей по полному ID (`KI-031` vs `KI-031a` — разные ID).

.PARAMETER File
    Путь к KNOWN_ISSUES.md. По умолчанию — docs/KNOWN_ISSUES.md относительно корня репозитория.

.PARAMETER Detailed
    Показать расширенную диагностику (статусы, секции, дубликаты).

.PARAMETER Strict
    Исторический «строгий» проход: считает вхождения `**Статус:** <Status>`
    по всему файлу (может давать ложные срабатывания на emoji и тексте).

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/check-zombie-ki.ps1

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/check-zombie-ki.ps1 -Detailed
#>

[CmdletBinding()]
param(
    [string]$File = (Join-Path $PSScriptRoot '..\..\docs\KNOWN_ISSUES.md'),
    [switch]$Detailed,
    [switch]$Strict
)

$resolved = Resolve-Path -LiteralPath $File -ErrorAction SilentlyContinue
if (-not $resolved) {
    Write-Host "Файл не найден: $File" -ForegroundColor Red
    exit 1
}

$file = $resolved.Path
$content = Get-Content $file -Raw

function Get-CanonicalStatus([string]$line) {
    if (-not $line) { return $null }
    $m = [regex]::Match($line, '\*\*Статус:\*\*\s*([^|]*)')
    if (-not $m.Success) { return $null }

    $norm = $m.Groups[1].Value
    $norm = $norm -replace '[\*✅✔️🟡🟠🔴🟢]', '' -replace '\s+', ' '
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
        '^Implemented'     { return 'Fixed' }   # legacy → Fixed
        '^Resolved'        { return 'Fixed' }   # legacy → Fixed
        default            { return "UNKNOWN:$norm" }
    }
}

# --- 1. По-секционный разбор ---
$sections = [regex]::Split($content, '(?m)(?=^### KI-)') | Where-Object { $_ -match '^### KI-' }
$rows = @()
foreach ($sec in $sections) {
    $m = [regex]::Match($sec, '^### (KI-\d+)([a-z]?)')
    if (-not $m.Success) { continue }

    $number = $m.Groups[1].Value
    $suffix = $m.Groups[2].Value
    $statusLine = ($sec -split "`n" | Where-Object { $_ -match '\*\*Статус:\*\*' } | Select-Object -First 1)

    $rows += [pscustomobject]@{
        Number = $number
        Id     = "$number$suffix"
        Status = Get-CanonicalStatus $statusLine
    }
}

# Один KI = один уникальный номер; KI-110a/b считаются один раз.
$unique = @()
foreach ($group in ($rows | Group-Object Number)) {
    $first = $group.Group[0]
    $distinct = $group.Group.Status | Select-Object -Unique
    if ($distinct.Count -gt 1) {
        Write-Host ("ВНИМАНИЕ: у {0} разные статусы: {1}" -f $group.Name, ($distinct -join ', ')) -ForegroundColor Yellow
    }
    $unique += [pscustomobject]@{
        Number = $group.Name
        Ids    = ($group.Group.Id -join ', ')
        Status = $first.Status
    }
}

$statuses = @('Fixed', 'In Progress', 'Planned', 'Deferred', 'Documented', 'Open', "Won't Fix", 'Partially Fixed')
$results = [ordered]@{}
foreach ($status in $statuses) { $results[$status] = 0 }
foreach ($row in $unique) {
    if ($results.Contains($row.Status)) { $results[$row.Status]++ }
}

$total = $unique.Count
$sum = ($results.Values | Measure-Object -Sum).Sum

Write-Host "Файл:             $file"
Write-Host "Всего KI:         $total (уникальных номеров)"
Write-Host "Секций ### KI-:   $($rows.Count)"
Write-Host "Сумма статусов:   $sum"
if ($total -ne $sum) {
    Write-Host "ВНИМАНИЕ: Зомби-KI! Есть KI без распознанного статуса." -ForegroundColor Red
}

# --- 2. Исторический строгий проход (по желанию) ---
if ($Strict) {
    Write-Host ''
    Write-Host '--- Строгий проход (вхождения по всему файлу) ---'
    $strictSum = 0
    foreach ($status in $statuses) {
        $pattern = [regex]::Escape("**Статус:** $status")
        $count = ([regex]::Matches($content, $pattern)).Count
        $strictSum += $count
        Write-Host ("  {0,-16} {1,3}" -f $status, $count)
    }
    Write-Host "  Сумма strict:     $strictSum (заголовков: $($rows.Count))"
}

# --- 3. Расширенная диагностика ---
if ($Detailed) {
    Write-Host ''
    Write-Host '--- Расширенная диагностика ---'
    foreach ($status in $statuses) {
        Write-Host ("  {0,-16} {1,3}" -f $status, $results[$status])
    }

    $unknown = $unique | Where-Object { $statuses -notcontains $_.Status }
    if ($unknown) {
        Write-Host 'KI с нераспознанным статусом:' -ForegroundColor Yellow
        foreach ($u in $unknown) { Write-Host ("  {0} — {1}" -f $u.Number, $u.Status) }
    }

    $dupes = $rows | Group-Object Id | Where-Object Count -gt 1
    if ($dupes) {
        Write-Host 'Дубли по полному ID:' -ForegroundColor Yellow
        foreach ($d in $dupes) { Write-Host ("  {0} — {1} секции" -f $d.Name, $d.Count) }
    }
    else {
        Write-Host 'Дублей по полному ID нет.'
    }

    $multi = $unique | Where-Object { $_.Ids -match ',' }
    if ($multi) {
        Write-Host 'Суффиксные группы (один номер, несколько секций):'
        foreach ($g in $multi) { Write-Host ("  {0} — {1}" -f $g.Number, $g.Ids) }
    }

    $strictMeta = 0
    foreach ($row in $rows) {
        $line = ($sections | Where-Object { $_ -match "^### $([regex]::Escape($row.Id))\b" } | Select-Object -First 1)
        if ($null -eq $line) { continue }
        $statusLine = ($line -split "`n" | Where-Object { $_ -match '\*\*Статус:\*\*' } | Select-Object -First 1)
        foreach ($s in $statuses) {
            if ($statusLine -match [regex]::Escape("**Статус:** $s")) { $strictMeta++; break }
        }
    }
    Write-Host "Строгих статус-строк (без emoji/legacy): $strictMeta из $($rows.Count)"
}
