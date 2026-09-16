# Folder for all .nupkg files
$outputDir = "G:\AI\IIChatTools\LocalPackages"

# Temp folder for downloads
$tempDir = "$env:TEMP\NuGetDownloads"

if (!(Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# Missing packages with their versions
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
    try {
        Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing -ErrorAction Stop
        Write-Host "  -> OK: $id.$ver.nupkg" -ForegroundColor DarkGreen
        return $true
    } catch {
        Write-Host "  -> FAILED: $($_.Exception.Message)" -ForegroundColor Red
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

# Copy all .nupkg to output
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
Write-Host ""
Write-Host "Now run: dotnet restore G:\AI\IIChatTools\IIChatTools.sln" -ForegroundColor Cyan