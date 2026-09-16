# Path to NuGetWebDownloader.exe
$nugetDownloader = "G:\AI\NugetWebDownloader-1.0.0\NuGetWebDownloader.exe"

# Folder for all .nupkg files
$outputDir = "G:\AI\IIChatTools\LocalPackages"

# Temp folder for downloads
$tempDir = "$env:TEMP\NuGetDownloads"

# Delay between packages (seconds) - to avoid 429
$delayBetweenPackages = 5

# Delay before retry after 429 (seconds)
$delayAfter429 = 30

# Max retries after 429
$maxRetries = 2

# Create folders if not exist
if (!(Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# Find all .csproj files
$csprojFiles = Get-ChildItem -Path "G:\AI\IIChatTools" -Filter "*.csproj" -Recurse

# Collect unique packages and versions
$packages = @{}

foreach ($csproj in $csprojFiles) {
    Write-Host "Scanning: $($csproj.FullName)" -ForegroundColor Cyan
    [xml]$xml = Get-Content $csproj.FullName
    $packageRefs = $xml.SelectNodes("//PackageReference")
    foreach ($ref in $packageRefs) {
        $id = $ref.Include
        $version = $ref.Version
        if ($id -and $version) {
            $packages[$id] = $version
        }
    }
}

Write-Host ""
Write-Host "Found unique packages: $($packages.Count)" -ForegroundColor Yellow
Write-Host ""

# Function to check if package already exists in output folder
function Test-PackageExists {
    param([string]$id, [string]$ver)
    $expectedName = "$id.$ver"
    $found = Get-ChildItem -Path $outputDir -Filter "*.nupkg" -ErrorAction SilentlyContinue |
             Where-Object { $_.BaseName -ieq $expectedName }
    return ($null -ne $found)
}

# Function to download directly via web request (bypasses rate limit)
function Download-Direct {
    param([string]$id, [string]$ver)
    $url = "https://www.nuget.org/api/v2/package/$id/$ver"
    $outFile = Join-Path $tempDir "$id.$ver.nupkg"
    Write-Host "  -> Direct download: $url" -ForegroundColor DarkYellow
    try {
        Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing -ErrorAction Stop
        Write-Host "  -> Saved to: $outFile" -ForegroundColor DarkGreen
        return $true
    } catch {
        Write-Host "  -> Direct download FAILED: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Statistics
$stats = @{
    Skipped = 0
    Downloaded = 0
    DirectFallback = 0
    Failed = 0
}

$failedPackages = @()

# Download each package
$i = 0
foreach ($pkg in $packages.GetEnumerator() | Sort-Object Name) {
    $i++
    $id = $pkg.Key
    $ver = $pkg.Value

    Write-Host "[$i/$($packages.Count)] $id $ver" -ForegroundColor Green

    # Skip if already downloaded
    if (Test-PackageExists -id $id -ver $ver) {
        Write-Host "  -> Already exists, skipping" -ForegroundColor DarkGray
        $stats.Skipped++
        continue
    }

    Set-Location $tempDir

    # Try NuGetWebDownloader with retries
    $success = $false
    $useDirect = $false
    $retry = 0

    while ($retry -le $maxRetries -and -not $success) {
        $output = & $nugetDownloader $id $ver 2>&1 | Out-String

        if ($output -match "429" -or $output -match "Too Many Requests") {
            $retry++
            if ($retry -le $maxRetries) {
                Write-Host "  -> Rate limited (429). Waiting $delayAfter429 sec before retry $retry/$maxRetries..." -ForegroundColor Yellow
                Start-Sleep -Seconds $delayAfter429
            } else {
                Write-Host "  -> Rate limit persists after $maxRetries retries, switching to direct download" -ForegroundColor Yellow
                $useDirect = $true
                break
            }
        }
        elseif ($output -match "No available frameworks" -or $output -match "No framework selected") {
            Write-Host "  -> Meta-package or no frameworks. Switching to direct download" -ForegroundColor Yellow
            $useDirect = $true
            break
        }
        elseif ($output -match "Error:" -or $output -match "Exception") {
            Write-Host "  -> Error from NuGetWebDownloader, switching to direct download" -ForegroundColor Yellow
            $useDirect = $true
            break
        }
        else {
            $success = $true
        }
    }

    # Direct download fallback
    if ($useDirect) {
        $directOk = Download-Direct -id $id -ver $ver
        if ($directOk) {
            $stats.DirectFallback++
        } else {
            $stats.Failed++
            $failedPackages += "$id $ver"
        }
    }
    elseif ($success) {
        $stats.Downloaded++
    }

    # Copy newly downloaded .nupkg files to output folder
    Get-ChildItem -Path $tempDir -Filter "*.nupkg" -ErrorAction SilentlyContinue | ForEach-Object {
        $dest = Join-Path $outputDir $_.Name
        if (!(Test-Path $dest)) {
            Copy-Item $_.FullName -Destination $outputDir -Force
        }
    }

    # Delay between packages to avoid rate limiting
    if ($i -lt $packages.Count) {
        Start-Sleep -Seconds $delayBetweenPackages
    }
}

# Final copy of all .nupkg to local folder
Write-Host ""
Write-Host "Final sync of .nupkg to $outputDir ..." -ForegroundColor Yellow
Get-ChildItem -Path $tempDir -Filter "*.nupkg" -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName -Destination $outputDir -Force
}

# Summary
Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host "Skipped (already present): $($stats.Skipped)" -ForegroundColor Gray
Write-Host "Downloaded via NuGetWebDownloader: $($stats.Downloaded)" -ForegroundColor Green
Write-Host "Downloaded via direct fallback: $($stats.DirectFallback)" -ForegroundColor Yellow
Write-Host "Failed: $($stats.Failed)" -ForegroundColor Red
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
Write-Host "Run: dotnet restore G:\AI\IIChatTools\IIChatTools.sln" -ForegroundColor Cyan