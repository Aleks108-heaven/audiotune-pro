<#
  Signs AudioTune Pro's own binaries and the MSI with an Authenticode code-signing certificate,
  so Windows SmartScreen shows a verified publisher instead of "unknown publisher".

  Usage (from the repo root, after `dotnet publish ... -o CI-artefact` and `wix build ...`):
    # certificate file
    ./installer/sign.ps1 -PfxPath C:\keys\audiotune.pfx -PfxPassword (Read-Host -AsSecureString)
    # or a certificate already in your Windows certificate store (e.g. a hardware token)
    ./installer/sign.ps1 -Thumbprint <SHA1 thumbprint>

  Order matters: the binaries are signed BEFORE `wix build` packs them into the MSI, then the MSI
  itself is signed. Re-run `wix build` between the two calls (see -Target). The CI release workflow
  (.github/workflows/release.yml) does exactly this when its signing secrets are configured.
  Never commit a .pfx or password; both are ignored by .gitignore.
#>
[CmdletBinding()]
param(
    [ValidateSet('Binaries', 'Msi')] [string] $Target = 'Binaries',
    [string] $PfxPath,
    [securestring] $PfxPassword,
    [string] $Thumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Find signtool.exe from the Windows SDK (present on GitHub's windows runners and with Visual Studio).
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK ("Windows SDK Signing Tools for Desktop Apps").' }

$files = if ($Target -eq 'Msi') {
    Get-ChildItem (Join-Path $root 'CI-artefact') -Filter 'AudioTunePro-Setup.msi'
} else {
    # Only our own code; third-party DLLs (NAudio, ...) keep their publishers' signatures/unsigned state.
    Get-ChildItem (Join-Path $root 'CI-artefact') -Include 'AudioTunePro.exe', 'AudioTunePro.dll', 'AudioTunePro.Core.dll' -Recurse
}
if (-not $files) { throw "Nothing to sign for target '$Target'. Build first (see README, 'Installer')." }

$common = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/d', 'AudioTune Pro')
if ($Thumbprint) {
    $common += @('/sha1', $Thumbprint)
} elseif ($PfxPath) {
    if (-not $PfxPassword) { throw '-PfxPassword is required with -PfxPath.' }
    $plain = [System.Net.NetworkCredential]::new('', $PfxPassword).Password
    $common += @('/f', $PfxPath, '/p', $plain)
} else {
    throw 'Provide -PfxPath/-PfxPassword or -Thumbprint.'
}

foreach ($f in $files) {
    & $signtool @common $f.FullName
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $($f.Name)" }
    & $signtool verify /pa $f.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $($f.Name)" }
    Write-Host "Signed and verified: $($f.Name)"
}
