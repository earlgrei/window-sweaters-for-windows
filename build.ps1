# Builds Window Sweaters for Windows into .\publish\WindowSweaters.exe
#
#   .\build.ps1                 framework-dependent (~300 KB, needs the .NET 9 Desktop Runtime)
#   .\build.ps1 -SelfContained  bundles the runtime (larger, runs on any Windows 10/11 x64 PC)
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Test-Path assets\app.ico)) {
    dotnet build -c Release | Out-Null
    New-Item -ItemType Directory -Force assets | Out-Null
    & .\bin\Release\net9.0-windows\WindowSweaters.exe --write-icon assets\app.ico
}

$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
Write-Host "`nBuilt publish\WindowSweaters.exe"
