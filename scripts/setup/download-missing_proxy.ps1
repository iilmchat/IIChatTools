# ============================================================
# download-missing.ps1
# Скачивает недостающие NuGet-пакеты напрямую через прокси
# ============================================================

# Folder for all .nupkg files
$outputDir = "G:\AI\IIChatTools\LocalPackages"

# Temp folder for downloads
$tempDir = "$env:TEMP\NuGetDownloads"

# Create folders if not exist
if (!(Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# ============================================================
# PROXY SETUP
# ============================================================
$proxyAddress = "http://222.1.20.1:8080"

# !!! ЗАМЕНИТЕ НА ВАШИ ДАННЫЕ, ЕСЛИ ОНИ ОТЛИЧАЮТСЯ !!!
$proxyUser = "proxy_user"
$proxyPass = "CHANGE_ME"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Proxy:      $proxyAddress" -ForegroundColor Yellow
Write-Host "Proxy user: $proxyUser" -ForegroundColor Yellow
Write-Host "Output dir: $outputDir" -ForegroundColor Yellow
Write-Host "Temp dir:   $tempDir" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# MISSING PACKAGES
# ============================================================
$missing = @(
    @("Microsoft.NETCore.Platforms", "3.1.0"),
    @("Microsoft.NETCore.Targets", "3.1.0"),
    @("System.Runtime", "4.3.1"),
    @("NETStandard.Library", "2.0.3"),
    @("runtime.win-arm64.runtime.native.System.Data.SqlClient.sni", "4.4.0"),
    @("runtime.win-x64.runtime.native.System.Data.SqlClient.sni", "4.4.0"),
    @("runtime.win-x86.runtime.native.System.Data.SqlClient.sni", "4.4.0"),
    @("System.ComponentModel.TypeConverter", "4.3.0"),
    @("System.Dynamic.Runtime", "4.3.0"),
    @("System.Runtime.Serialization.Formatters", "4.3.0"),
    @("System.Runtime.Serialization.Json", "4.3.0"),
    @("System.Runtime.Serialization.Primitives", "4.3.0"),
    @("System.Security.Cryptography.X509Certificates", "4.3.0"),
    @("System.Security.SecureString", "4.3.0"),
    @("System.Xml.XDocument", "4.3.0"),
    @("System.Xml.XmlDocument", "4.3.0"),
    @("System.Net.NameResolution", "4.3.0"),
    @("System.Runtime.Caching", "4.7.0"),
    @("Microsoft.AspNetCore.Localization", "3.1.32"),
    @("Microsoft.AspNetCore.Localization.Routing", "3.1.32"),
    @("Microsoft.AspNetCore.Razor.Language", "3.1.32"),
    @("Microsoft.IdentityModel.Protocols", "6.7.1"),
    @("Microsoft.CodeAnalysis.CSharp", "3.8.0"),
    @("Microsoft.CodeAnalysis.Common", "3.8.0")
)

# ============================================================
# HELPER FUNCTIONS
# ============================================================

function Test-PackageExists {
    param([string]$id, [string]$ver)
    $expectedName = "$id.$ver"
    $found = Get-ChildItem -Path $outputDir -Filter "*.nupkg" -ErrorAction SilentlyContinue |
             Where-Object { $_.BaseName -ieq $expectedName }
    return ($null -ne $found)
}

function Download-Direct {
    param([string]$id, [string]$ver)

    $url = "https://www.nuget.org/api/v2/package/$id/$ver"
    $outFile = Join-Path $tempDir "$id.$ver.nupkg"

    # curl.exe с Basic-аутентификацией на прокси и отключённой проверкой отзыва сертификата
    $curlArgs = @(
        "-s", "-L",
        "--ssl-no-revoke",
        "--proxy-basic",
        "-U", "$proxyUser`:$proxyPass",
        "--proxy", $proxyAddress,
        "-o", $outFile,
        $url
    )

    $result = & curl.exe @curlArgs 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0 -and (Test-Path $outFile) -and (Get-Item $outFile).Length -gt 1000) {
        Write-Host "  -> OK: $id.$ver.nupkg" -ForegroundColor DarkGreen
        return $true
    } else {
        Write-Host "  -> curl.exe failed (exit=$exitCode)" -ForegroundColor Red
        if ($result) { Write-Host "     $result" -ForegroundColor DarkGray }
        if (Test-Path $outFile) { Remove-Item $outFile -Force }
        return $false
    }
}

# ============================================================
# MAIN LOOP
# ============================================================
$ok = 0
$skip = 0
$fail = 0
$failedPackages = @()

$i = 0
foreach ($pkg in $missing) {
    $i++
    $id = $pkg[0]
    $ver = $pkg[1]
    Write-Host "[$i/$($missing.Count)] $id $ver" -ForegroundColor Green

    if (Test-PackageExists -id $id -ver $ver) {
        Write-Host "  -> already present, skipping" -ForegroundColor DarkGray
        $skip++
        continue
    }

    if (Download-Direct -id $id -ver $ver) {
        $ok++
    } else {
        $fail++
        $failedPackages += "$id $ver"
    }

    Start-Sleep -Seconds 2
}

# ============================================================
# COPY TO OUTPUT FOLDER
# ============================================================
Write-Host ""
Write-Host "Copying all .nupkg to $outputDir ..." -ForegroundColor Yellow
Get-ChildItem -Path $tempDir -Filter "*.nupkg" -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName -Destination $outputDir -Force
}

# ============================================================
# SUMMARY
# ============================================================
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host "Downloaded: $ok" -ForegroundColor Green
Write-Host "Skipped:    $skip" -ForegroundColor Gray
Write-Host "Failed:     $fail" -ForegroundColor Red
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

if ($failedPackages.Count -gt 0) {
    Write-Host "Failed packages:" -ForegroundColor Red
    $failedPackages | ForEach-Object { Write-Host "  - $_" }
    Write-Host ""
    Write-Host "Try downloading them manually via browser:" -ForegroundColor Yellow
    $failedPackages | ForEach-Object {
        $parts = $_ -split ' '
        Write-Host "  https://www.nuget.org/api/v2/package/$($parts[0])/$($parts[1])"
    }
} else {
    Write-Host "All packages are ready in $outputDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Next step:" -ForegroundColor Cyan
Write-Host "  dotnet restore G:\AI\IIChatTools\IIChatTools.sln" -ForegroundColor White