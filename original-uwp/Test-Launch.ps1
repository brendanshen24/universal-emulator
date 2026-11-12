# Diagnoses why the registered Universal Emulator won't launch (0x80270254 "does not support the contract").
# Registers a second copy under a different package identity (no Store name, no PhoneIdentity) and launches both.
# Run: powershell -ExecutionPolicy Bypass -File .\Test-Launch.ps1

$ErrorActionPreference = 'Stop'
$src = Join-Path $env:LOCALAPPDATA 'UniversalEmulator-UWP'
$dst = Join-Path $env:LOCALAPPDATA 'UniversalEmulator-UWP-test'
$testName = 'Nesbox.UniversalEmulator.Test'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IApplicationActivationManager {
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
}
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
public class ApplicationActivationManager {}
public static class Act {
    public static string Run(string aumid) {
        try {
            uint pid;
            ((IApplicationActivationManager)new ApplicationActivationManager()).ActivateApplication(aumid, null, 0, out pid);
            System.Threading.Thread.Sleep(5000);
            bool alive = false;
            try { alive = !System.Diagnostics.Process.GetProcessById((int)pid).HasExited; } catch {}
            return "started pid " + pid + (alive ? " (still running after 5s)" : " (exited within 5s)");
        } catch (Exception e) { return "FAILED: " + e.Message; }
    }
}
'@

Write-Host '1. Launching the installed app (original identity)...'
Write-Host ('   ' + [Act]::Run('50446Nesbox.16735B73F29AD_8y3dwps6jawp4!App'))

Write-Host "2. Registering a test copy as $testName ..."
if (Test-Path $dst) {
    Get-AppxPackage $testName | Remove-AppxPackage
    Remove-Item $dst -Recurse -Force
}
Copy-Item $src $dst -Recurse
$m = Join-Path $dst 'AppxManifest.xml'
$x = [IO.File]::ReadAllText($m)
$x = $x.Replace('Name="50446Nesbox.16735B73F29AD"', "Name=`"$testName`"")
$x = [regex]::Replace($x, '<mp:PhoneIdentity[^>]*/>', '')
$x = $x.Replace('<DisplayName>Universal Emulator</DisplayName>', '<DisplayName>Universal Emulator (test)</DisplayName>')
[IO.File]::WriteAllText($m, $x, (New-Object Text.UTF8Encoding $true))
icacls $dst /grant '*S-1-15-2-1:(OI)(CI)(RX)' /T /Q | Out-Null
Add-AppxPackage -Register $m
$pfn = (Get-AppxPackage $testName).PackageFamilyName

Write-Host '3. Launching the test copy...'
Write-Host ('   ' + [Act]::Run("$pfn!App"))
