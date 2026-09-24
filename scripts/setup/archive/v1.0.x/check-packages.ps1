# Folder with local packages
$localPackagesDir = "G:\AI\IIChatTools\LocalPackages"

# Solution folder to scan
$solutionDir = "G:\AI\IIChatTools"

# Collect all PackageReference from all .csproj
$csprojFiles = Get-ChildItem -Path $solutionDir -Filter "*.csproj" -Recurse
$required = @{}

foreach ($csproj in $csprojFiles) {
    [xml]$xml = Get-Content $csproj.FullName
    $packageRefs = $xml.SelectNodes("//PackageReference")
    foreach ($ref in $packageRefs) {
        $id = $ref.Include
        $version = $ref.Version
        if ($id -and $version) {
            $required[$id] = $version
        }
    }
}

# Collect existing .nupkg files in LocalPackages
$existing = @{}
if (Test-Path $localPackagesDir) {
    Get-ChildItem -Path $localPackagesDir -Filter "*.nupkg" -Recurse | ForEach-Object {
        # File name format: PackageId.Version.nupkg
        $name = $_.BaseName
        $existing[$name] = $_.FullName
    }
}

Write-Host ""
Write-Host "=== REQUIRED PACKAGES ===" -ForegroundColor Cyan
Write-Host "Total: $($required.Count)"
Write-Host ""

$missing = @()
$found = @()

foreach ($pkg in $required.GetEnumerator() | Sort-Object Name) {
    $id = $pkg.Key
    $ver = $pkg.Value
    $expectedName = "$id.$ver"
    # .nupkg files use lowercase, and version may be normalized
    $match = $existing.Keys | Where-Object { $_ -ieq $expectedName }

    if ($match) {
        Write-Host "[OK]      $id $ver" -ForegroundColor Green
        $found += "$id $ver"
    } else {
        Write-Host "[MISSING] $id $ver" -ForegroundColor Red
        $missing += "$id $ver"
    }
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host "Found:   $($found.Count)" -ForegroundColor Green
Write-Host "Missing: $($missing.Count)" -ForegroundColor Red
Write-Host ""

if ($missing.Count -gt 0) {
    Write-Host "Missing packages:" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  - $_" }
    Write-Host ""
    Write-Host "To download all missing packages at once:" -ForegroundColor Yellow
    $missing | ForEach-Object {
        $parts = $_ -split ' '
        Write-Host "NuGetWebDownloader.exe $($parts[0]) $($parts[1])"
    }
} else {
    Write-Host "All required packages are present. You can run dotnet restore." -ForegroundColor Green
}