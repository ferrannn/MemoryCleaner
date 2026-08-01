# Generate the SHA-256 checksum sidecar file for a MemoryCleaner release.
#
# Since v1.3.7 the client's auto-update REQUIRES this sidecar (fail-closed):
# if a release lacks it, every client refuses to auto-update. So run this
# script before uploading each exe, and upload the generated .sha256 too.
#
# Usage (run once per artifact to upload):
#   powershell -ExecutionPolicy Bypass -File .\tools\make-sha256.ps1 -Path .\publish-lean\MemoryCleaner.exe
#
# Output format matches exactly what UpdateChecker.ChecksumVerifier.TryParse
# accepts: lowercase 64-char hex + two spaces + filename, ASCII, no BOM,
# no trailing newline.
#
# NOTE: ASCII-only file (no Chinese comments) so it parses under Windows
# PowerShell 5.1, which reads BOM-less .ps1 as ANSI.
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$full = (Resolve-Path $Path).Path
$hash = (Get-FileHash -Path $full -Algorithm SHA256).Hash.ToLowerInvariant()
$line = "$hash  $([IO.Path]::GetFileName($full))"

[IO.File]::WriteAllText("$full.sha256", $line, [Text.Encoding]::ASCII)

Write-Host "Generated: $full.sha256"
Write-Host $line
