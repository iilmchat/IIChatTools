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
    # ---- runtime.win-* (4.4.0) ----
    @("runtime.win-arm64.runtime.native.System.Data.SqlClient.sni", "4.4.0"),
    @("runtime.win-x64.runtime.native.System.Data.SqlClient.sni", "4.4.0"),
    @("runtime.win-x86.runtime.native.System.Data.SqlClient.sni", "4.4.0"),

    # ---- runtime.native.* ----
    @("runtime.native.System", "4.3.0"),
    @("runtime.native.System.Net.Http", "4.3.0"),
    @("runtime.native.System.Security.Cryptography.OpenSsl", "4.3.0"),

    # ---- System.* 4.3.0 ----
    @("System.Collections", "4.3.0"),
    @("System.Collections.NonGeneric", "4.3.0"),
    @("System.Collections.Specialized", "4.3.0"),
    @("System.ComponentModel", "4.3.0"),
    @("System.ComponentModel.Primitives", "4.3.0"),
    @("System.Diagnostics.Debug", "4.3.0"),
    @("System.Diagnostics.Tools", "4.3.0"),
    @("System.Diagnostics.Tracing", "4.3.0"),
    @("System.Globalization", "4.3.0"),
    @("System.Globalization.Calendars", "4.3.0"),
    @("System.IO", "4.3.0"),
    @("System.IO.FileSystem", "4.3.0"),
    @("System.IO.FileSystem.Primitives", "4.3.0"),
    @("System.Linq", "4.3.0"),
    @("System.Linq.Expressions", "4.3.0"),
    @("System.Net.Primitives", "4.3.0"),
    @("System.ObjectModel", "4.3.0"),
    @("System.Private.DataContractSerialization", "4.3.0"),
    @("System.Reflection", "4.3.0"),
    @("System.Reflection.Extensions", "4.3.0"),
    @("System.Reflection.Primitives", "4.3.0"),
    @("System.Reflection.TypeExtensions", "4.3.0"),
    @("System.Resources.ResourceManager", "4.3.0"),
    @("System.Runtime.Extensions", "4.3.1"),
    @("System.Runtime.Handles", "4.3.0"),
    @("System.Runtime.InteropServices", "4.3.0"),
    @("System.Runtime.Numerics", "4.3.0"),
    @("System.Security.Cryptography.Algorithms", "4.3.0"),
    @("System.Security.Cryptography.Csp", "4.3.0"),
    @("System.Security.Cryptography.Encoding", "4.3.0"),
    @("System.Security.Cryptography.OpenSsl", "4.3.0"),
    @("System.Security.Cryptography.Primitives", "4.3.0"),
    @("System.Threading", "4.3.0"),
    @("System.Threading.Tasks", "4.3.0"),
    @("System.Xml.ReaderWriter", "4.3.0"),

    # ---- Specific versions from NU1102/NU1605 ----
    @("Microsoft.AspNetCore.Localization", "3.1.32"),
    @("Microsoft.AspNetCore.Localization.Routing", "3.1.32"),
    @("Microsoft.Win32.Registry", "4.7.0"),
    @("System.Collections.Immutable", "5.0.0"),
    @("System.Reflection.Metadata", "5.0.0"),
    @("System.Text.Encoding.CodePages", "4.5.1"),
    @("System.Configuration.ConfigurationManager", "4.7.0"),
    @("Microsoft.CodeAnalysis.Analyzers", "3.3.4"),

    # ---- xunit (meta + dependencies) ----
    @("xunit", "2.4.2"),
    @("xunit.core", "2.4.2"),
    @("xunit.assert", "2.4.2"),
    @("xunit.analyzers", "1.0.0"),

    # ---- coverlet (meta-package, downloaded earlier via fallback) ----
    @("coverlet.collector", "6.0.2")
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