<#
.SYNOPSIS
    Финализирует релиз v1.1.0 после merge PR в main.
.DESCRIPTION
    1. Переключается на main и подтягивает изменения.
    2. Ставит тег v1.1.0.
    3. Пушит тег.
    4. Удаляет рабочую ветку (локально и на remote).
    5. Печатает итоговую сводку.
.NOTES
    Запускать ТОЛЬКО после успешного merge PR на GitHub.
#>

[CmdletBinding()]
param(
    [string]$MainBranch = 'main',
    [string]$WorkingBranch = 'feature/net10-migration',
    [string]$TagName = 'v1.1.0',
    [string]$TagMessage = 'IIChatTools v1.1.0 — миграция на .NET 10 LTS',
    [switch]$SkipBranchCleanup
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    Write-Host "== 1. Переключение на $MainBranch ==" -ForegroundColor Cyan
    git checkout $MainBranch
    git pull origin $MainBranch

    Write-Host "`n== 2. Проверка последнего коммита ==" -ForegroundColor Cyan
    git log --oneline -3

    $confirm = Read-Host "`nПоследний коммит — это merge миграции? (y/N)"
    if ($confirm -ne 'y') {
        throw "Отменено пользователем."
    }

    Write-Host "`n== 3. Создание тега $TagName ==" -ForegroundColor Cyan
    # Проверка, что тег ещё не существует
    $existing = git tag -l $TagName
    if ($existing) {
        Write-Host "  [!] Тег $TagName уже существует — пропускаю." -ForegroundColor Yellow
    } else {
        git tag -a $TagName -m $TagMessage
        git push origin $TagName
        Write-Host "  [OK] Тег $TagName создан и запушен." -ForegroundColor Green
    }

    if (-not $SkipBranchCleanup) {
        Write-Host "`n== 4. Удаление рабочей ветки ==" -ForegroundColor Cyan
        $branchExists = git branch --list $WorkingBranch
        if ($branchExists) {
            git branch -d $WorkingBranch
        } else {
            Write-Host "  Локальная ветка $WorkingBranch уже удалена." -ForegroundColor Yellow
        }

        $remoteBranch = git ls-remote --heads origin $WorkingBranch
        if ($remoteBranch) {
            git push origin --delete $WorkingBranch
            Write-Host "  [OK] Remote-ветка $WorkingBranch удалена." -ForegroundColor Green
        } else {
            Write-Host "  Remote-ветка $WorkingBranch уже удалена." -ForegroundColor Yellow
        }
    }

    Write-Host "`n== Итог ==" -ForegroundColor Green
    Write-Host "Релиз $TagName опубликован." -ForegroundColor Green
    Write-Host "Ветка $MainBranch синхронизирована с origin." -ForegroundColor Green
    Write-Host "Резервная ветка: preNet10 (сохраняется как архив)" -ForegroundColor Gray
    Write-Host "Тег до миграции: v1.0.2-pre-net10" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Дальнейшие шаги:" -ForegroundColor Cyan
    Write-Host "  1. Создать GitHub Release для тега $TagName (веб-интерфейс)."
    Write-Host "  2. Начать задачи v1.1.x (KI-022, KI-036, KI-037, rate limiting, ...)."
}
finally {
    Pop-Location
}