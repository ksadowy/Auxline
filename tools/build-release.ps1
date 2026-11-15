<#
    Builds the Auxline installer: a self-contained Release publish, packed by Inno Setup.
    Used by the release workflow, and locally (needs the .NET 10 SDK and Inno Setup 6). From the repository root:

        powershell -ExecutionPolicy Bypass -File tools/build-release.ps1 -Version 1.2.0

    Output: artifacts/Auxline-Setup-<version>.exe (the publish folder it was made from is next to it).
#>
param(
    # Semantic version, e.g. 1.2.0 or 1.2.0-beta.1 (the release tag without its "v").
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'

# Inno Setup's command-line compiler: on PATH, or where its installer puts it (machine-wide or per user).
function Find-InnoCompiler {
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php (or: choco install innosetup).' }
    return $found
}

# Allowlist: the version ends up in file names, the installer and the assembly metadata.
if ($Version -notmatch '^(?<prefix>\d+\.\d+\.\d+)(-(?<suffix>[0-9A-Za-z][0-9A-Za-z.-]*))?$') {
    throw "Version '$Version' must look like 1.2.0 or 1.2.0-beta.1."
}
$versionPrefix = $Matches['prefix']
$versionSuffix = $Matches['suffix']

$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root $OutputDirectory
$publish = Join-Path $output 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# Self-contained: runs without a separately installed .NET. A folder rather than a single file, so nothing has to be
# extracted at every start. No .pdb files in the installer.
$publishArguments = @(
    'publish', (Join-Path $root 'Auxline/Auxline.csproj'),
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $publish,
    '--source', 'https://api.nuget.org/v3/index.json',
    "-p:VersionPrefix=$versionPrefix",
    '-p:DebugType=None'
)
if ($versionSuffix) { $publishArguments += "-p:VersionSuffix=$versionSuffix" }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

$compiler = Find-InnoCompiler
& $compiler "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$output" (Join-Path $root 'installer/Auxline.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit code $LASTEXITCODE)." }

Join-Path $output "Auxline-Setup-$Version.exe"
