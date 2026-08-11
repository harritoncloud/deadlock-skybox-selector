[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repositoryRoot "source"
$unpackedRoot = Join-Path $repositoryRoot "unpacked"
$assetRoot = Join-Path $unpackedRoot "assets"
$runtimeRoot = Join-Path $unpackedRoot "runtime"
$manifestPath = Join-Path $assetRoot "manifest.json"

foreach ($path in @(
    $manifestPath,
    (Join-Path $sourceRoot "launcher\Program.cs"),
    (Join-Path $sourceRoot "gameinfo-installer\Program.cs"),
    (Join-Path $sourceRoot "runtime\SkyboxSelector.cmd"),
    (Join-Path $sourceRoot "runtime\select-skybox.ps1"),
    (Join-Path $sourceRoot "config\gameinfo.gi"),
    (Join-Path $unpackedRoot "config\gameinfo.gi"),
    (Join-Path $runtimeRoot "runtime-checksums.sha256")
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $path"
    }
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$variants = @($manifest.variants)
if ($variants.Count -ne 32) {
    throw "Expected 32 variants, found $($variants.Count)."
}
if (@($variants | Where-Object category -eq "anime").Count -ne 13) {
    throw "Anime variant count is not 13."
}
if (@($variants | Where-Object category -eq "realistic").Count -ne 19) {
    throw "Realistic variant count is not 19."
}

foreach ($variant in $variants) {
    $relativePath = ([string]$variant.entry).Replace('/', '\')
    $path = Join-Path $assetRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Variant is missing: $relativePath"
    }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne [long]$variant.bytes) {
        throw "Variant size mismatch: $relativePath"
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne ([string]$variant.sha256).ToLowerInvariant()) {
        throw "Variant hash mismatch: $relativePath"
    }
}

$sourceScriptHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot "runtime\select-skybox.ps1") -Algorithm SHA256).Hash
$unpackedScriptHash = (Get-FileHash -LiteralPath (Join-Path $runtimeRoot "select-skybox.ps1") -Algorithm SHA256).Hash
if ($sourceScriptHash -ne $unpackedScriptHash) {
    throw "Readable selector script differs from the embedded copy."
}
$sourceCmdHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot "runtime\SkyboxSelector.cmd") -Algorithm SHA256).Hash
$unpackedCmdHash = (Get-FileHash -LiteralPath (Join-Path $runtimeRoot "SkyboxSelector.cmd") -Algorithm SHA256).Hash
if ($sourceCmdHash -ne $unpackedCmdHash) {
    throw "Readable command wrapper differs from the embedded copy."
}
$sourceConfigHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot "config\gameinfo.gi") -Algorithm SHA256).Hash
$unpackedConfigHash = (Get-FileHash -LiteralPath (Join-Path $unpackedRoot "config\gameinfo.gi") -Algorithm SHA256).Hash
if ($sourceConfigHash -ne $unpackedConfigHash) {
    throw "Readable GameInfo differs from the embedded copy."
}
$configText = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot "config\gameinfo.gi")
if ($configText -notmatch '(?im)^\s*citadel_show_survey\s+"false"') {
    throw "GameInfo does not disable the playtester survey."
}

$checksumLines = Get-Content -LiteralPath (Join-Path $runtimeRoot "runtime-checksums.sha256")
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') {
        throw "Invalid runtime checksum line: $line"
    }
    $path = Join-Path $runtimeRoot $Matches[2]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Runtime payload is missing: $($Matches[2])"
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Matches[1].ToLowerInvariant()) {
        throw "Runtime payload hash mismatch: $($Matches[2])"
    }
}

$ignoredRoots = @(
    ([IO.Path]::GetFullPath((Join-Path $repositoryRoot ".build")).TrimEnd('\') + '\'),
    ([IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist")).TrimEnd('\') + '\'),
    ([IO.Path]::GetFullPath((Join-Path $repositoryRoot ".git")).TrimEnd('\') + '\')
)
$allFiles = @(
    Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File | Where-Object {
        $fullPath = [IO.Path]::GetFullPath($_.FullName)
        -not @($ignoredRoots | Where-Object {
            $fullPath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
        }).Count
    }
)
$largestFile = $allFiles | Sort-Object Length -Descending | Select-Object -First 1
$totalBytes = ($allFiles | Measure-Object Length -Sum).Sum
if ($largestFile.Length -gt 100MB) {
    throw "A file exceeds GitHub's 100 MiB regular Git limit: $($largestFile.FullName)"
}

Write-Host "Verification passed."
Write-Host "Variants: $($variants.Count)"
Write-Host "Files: $($allFiles.Count)"
Write-Host "Total MiB: $([Math]::Round($totalBytes / 1MB, 2))"
Write-Host "Largest file: $($largestFile.FullName) ($([Math]::Round($largestFile.Length / 1MB, 2)) MiB)"
