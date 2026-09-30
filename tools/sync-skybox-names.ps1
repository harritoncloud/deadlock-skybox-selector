[CmdletBinding()]
param(
    [switch]$Check
)

# The readable names live in source\skyboxes.json. The asset manifest that ships inside the
# executable carries a copy so the extracted cache is self-describing, and this script is the only
# thing allowed to write that copy. tools\verify.ps1 runs the same comparison and fails the build
# when the two ever drift apart.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$namesPath = Join-Path $repositoryRoot "source\skyboxes.json"
$manifestPath = Join-Path $repositoryRoot "unpacked\assets\manifest.json"
foreach ($path in @($namesPath, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $path"
    }
}

$names = @{}
foreach ($entry in [object[]](Get-Content -Raw -LiteralPath $namesPath | ConvertFrom-Json)) {
    if ($entry.id -eq "vanilla") {
        continue
    }
    if ([string]::IsNullOrWhiteSpace($entry.displayName)) {
        throw "skyboxes.json has no display name for $($entry.id)."
    }
    if ($names.ContainsKey([string]$entry.id)) {
        throw "skyboxes.json lists $($entry.id) twice."
    }
    $names[[string]$entry.id] = [string]$entry.displayName
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$variants = @($manifest.variants)
if ($variants.Count -ne $names.Count) {
    throw "The manifest has $($variants.Count) variants but skyboxes.json names $($names.Count)."
}

$changed = @()
foreach ($variant in $variants) {
    $id = [string]$variant.id
    if (-not $names.ContainsKey($id)) {
        throw "skyboxes.json has no entry for manifest variant $id."
    }
    $expected = $names[$id]
    if ([string]$variant.displayName -ne $expected) {
        $changed += ($id + ": '" + $variant.displayName + "' -> '" + $expected + "'")
        $variant.displayName = $expected
    }
}

if ($Check) {
    if ($changed.Count -gt 0) {
        $changed | ForEach-Object { Write-Host $_ }
        throw "The manifest display names are out of date. Run tools\sync-skybox-names.ps1."
    }
    Write-Host "Manifest display names match skyboxes.json: $($variants.Count) variants."
    exit 0
}

if ($changed.Count -eq 0) {
    Write-Host "Manifest display names already match skyboxes.json."
    exit 0
}

$temporary = $manifestPath + ".names-new"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding UTF8
$rewritten = @((Get-Content -Raw -LiteralPath $temporary | ConvertFrom-Json).variants)
if ($rewritten.Count -ne $variants.Count) {
    Remove-Item -LiteralPath $temporary -Force
    throw "The rewritten manifest lost variants."
}
foreach ($variant in $rewritten) {
    if ([string]$variant.displayName -ne $names[[string]$variant.id]) {
        Remove-Item -LiteralPath $temporary -Force
        throw "The rewritten manifest failed verification: $($variant.id)."
    }
}
Move-Item -LiteralPath $temporary -Destination $manifestPath -Force

$changed | ForEach-Object { Write-Host $_ }
Write-Host "Updated display names: $($changed.Count)."
