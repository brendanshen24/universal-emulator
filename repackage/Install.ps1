# Reinstalls the original "Universal Emulator" 1.1.3.0 (NESBox) Store app from the re-signed package next to this script.
# The app itself is unchanged; it is re-signed with UniversalEmulator.cer, a certificate made for this package only
# (its private key was deleted after signing). Package identity is the same as the Store version.
#
# Run: right-click Install.ps1 > Run with PowerShell
#  or: powershell -ExecutionPolicy Bypass -File .\Install.ps1
# Missing framework packages (.appx/.msix) can be put in a "Dependencies" folder next to this script.

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$cer = Join-Path $here 'UniversalEmulator.cer'
$appx = Join-Path $here 'UniversalEmulator_1.1.3.0_x64.appx'
$packageName = '50446Nesbox.16735B73F29AD'

# 1. Trust the certificate machine-wide (Trusted People). This is the only step that needs administrator rights.
$thumbprint = (New-Object Security.Cryptography.X509Certificates.X509Certificate2 $cer).Thumbprint
if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$thumbprint")) {
    Write-Host 'Trusting the package certificate (Windows will ask for administrator approval)...'
    $command = "Import-Certificate -FilePath '$($cer -replace "'", "''")' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -EncodedCommand $encoded"
    if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$thumbprint")) {
        throw 'The certificate was not added to Trusted People, so Windows would refuse the package.'
    }
}

# 2. Never replace an existing copy here: uninstalling a UWP app deletes its saved games.
$existing = Get-AppxPackage -Name $packageName
if ($existing) {
    throw ("Universal Emulator is already installed ($($existing.PackageFullName), signed by $($existing.SignatureKind)). " +
        "To replace it, first back up %LOCALAPPDATA%\Packages\$($existing.PackageFamilyName), then uninstall it from Settings > Apps.")
}

# 3. The frameworks the app was built against (from its AppxManifest.xml).
$dependencyFiles = @(Get-ChildItem -Path (Join-Path $here 'Dependencies') -Recurse -Include *.appx, *.msix -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName })
$required = [ordered]@{
    'Microsoft.VCLibs.140.00'            = '14.0.24123.0'
    'Microsoft.NET.Native.Framework.1.3' = '1.3.24201.0'
    'Microsoft.NET.Native.Runtime.1.4'   = '1.4.24201.0'
    'Microsoft.Advertising.Xaml'         = '10.0.1603.0'
}
$missing = @()
foreach ($name in $required.Keys) {
    $installed = Get-AppxPackage -Name $name | Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]$required[$name] }
    $supplied = $dependencyFiles | Where-Object { (Split-Path $_ -Leaf) -like "$name*" }
    if (-not $installed -and -not $supplied) { $missing += "$name (x64, $($required[$name]) or later)" }
}
if ($missing) {
    Write-Warning ("These framework packages are not installed, so Windows may refuse the app:`n  " + ($missing -join "`n  ") +
        "`nPut their .appx files in a 'Dependencies' folder next to this script and run it again.")
}

# 4. Install.
Write-Host 'Installing Universal Emulator...'
if ($dependencyFiles) { Add-AppxPackage -Path $appx -DependencyPath $dependencyFiles }
else { Add-AppxPackage -Path $appx }
Write-Host 'Done. Start "Universal Emulator" from the Start menu.'
