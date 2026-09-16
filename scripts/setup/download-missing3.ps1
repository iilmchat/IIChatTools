# Folder for all .nupkg files
$outputDir = "G:\AI\IIChatTools\LocalPackages"
$tempDir = "$env:TEMP\NuGetDownloads"

if (!(Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# === PROXY SETUP ===
$proxyAddress = "http://222.1.20.1:8080"
Write-Host "Using proxy: $proxyAddress" -ForegroundColor Yellow

# Настраиваем глобальный прокси для .NET с учётными данными Windows
[System.Net.WebRequest]::DefaultWebProxy = New-Object System.Net.WebProxy($proxyAddress)
[System.Net.WebRequest]::DefaultWebProxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials

# Также настраиваем для HttpClient (используется в .NET Core)
[System.Net.Http.HttpClient]::DefaultProxy = New-Object System.Net.WebProxy($proxyAddress)
[System.Net.Http.HttpClient]::DefaultProxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials

Write-Host "Proxy configured with DefaultCredentials" -ForegroundColor Green
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

    # Способ 1: HttpClient с явным прокси и DefaultCredentials
    try {
        Add-Type -AssemblyName System.Net.Http
        $handler = New-Object System.Net.Http.HttpClientHandler
        $handler.Proxy = New-Object System.Net.WebProxy($proxyAddress)
        $handler.Proxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials
        $handler.UseProxy = $true
        $handler.UseDefaultCredentials = $true

        $client = New-Object System.Net.Http.HttpClient($handler)
        $client.Timeout = [TimeSpan]::FromSeconds(60)

        $response = $client.GetAsync($url).Result
        $response.EnsureSuccessStatusCode()
        $bytes = $response.Content.ReadAsByteArrayAsync().Result
        [System.IO.File]::WriteAllBytes($outFile, $bytes)

        Write-Host "  -> OK via HttpClient: $id.$ver.nupkg" -ForegroundColor DarkGreen
        return $true
    } catch {
        Write-Host "  -> HttpClient failed: $($_.Exception.Message)" -ForegroundColor Red
    }

    # Способ 2: curl.exe с NTLM и текущими учётными данными
    Write-Host "  -> Trying curl.exe with --proxy-ntlm..." -ForegroundColor DarkYellow
    try {
        # -U : (двоеточие без логина) заставляет curl использовать текущие Windows-учётные данные для NTLM
        $curlArgs = @(
            "-s", "-L",
            "--proxy", $proxyAddress,
            "--proxy-ntlm",
            "-U", ":",
            "-o", $outFile,
            $url
        )
        & curl.exe @curlArgs

        if ((Test-Path $outFile) -and (Get-Item $outFile).Length -gt 1000) {
            Write-Host "  -> OK via curl.exe: $id.$ver.nupkg" -ForegroundColor DarkGreen
            return $true
        } else {
            Write-Host "  -> curl.exe returned empty or tiny file" -ForegroundColor Red
            if (Test-Path $outFile) { Remove-Item $outFile -Force }
            return $false
        }
    } catch {
        Write-Host "  -> curl.exe failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
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