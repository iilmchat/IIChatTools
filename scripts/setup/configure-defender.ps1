# Требует прав администратора.
# Добавляет исключения Windows Defender для папки проекта IIChatTools —
# ускоряет первый `dotnet test` после холодной сборки (125s → 2s, см. KI-073).
#
# Использование:
#   1. Открыть PowerShell от имени администратора.
#   2. cd <repo-root>
#   3. .\scripts\setup\configure-defender.ps1

#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Write-Host "Проект: $projectPath" -ForegroundColor Cyan

$paths = @(
    # Проект и его bin/obj
    $projectPath,

    # NuGet-кэш
    "$env:USERPROFILE\.nuget\packages",

    # .NET SDK (главный источник тормозов — здесь живут dotnet.exe, JIT, testhost, vstest.console)
    "C:\Program Files\dotnet",
    "C:\Program Files (x86)\dotnet",

    # Точки установки от пользователя
    "$env:USERPROFILE\.dotnet",
    "$env:LOCALAPPDATA\Microsoft\dotnet",

    # Временные папки (сюда dotnet распаковывает сборки для загрузки)
    "$env:TEMP",
    "$env:LOCALAPPDATA\Temp"
)

$processes = @(
    'dotnet.exe',
    'VBCSCompiler.exe',
    'testhost.exe',
    'MSBuild.exe',
    'vstest.console.exe',
    'vstest.discoveryengine.exe',
    'vstest.executionengine.exe'
)

Write-Host "`nДобавление исключений для путей:" -ForegroundColor Yellow
foreach ($p in $paths) {
    Add-MpPreference -ExclusionPath $p
    Write-Host "  + $p"
}

Write-Host "`nДобавление исключений для процессов:" -ForegroundColor Yellow
foreach ($p in $processes) {
    Add-MpPreference -ExclusionProcess $p
    Write-Host "  + $p"
}

Write-Host "`nТекущие exclusions:" -ForegroundColor Cyan
Get-MpPreference | Select-Object -ExpandProperty ExclusionPath
Get-MpPreference | Select-Object -ExpandProperty ExclusionProcess

Write-Host "`nГотово. Попробуй снова:" -ForegroundColor Green
Write-Host "  cd `"$projectPath`""
Write-Host "  Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force"
Write-Host "  dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force"
Write-Host "  dotnet build IIChatTools.sln --no-restore"
Write-Host "  dotnet test IIChatTools.sln --no-build"
Write-Host "`nПроверка Controlled Folder Access (должно быть 0):" -ForegroundColor Cyan
Get-MpPreference | Select-Object EnableControlledFolderAccess
