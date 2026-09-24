# Folder for all .nupkg files
$outputDir = "G:\AI\IIChatTools\LocalPackages"
$tempDir = "$env:TEMP\NuGetDownloads"

if (!(Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# === PROXY SETUP ===
# Получаем системный прокси (тот же, что использует браузер)
$proxy = [System.Net.WebRequest]::GetSystemWebProxy()
# Включаем использование учётных данных Windows (NTLM/Kerberos)
$proxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials

# Также установим глобально для .NET
[System.Net.WebRequest]::DefaultWebProxy = $proxy
[System.Net.WebRequest]::DefaultWebProxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials

# Проверяем, что прокси определён
try {
    $testUri = [System.Uri]"https://www.nuget.org/"
    $proxyUri = $proxy.GetProxy($testUri)
    if ($proxyUri -ne $testUri) {
        Write-Host "Proxy detected: $proxyUri" -ForegroundColor Yellow
    } else {
        Write-Host "No proxy detected (direct connection)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Proxy check failed: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# Missing packages (same as before)
$missing = @(
    @("Microsoft.NETCore.Platforms", "3.1.0"),
    @("Microsoft.NETCore.Targets", "3.1.0"),
    @("System.Runtime", "4.3.1"),
    @("NETStandard.Library", "2.0.3"),
    @("runtime.win-arm64.runtime.native.System.Data.SqlClient.sni", "4.7.0"),
    @("runtime.win-x64.runtime.native.System.Data.SqlClient.sni", "4.7.0"),
    @("runtime.win-x86.runtime.native.System.Data.SqlClient.sni", "4.7.0"),
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

    # Используем WebClient с явно заданным прокси и учётными данными
    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.Proxy = $proxy
        $webClient.Proxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials
        $webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")
        $webClient.DownloadFile($url, $outFile)
        Write-Host "  -> OK: $id.$ver.nupkg" -ForegroundColor DarkGreen
        return $true
    } catch {
        Write-Host "  -> WebClient failed: $($_.Exception.Message)" -ForegroundColor Red

        # Fallback: пробуем через curl.exe (использует системный прокси Windows)
        Write-Host "  -> Trying curl.exe fallback..." -ForegroundColor DarkYellow
        try {
            & curl.exe -s -L -o $outFile --proxy-ntlm $url
            if ((Test-Path $outFile) -and (Get-Item $outFile).Length -gt 0) {
                Write-Host "  -> OK via curl: $id.$ver.nupkg" -ForegroundColor DarkGreen
                return $true
            } else {
                Write-Host "  -> curl.exe returned empty file" -ForegroundColor Red
                return $false
            }
        } catch {
            Write-Host "  -> curl.exe failed: $($_.Exception.Message)" -ForegroundColor Red
            return $false
        }
    }
}

$ok = 0
$skip = 0
$fail = 0

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
    }

    Start-Sleep -Seconds 3
}

Write-Host ""
Write-Host "Copying to $outputDir ..." -ForegroundColor Yellow
Get-ChildItem -Path $tempDir -Filter "*.nupkg" -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName -Destination $outputDir -Force
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host "Downloaded: $ok" -ForegroundColor Green
Write-Host "Skipped:    $skip" -ForegroundColor Gray
Write-Host "Failed:     $fail" -ForegroundColor Red