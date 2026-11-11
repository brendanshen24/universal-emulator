# Installs the re-signed Universal Emulator UWP package (.appx) on Windows 10/11 (x64).
# Usage from PowerShell:
#   powershell -ExecutionPolicy Bypass -File .\Install.ps1

$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$CerPath = Join-Path $Here "UniversalEmulator_1.1.3.0_x64.cer"
$AppxPath = Join-Path $Here "UniversalEmulator_1.1.3.0_x64.appx"

$Cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CerPath)
$Thumbprint = $Cert.Thumbprint
$AlreadyTrusted = Test-Path "Cert:\LocalMachine\TrustedPeople\$Thumbprint"

if (-not $AlreadyTrusted) {
    $IsAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
    Write-Host "1. Trusting signing certificate ($Thumbprint) in LocalMachine\TrustedPeople..."
    if ($IsAdmin) {
        Import-Certificate -FilePath $CerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
    } else {
        $EscapedCer = $CerPath.Replace("'", "''")
        $proc = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList @(
            "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-Command", "Import-Certificate -FilePath '$EscapedCer' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
        )
        if ($proc.ExitCode -ne 0) {
            throw "Failed to import certificate into LocalMachine\TrustedPeople (exit $($proc.ExitCode))."
        }
    }
} else {
    Write-Host "1. Signing certificate ($Thumbprint) is already trusted."
}

Write-Host "2. Checking declared UWP framework dependencies..."
$RequiredDeps = @(
    @{ Name = "Microsoft.Advertising.Xaml"; Min = [Version]"10.0.1603.0" },
    @{ Name = "Microsoft.VCLibs.140.00"; Min = [Version]"14.0.24123.0" },
    @{ Name = "Microsoft.NET.Native.Framework.1.3"; Min = [Version]"1.3.24201.0" },
    @{ Name = "Microsoft.NET.Native.Runtime.1.4"; Min = [Version]"1.4.24201.0" }
)
$Missing = @()
foreach ($dep in $RequiredDeps) {
    $found = Get-AppxPackage -Name $dep.Name -ErrorAction SilentlyContinue |
        Where-Object { ($_.Architecture -eq "X64" -or $_.Architecture -eq "Neutral") -and ([Version]$_.Version -ge $dep.Min) }
    if (-not $found) {
        $Missing += "$($dep.Name) (>= $($dep.Min))"
    }
}
if ($Missing.Count -gt 0) {
    Write-Warning "The following Microsoft framework dependencies declared in AppxManifest.xml were not found for this user:"
    $Missing | ForEach-Object { Write-Warning "  - $_" }
    Write-Warning "Add-AppxPackage will still attempt installation (matching frameworks may already be provisioned/staged). If it fails with 0x80073CF3, pass -DependencyPath with those x64 framework .appx files."
}

Write-Host "3. Installing UniversalEmulator_1.1.3.0_x64.appx..."
Add-AppxPackage -Path $AppxPath
Write-Host "Done! Universal Emulator has been installed." -ForegroundColor Green
