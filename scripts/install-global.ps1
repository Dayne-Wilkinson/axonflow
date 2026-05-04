#Requires -Version 5.1
<#
.SYNOPSIS
  Pack AxonFlow and install or update the global dotnet tool (axonflow on PATH).
.DESCRIPTION
  Invoke as .\scripts\install-global.ps1 from the repo root, or .\install-global.ps1 from scripts\
  (PowerShell needs the .\ prefix). If execution policy blocks unsigned scripts, run install-global.cmd
  or: powershell -ExecutionPolicy Bypass -File <this script>. The script cd's to the repo root.
  Reads Version from src/AxonFlow/AxonFlow.csproj.
#>
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$csproj = Join-Path $repoRoot 'src/AxonFlow/AxonFlow.csproj'
if (-not (Test-Path $csproj)) {
  Write-Error "Expected csproj at $csproj (run from repo root)."
}

[xml]$projXml = Get-Content $csproj
$version = $projXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
  Write-Error "Could not read Version from $csproj"
}

$artifacts = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

Write-Host "Packing AxonFlow $version -> $artifacts"
dotnet pack $csproj -c Release -o $artifacts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pkg = "AxonFlow"
Write-Host "Updating or installing global tool $pkg $version (from $artifacts)"
dotnet tool update --global $pkg --add-source $artifacts --version $version
if ($LASTEXITCODE -ne 0) {
  Write-Host "dotnet tool update exited $LASTEXITCODE; trying install (first-time or different feed)." -ForegroundColor DarkYellow
  dotnet tool install --global $pkg --add-source $artifacts --version $version
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Done. Ensure ~/.dotnet/tools (or %USERPROFILE%\.dotnet\tools) is on PATH, then run: axonflow --help"
