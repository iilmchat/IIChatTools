# Path to NuGetWebDownloader.exe
$nugetDownloader = "G:\AI\NugetWebDownloader-1.0.0\NuGetWebDownloader.exe"

# Folder for all .nupkg files
$outputDir = "G:\AI\IIChatTools\LocalPackages"

# Temp folder for downloads
$tempDir = "$env:TEMP\NuGetDownloads"

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

# Download each package
foreach ($pkg in $packages.GetEnumerator()) {
    $id = $pkg.Key
    $ver = $pkg.Value
    Write-Host "Downloading $id $ver ..." -ForegroundColor Green
    Set-Location $tempDir
    & $nugetDownloader $id $ver
}

# Copy all .nupkg to local folder
Write-Host ""
Write-Host "Copying .nupkg to $outputDir ..." -ForegroundColor Yellow
Get-ChildItem -Path $tempDir -Filter "*.nupkg" | ForEach-Object {
    Copy-Item $_.FullName -Destination $outputDir -Force
}

Write-Host ""
Write-Host "Done! All packages copied to $outputDir" -ForegroundColor Green