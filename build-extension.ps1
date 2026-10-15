param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath
)

$ErrorActionPreference = "Stop"
$manifest = Join-Path $SourceDirectory "manifest.json"
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "Browser extension manifest not found: $manifest"
}

$resolvedSource = (Resolve-Path -LiteralPath $SourceDirectory).Path
$destinationDirectory = Split-Path -Parent $ArchivePath
if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
}

Compress-Archive -Path (Join-Path $resolvedSource "*") `
    -DestinationPath $ArchivePath -Force
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw "Failed to create browser extension archive: $ArchivePath"
}
