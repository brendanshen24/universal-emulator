param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
$bytes = [IO.File]::ReadAllBytes($Source)
# Latin-1 preserves the byte offset. Match the complete, NUL-terminated import name.
$image = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
$importName = "XINPUTUAP.dll" + [char]0
$offset = $image.IndexOf($importName, [StringComparison]::OrdinalIgnoreCase)
if ($offset -lt 0 -or $image.IndexOf($importName, $offset + 1, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw 'Expected exactly one XINPUTUAP.dll import name in GameController.dll.'
}

$desktopName = [Text.Encoding]::ASCII.GetBytes("XINPUT1_4.dll" + [char]0)
[Array]::Copy($desktopName, 0, $bytes, $offset, $desktopName.Length)
[IO.File]::WriteAllBytes($Destination, $bytes)
