# Installs Universal Emulator (desktop rebuild of the original UWP) as a regular Windows app for the current user.
#   - publishes a self-contained single-file exe (no .NET install needed) plus an "Original" folder holding the
#     original Store app's NesboxPanels.dll, which the exe uses for emulation, video, sound, and .7z archives
#   - copies it to %LOCALAPPDATA%\Programs\Universal Emulator
#   - adds Start menu + desktop shortcuts and an "Apps & features" uninstall entry
# Run:        powershell -ExecutionPolicy Bypass -File .\Install-Windows.ps1
# Uninstall:  powershell -ExecutionPolicy Bypass -File .\Install-Windows.ps1 -Uninstall   (or via Apps & features)

param([switch]$Uninstall, [switch]$NoDesktopShortcut)

$ErrorActionPreference = 'Stop'
$name = 'Universal Emulator'
$dest = Join-Path $env:LOCALAPPDATA "Programs\$name"
$exe = Join-Path $dest 'UniversalEmulator.exe'
$startLnk = Join-Path ([Environment]::GetFolderPath('Programs')) "$name.lnk"
$deskLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk"
$uninstKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\UniversalEmulator'

Get-Process UniversalEmulator -ErrorAction SilentlyContinue | Stop-Process -Force

if ($Uninstall) {
    Remove-Item $startLnk, $deskLnk -ErrorAction SilentlyContinue
    Remove-Item $uninstKey -Recurse -ErrorAction SilentlyContinue
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Write-Host "$name uninstalled (your games and saves are kept)." -ForegroundColor Green
    return
}

$proj = Join-Path $PSScriptRoot 'UniversalEmulator\UniversalEmulator.csproj'
$publish = Join-Path $PSScriptRoot 'UniversalEmulator\bin\Release\net10.0-desktop\win-x64\publish'

Write-Host '1. Building...'
dotnet publish $proj -c Release -f net10.0-desktop -p:PublishProfile=win-x64 --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Write-Host "2. Installing to $dest ..."
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $publish 'UniversalEmulator.exe') $dest -Force
# The original Store binaries (NesboxPanels.dll, GameController.dll + their UWP C++ runtime and host assembly).
$origDest = Join-Path $dest 'Original'
if (Test-Path $origDest) { Remove-Item $origDest -Recurse -Force }
Copy-Item (Join-Path $publish 'Original') $origDest -Recurse
Copy-Item $MyInvocation.MyCommand.Path (Join-Path $dest 'Uninstall.ps1') -Force

Write-Host '3. Creating shortcuts...'
$shell = New-Object -ComObject WScript.Shell
$links = @($startLnk); if (-not $NoDesktopShortcut) { $links += $deskLnk }
foreach ($l in $links) {
    $s = $shell.CreateShortcut($l)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $dest
    $s.IconLocation = "$exe,0"
    $s.Description = $name
    $s.Save()
}

New-Item $uninstKey -Force | Out-Null
$props = @{
    DisplayName = $name; DisplayVersion = '1.1.3.0'; Publisher = 'Nesbox'
    DisplayIcon = "$exe,0"; InstallLocation = $dest; NoModify = 1; NoRepair = 1
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$dest\Uninstall.ps1`" -Uninstall"
}
foreach ($k in $props.Keys) { Set-ItemProperty $uninstKey -Name $k -Value $props[$k] }

Write-Host "Done! Launch '$name' from the Start menu." -ForegroundColor Green
