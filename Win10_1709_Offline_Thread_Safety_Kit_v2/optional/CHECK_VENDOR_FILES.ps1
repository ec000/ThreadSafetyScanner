#Requires -Version 5.1
# Offline verification of optional official installer files.
param([string]$VendorDir=(Join-Path $PSScriptRoot '..\vendor_installers'))
if (-not (Test-Path -LiteralPath $VendorDir -PathType Container)) { throw "Missing directory: $VendorDir" }
Get-ChildItem -LiteralPath $VendorDir -File | Where-Object { $_.Extension -in @('.exe','.msi','.zip') } | ForEach-Object {
    Write-Host ('FILE: ' + $_.Name)
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    Write-Host ('SHA256: ' + $hash.Hash)
    if ($_.Extension -in @('.exe','.msi')) {
        $sig = Get-AuthenticodeSignature -LiteralPath $_.FullName
        Write-Host ('SIGNATURE STATUS: ' + $sig.Status)
        if ($sig.SignerCertificate) { Write-Host ('SIGNER: ' + $sig.SignerCertificate.Subject) }
    }
    Write-Host ''
}
Write-Host 'Compare the SHA256 hashes with vendor official release/checksum statements.'
