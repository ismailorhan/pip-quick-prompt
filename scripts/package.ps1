# Builds the plugin in Release and packs dist\QuickPrompt.lplug4 (Windows).
#   .\scripts\package.ps1            build + pack
#   .\scripts\package.ps1 -Install   build + pack + open the package in Logi Options+
param([switch]$Install)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet tool restore | Out-Null
dotnet build src/QuickPrompt.Plugin -c Release -p:SkipPluginLink=true -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

New-Item -ItemType Directory -Force dist | Out-Null
Remove-Item -ErrorAction SilentlyContinue dist/QuickPrompt.lplug4
dotnet logiplugintool pack bin/plugin/Release dist/QuickPrompt.lplug4
dotnet logiplugintool verify dist/QuickPrompt.lplug4
Write-Host "Package: dist\QuickPrompt.lplug4"

if ($Install) { Start-Process (Resolve-Path dist/QuickPrompt.lplug4) }
