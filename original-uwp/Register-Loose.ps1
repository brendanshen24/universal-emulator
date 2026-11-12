# Installs the original Universal Emulator 1.1.3.0 UWP by registering its unpacked files (no signature needed).
# Both .appx files in this repo are broken (original-uwp: invalid block map; repackage: bad signature digest),
# but the files inside repackage\*.appx are intact, so this unpacks them and registers the manifest directly.
#
# Requires Developer Mode: Settings > Update & Security > For developers > Developer Mode.
# Run: powershell -ExecutionPolicy Bypass -File .\Register-Loose.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$appx = Join-Path $repo 'repackage\UniversalEmulator_1.1.3.0_x64.appx'
$dst = Join-Path $env:LOCALAPPDATA 'UniversalEmulator-UWP'

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*\x64\*' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe not found (install the Windows 10/11 SDK).' }

Write-Host "1. Unpacking to $dst ..."
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
& $makeappx.FullName unpack /p $appx /d $dst /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx unpack failed ($LASTEXITCODE)." }
Remove-Item (Join-Path $dst 'AppxSignature.p7x'), (Join-Path $dst 'AppxBlockMap.xml'), (Join-Path $dst '[[]Content_Types[]].xml') -ErrorAction SilentlyContinue
# UWP apps run in an AppContainer and can only read their install folder if ALL APPLICATION PACKAGES (S-1-15-2-1) may.
icacls $dst /grant '*S-1-15-2-1:(OI)(CI)(RX)' /T /Q | Out-Null

Write-Host '2. Registering the app ...'
Add-AppxPackage -Register (Join-Path $dst 'AppxManifest.xml')
Get-AppxPackage 50446Nesbox.16735B73F29AD | Select-Object Name, Version, InstallLocation
Write-Host 'Done! Launch "Universal Emulator" from the Start menu.' -ForegroundColor Green
