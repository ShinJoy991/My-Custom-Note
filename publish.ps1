# Builds a release of MyCustomNote that runs on a Windows PC without .NET installed.
# Run it from a PowerShell window in this folder:   .\publish.ps1
# Result: the folder .\publish (start MyCustomNote.exe) and a zip next to it, ready to upload to a GitHub release.
# Run the tests first (dotnet test MyCustomNote.slnx, or Visual Studio: Test > Run All Tests): this script does not run them.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = '.\MyCustomNote\MyCustomNote.csproj'
$version = (([xml](Get-Content $project)).Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> found in $project." }

if (Test-Path .\publish) { Remove-Item .\publish -Recurse -Force }

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o .\publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

$zip = ".\MyCustomNote-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path .\publish\* -DestinationPath $zip

Write-Host ""
Write-Host "Done."
Write-Host "  Program: .\publish\MyCustomNote.exe"
Write-Host "  Zip for a GitHub release: $zip"
Write-Host "  SHA256: $((Get-FileHash $zip -Algorithm SHA256).Hash)"
Write-Host "The app keeps its data in %LOCALAPPDATA%\MyCustomNote (notes.db, settings.json, security.json): back that folder up."
