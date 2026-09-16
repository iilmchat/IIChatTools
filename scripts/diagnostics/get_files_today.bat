@echo off
setlocal
echo Starting get_files_today.bat...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $today = (Get-Date).Date; $updatesDir = Join-Path $PWD 'UPDATES'; if (Test-Path $updatesDir) { $subDirs = Get-ChildItem $updatesDir -Directory | Where-Object { $_.Name -notmatch '\D' } | ForEach-Object { [int]$_.Name }; $next = if ($subDirs) { ($subDirs | Measure-Object -Maximum).Maximum + 1 } else { 1 } } else { New-Item -ItemType Directory -Path $updatesDir -Force | Out-Null; $next = 1 }; $destRoot = Join-Path $updatesDir $next.ToString(); Write-Host ('Destination folder: ' + $destRoot); $excludeDirs = @('.git', '.vs', 'UPDATES', 'node_modules', '__pycache__', '.idea', '.vscode', 'bin', 'obj'); $files = Get-ChildItem -Recurse -File | Where-Object { $path = $_.FullName; $exclude = $false; foreach ($dir in $excludeDirs) { if ($path -match ('\\' + $dir + '\\')) { $exclude = $true; break } }; -not $exclude }; $count = 0; foreach ($file in $files) { if ($file.CreationTime.Date -eq $today -or $file.LastWriteTime.Date -eq $today) { $relativePath = $file.FullName.Substring($PWD.Path.Length + 1); $destFile = Join-Path $destRoot $relativePath; $destDir = Split-Path $destFile -Parent; if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }; Copy-Item -Path $file.FullName -Destination $destFile -Force; $count++; Write-Host ('Copied: ' + $relativePath) } }; Write-Host ('Total files copied: ' + $count) }"

echo.
echo Done.
pause