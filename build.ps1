# Builds Window Sweaters for Windows into .\publish\WindowSweaters.exe
#
#   .\build.ps1          standalone (~100 MB): the .NET runtime is inside the exe, so it runs on
#                        any Windows 10/11 x64 PC with nothing to install. This is the one to share.
#   .\build.ps1 -Small   framework-dependent (~300 KB): needs the .NET 9 Desktop Runtime installed.
param([switch]$Small)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Test-Path assets\app.ico)) {
    dotnet build -c Release | Out-Null
    New-Item -ItemType Directory -Force assets | Out-Null
    & .\bin\Release\net9.0-windows\WindowSweaters.exe --write-icon assets\app.ico
}

if (Test-Path publish) { Remove-Item -Recurse -Force publish }

# Uncompressed on purpose: compressing the single file halves its size but roughly
# doubles the app's memory use for as long as it runs.
$selfContained = if ($Small) { 'false' } else { 'true' }
dotnet publish -c Release -r win-x64 --self-contained $selfContained `
    -p:PublishSingleFile=true -p:DebugType=none -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Get-Item publish\WindowSweaters.exe
$kind = if ($Small) { 'needs the .NET 9 Desktop Runtime' } else { 'standalone, nothing to install' }
Write-Host ("`nBuilt publish\WindowSweaters.exe ({0:N1} MB, {1})" -f ($exe.Length / 1MB), $kind)
