<#
.SYNOPSIS
    Builds the Tyler MSI installer (x64) from the Native AOT publish output.

.DESCRIPTION
    This script:
      1. Publishes the app with `dotnet publish -c Release -r win-x64`
         (Native AOT -> self-contained Tyler.exe + QuestPDF support files).
      2. Invokes the WiX v3 toolset (candle + light) to compile
         src/installer/Product.wxs into an MSI.
      3. Outputs artifacts/Tyler-<version>-x64.msi.

    Prerequisites:
      - .NET 10 SDK (for publishing)
      - WiX Toolset v3 (candle.exe + light.exe). Provide the location via
        -WixPath, or add WiX to PATH. A clear error is raised if missing.

.PARAMETER WixPath
    Directory containing candle.exe and light.exe (WiX v3 bin folder).
    Defaults to common install locations; falls back to PATH lookup.

.PARAMETER Configuration
    Build configuration. Defaults to 'Release'.

.PARAMETER Runtime
    Runtime identifier. Defaults to 'win-x64'.

.PARAMETER OutputDir
    Where to place the produced .msi. Defaults to <repo>/artifacts.

.PARAMETER Version
    Optional MSI version (x.y.z). If omitted, it is read from the published
    executable's FileVersion; fallback is "1.0.0".

.EXAMPLE
    .\build-installer.ps1 -WixPath "C:\Program Files (x86)\WiX Toolset v3.11\bin"
#>
[CmdletBinding()]
param(
    [string]$WixPath,
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = $PSScriptRoot
$Project    = Join-Path $RepoRoot 'src\Tyler.csproj'
$WxsFile    = Join-Path $RepoRoot 'src\installer\Product.wxs'
$IcoFile    = Join-Path $RepoRoot 'src\installer\Tyler.ico'
$PublishDir = Join-Path $RepoRoot "src\bin\Release\net10.0\$Runtime\publish"

if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'artifacts' }

# ---------------------------------------------------------------------------
# Locate the WiX v3 toolset.
# ---------------------------------------------------------------------------
function Find-WixExe {
    param([string]$Name)

    if ($WixPath) {
        $p = Join-Path $WixPath $Name
        if (Test-Path $p) { return $p }
    }

    # Common default install locations (documented in README usage note).
    $candidates = @(
        'C:\Program Files (x86)\WiX Toolset v3.11\bin',
        'C:\Program Files (x86)\WiX Toolset v3.14\bin',
        'C:\Program Files (x86)\WiX Toolset v3.10\bin'
    )
    foreach ($c in $candidates) {
        $p = Join-Path $c $Name
        if (Test-Path $p) { return $p }
    }

    # Fall back to PATH.
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    return $null
}

$candle = Find-WixExe 'candle.exe'
$light  = Find-WixExe 'light.exe'

if (-not $candle -or -not $light) {
    throw @"
WiX v3 toolset was not found (candle.exe / light.exe).
Install WiX v3 (https://wixtoolset.org/releases/v3-11-1/) and either:
  - pass -WixPath 'C:\Program Files (x86)\WiX Toolset v3.11\bin', or
  - add that bin folder to your PATH.
"@
}
Write-Host "Using WiX:  $candle"
Write-Host "       :  $light"

# ---------------------------------------------------------------------------
# 1) Publish the app (Native AOT, self-contained, x64).
# ---------------------------------------------------------------------------
Write-Host "Publishing Tyler ($Configuration / $Runtime)..."
& dotnet publish $Project -c $Configuration -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (-not (Test-Path (Join-Path $PublishDir 'Tyler.exe'))) {
    throw "Publish output not found at '$PublishDir'. Expected Tyler.exe there."
}

# ---------------------------------------------------------------------------
# 2) Determine version.
# ---------------------------------------------------------------------------
if (-not $Version) {
    try {
        $vi = (Get-Item (Join-Path $PublishDir 'Tyler.exe')).VersionInfo
        if ($vi.FileVersion) { $Version = $vi.FileVersion }
    } catch { }
}
if (-not $Version) { $Version = '1.0.0' }

# ---------------------------------------------------------------------------
# 3) Compile the MSI with candle + light.
# ---------------------------------------------------------------------------
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$objDir = Join-Path $OutputDir 'obj'
New-Item -ItemType Directory -Path $objDir -Force | Out-Null

$wixobj = Join-Path $objDir 'Product.wixobj'
$msi    = Join-Path $OutputDir "Tyler-$Version-x64.msi"

Write-Host "Compiling WiX source..."
& $candle $WxsFile `
    -dPublishDir="$PublishDir" `
    -dTylerVersion="$Version" `
    -dTylerIcon="$IcoFile" `
    -arch x64 `
    -out $wixobj
if ($LASTEXITCODE -ne 0) { throw "candle failed with exit code $LASTEXITCODE" }

Write-Host "Linking MSI..."
& $light $wixobj `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "light failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "MSI created: $msi"
