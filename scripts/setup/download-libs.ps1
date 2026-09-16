# ============================================================
# download-libs.ps1 — загрузка клиентских библиотек IIChatTools
# через корпоративный прокси с Basic-авторизацией.
# ============================================================
# © 2026 RuChating (iilmchat) · IIChatTools v1.0
# ============================================================

# --- Настройки прокси ---
$proxyAddress = "http://222.1.20.1:8080"
$proxyUser    = "nikiforov"
$proxyPass    = "6989"

# --- Куда скачивать ---
$wwwrootLib = "G:\AI\IIChatTools\IIChatTools.API\wwwroot\lib"

# Создаём корневую папку, если её нет
if (!(Test-Path $wwwrootLib)) {
    New-Item -ItemType Directory -Path $wwwrootLib -Force | Out-Null
}

# --- Список файлов для скачивания ---
# Формат: URL, относительный путь внутри wwwroot/lib
$files = @(
    @{
        Url  = "https://code.jquery.com/jquery-3.7.1.min.js"
        Path = "jquery\dist\jquery.min.js"
    },
    @{
        Url  = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css"
        Path = "bootstrap\dist\css\bootstrap.min.css"
    },
    @{
        Url  = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/js/bootstrap.bundle.min.js"
        Path = "bootstrap\dist\js\bootstrap.bundle.min.js"
    },
    @{
        Url  = "https://cdn.jsdelivr.net/npm/jquery-validation@1.19.5/dist/jquery.validate.min.js"
        Path = "jquery-validation\dist\jquery.validate.min.js"
    },
    @{
        Url  = "https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/dist/jquery.validate.unobtrusive.min.js"
        Path = "jquery-validation-unobtrusive\dist\jquery.validate.unobtrusive.min.js"
    }
)

# --- Загрузка ---
$successCount = 0
$failCount = 0

foreach ($file in $files) {
    $outFile = Join-Path $wwwrootLib $file.Path
    $outDir  = Split-Path $outFile -Parent

    if (!(Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }

    Write-Host "Downloading $($file.Url)" -ForegroundColor Green

    $curlArgs = @(
        "-s", "-L",
        "--ssl-no-revoke",
        "--proxy-basic",
        "-U", "$proxyUser`:$proxyPass",
        "--proxy", $proxyAddress,
        "-o", $outFile,
        $file.Url
    )
    & curl.exe @curlArgs

    # Проверка результата
    if ((Test-Path $outFile) -and (Get-Item $outFile).Length -gt 500) {
        $size = [math]::Round((Get-Item $outFile).Length / 1KB, 1)
        Write-Host "  -> OK: $($file.Path) ($size KB)" -ForegroundColor DarkGreen
        $successCount++
    } else {
        Write-Host "  -> FAILED: $($file.Path)" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Успешно: $successCount из $($files.Count)" -ForegroundColor Cyan
Write-Host "Ошибок:  $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Cyan" })
Write-Host "Папка:   $wwwrootLib" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan