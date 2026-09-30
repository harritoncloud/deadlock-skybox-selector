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

function Assert-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

function Assert-SameFile([string]$Readable, [string]$Extracted, [string]$Description) {
    $readableHash = (Get-FileHash -LiteralPath $Readable -Algorithm SHA256).Hash
    $extractedHash = (Get-FileHash -LiteralPath $Extracted -Algorithm SHA256).Hash
    if ($readableHash -ne $extractedHash) {
        throw "$Description differs from the extracted release copy."
    }
}

function Assert-SkyboxMaterial([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt32() -ne 0x55AA1234 -or $reader.ReadUInt32() -ne 2) {
            throw "Unsupported skybox VPK: $Path"
        }
        $treeSize = $reader.ReadInt32()
        if ($treeSize -le 0 -or $treeSize -gt 1MB) {
            throw "Invalid skybox VPK tree: $Path"
        }
        $reader.ReadBytes(16) | Out-Null
        $tree = [Text.Encoding]::ASCII.GetString($reader.ReadBytes($treeSize))
        if (-not $tree.Contains('dl_midtown_dusk_02') -or $tree.Contains('sky_dl_dusk03')) {
            throw "Skybox VPK targets the wrong Deadlock material: $Path"
        }
    } finally {
        $stream.Dispose()
    }
}

$requiredFiles = @(
    $manifestPath,
    (Join-Path $sourceRoot "launcher\Program.cs"),
    (Join-Path $sourceRoot "launcher\SelectorForm.cs"),
    (Join-Path $sourceRoot "launcher\app.manifest"),
    (Join-Path $sourceRoot "launcher\app.ico"),
    (Join-Path $sourceRoot "gameinfo-installer\Program.cs"),
    (Join-Path $sourceRoot "runtime\SkyboxSelector.cmd"),
    (Join-Path $sourceRoot "runtime\select-skybox.ps1"),
    (Join-Path $sourceRoot "runtime\install-fps-config.ps1"),
    (Join-Path $sourceRoot "runtime\deadlock-fps.cfg"),
    (Join-Path $sourceRoot "config\gameinfo.gi"),
    (Join-Path $unpackedRoot "config\gameinfo.gi"),
    (Join-Path $repositoryRoot "tools\build-base-veil-override.ps1"),
    (Join-Path $runtimeRoot "runtime-checksums.sha256")
)
foreach ($path in $requiredFiles) {
    Assert-File $path
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$variants = @($manifest.variants)
if ($manifest.formatVersion -ne 2 -or $variants.Count -ne 32) {
    throw "Expected a format-2 manifest with 32 variants."
}
if (@($variants | Where-Object category -eq "anime").Count -ne 13 -or
    @($variants | Where-Object category -eq "realistic").Count -ne 19) {
    throw "Internal asset category counts are invalid."
}
if (@($variants.id | Sort-Object -Unique).Count -ne 32 -or
    @($variants.sha256 | Sort-Object -Unique).Count -ne 32 -or
    @($variants.entry | Sort-Object -Unique).Count -ne 32 -or
    @($variants.preview | Sort-Object -Unique).Count -ne 32) {
    throw "Variant ids, hashes, entries, and previews must be unique."
}
if (@($variants.legacySha256 | Where-Object { $_ -match '^[0-9a-fA-F]{64}$' } |
    Sort-Object -Unique).Count -ne 32) {
    throw "Every variant must have a unique legacy hash for migration."
}

foreach ($variant in $variants) {
    $relativeVpk = ([string]$variant.entry).Replace('/', '\')
    $relativePreview = ([string]$variant.preview).Replace('/', '\')
    $vpkPath = Join-Path $assetRoot $relativeVpk
    $previewPath = Join-Path $assetRoot $relativePreview
    Assert-File $vpkPath
    Assert-File $previewPath

    $file = Get-Item -LiteralPath $vpkPath
    if ($file.Length -ne [long]$variant.bytes) {
        throw "Variant size mismatch: $relativeVpk"
    }
    $hash = (Get-FileHash -LiteralPath $vpkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne ([string]$variant.sha256).ToLowerInvariant()) {
        throw "Variant hash mismatch: $relativeVpk"
    }
    Assert-SkyboxMaterial $vpkPath
}

$veil = $manifest.baseVeilOverride
if (-not $veil -or ([string]$veil.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$veil.bytes -le 0) {
    throw "Base-veil override metadata is invalid."
}
$veilPath = Join-Path $assetRoot ([string]$veil.entry).Replace('/', '\')
Assert-File $veilPath
if ((Get-Item -LiteralPath $veilPath).Length -ne [long]$veil.bytes -or
    (Get-FileHash -LiteralPath $veilPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$veil.sha256).ToLowerInvariant()) {
    throw "Base-veil override hash or size mismatch."
}

$smoke = $manifest.factorySmokeOverride
if (-not $smoke -or ([string]$smoke.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$smoke.bytes -le 0) {
    throw "Factory-smoke override metadata is invalid."
}
$smokePath = Join-Path $assetRoot ([string]$smoke.entry).Replace('/', '\')
Assert-File $smokePath
if ((Get-Item -LiteralPath $smokePath).Length -ne [long]$smoke.bytes -or
    (Get-FileHash -LiteralPath $smokePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$smoke.sha256).ToLowerInvariant()) {
    throw "Factory-smoke override hash or size mismatch."
}

$names = $manifest.hideNamesOverride
if (-not $names -or ([string]$names.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$names.bytes -le 0) {
    throw "Hide-names override metadata is invalid."
}
$namesPath = Join-Path $assetRoot ([string]$names.entry).Replace('/', '\')
Assert-File $namesPath
if ((Get-Item -LiteralPath $namesPath).Length -ne [long]$names.bytes -or
    (Get-FileHash -LiteralPath $namesPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$names.sha256).ToLowerInvariant()) {
    throw "Hide-names override hash or size mismatch."
}

$book = $manifest.hidePickupBookOverride
if (-not $book -or ([string]$book.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$book.bytes -le 0) {
    throw "Pickup-book override metadata is invalid."
}
$bookPath = Join-Path $assetRoot ([string]$book.entry).Replace('/', '\')
Assert-File $bookPath
if ((Get-Item -LiteralPath $bookPath).Length -ne [long]$book.bytes -or
    (Get-FileHash -LiteralPath $bookPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$book.sha256).ToLowerInvariant()) {
    throw "Pickup-book override hash or size mismatch."
}

$markers = $manifest.hidePlayerMarkersOverride
if (-not $markers -or ([string]$markers.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$markers.bytes -le 0) {
    throw "Player-marker override metadata is invalid."
}
$markersPath = Join-Path $assetRoot ([string]$markers.entry).Replace('/', '\')
Assert-File $markersPath
if ((Get-Item -LiteralPath $markersPath).Length -ne [long]$markers.bytes -or
    (Get-FileHash -LiteralPath $markersPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$markers.sha256).ToLowerInvariant()) {
    throw "Player-marker override hash or size mismatch."
}

$healthLines = $manifest.hideHealthLinesOverride
if (-not $healthLines -or ([string]$healthLines.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$healthLines.bytes -le 0) {
    throw "HP-bar line override metadata is invalid."
}
$healthLinesPath = Join-Path $assetRoot ([string]$healthLines.entry).Replace('/', '\')
Assert-File $healthLinesPath
if ((Get-Item -LiteralPath $healthLinesPath).Length -ne [long]$healthLines.bytes -or
    (Get-FileHash -LiteralPath $healthLinesPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$healthLines.sha256).ToLowerInvariant()) {
    throw "HP-bar line override hash or size mismatch."
}

$classicFill = $manifest.classicAbilityFillOverride
if (-not $classicFill -or ([string]$classicFill.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$classicFill.bytes -le 0) {
    throw "Classic ability-fill override metadata is invalid."
}
$classicFillPath = Join-Path $assetRoot ([string]$classicFill.entry).Replace('/', '\')
Assert-File $classicFillPath
if ((Get-Item -LiteralPath $classicFillPath).Length -ne [long]$classicFill.bytes -or
    (Get-FileHash -LiteralPath $classicFillPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$classicFill.sha256).ToLowerInvariant()) {
    throw "Classic ability-fill override hash or size mismatch."
}

$colorFix = $manifest.colorFixOverride
if (-not $colorFix -or ([string]$colorFix.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    [long]$colorFix.bytes -le 0) {
    throw "ColorFix override metadata is invalid."
}
$colorFixPath = Join-Path $assetRoot ([string]$colorFix.entry).Replace('/', '\')
Assert-File $colorFixPath
if ((Get-Item -LiteralPath $colorFixPath).Length -ne [long]$colorFix.bytes -or
    (Get-FileHash -LiteralPath $colorFixPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        ([string]$colorFix.sha256).ToLowerInvariant()) {
    throw "ColorFix override hash or size mismatch."
}

$nameMap = [object[]](Get-Content -LiteralPath (Join-Path $sourceRoot "skyboxes.json") -Raw | ConvertFrom-Json)
$namedVariants = @($nameMap | Where-Object { $_.id -ne "vanilla" })
if ($namedVariants.Count -ne 32 -or
    @($namedVariants.displayName | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count -ne 32) {
    throw "The readable skybox map must provide 32 unique display names."
}

# The interface reads names straight out of the shipped manifest, so the manifest copy has to stay
# equal to skyboxes.json. tools\sync-skybox-names.ps1 rewrites it; this only refuses to build when
# the two have drifted.
$expectedNames = @{}
foreach ($named in $namedVariants) {
    $expectedNames[[string]$named.id] = [string]$named.displayName
}
foreach ($variant in $variants) {
    $variantId = [string]$variant.id
    if (-not $expectedNames.ContainsKey($variantId)) {
        throw "skyboxes.json has no entry for manifest variant $variantId."
    }
    if ([string]$variant.displayName -ne $expectedNames[$variantId]) {
        throw "Manifest display name for $variantId is out of date. Run tools\sync-skybox-names.ps1."
    }
}

foreach ($name in @("SkyboxSelector.cmd", "select-skybox.ps1", "install-fps-config.ps1", "deadlock-fps.cfg")) {
    Assert-SameFile `
        (Join-Path $sourceRoot ("runtime\" + $name)) `
        (Join-Path $runtimeRoot $name) `
        $name
}
Assert-SameFile `
    (Join-Path $sourceRoot "config\gameinfo.gi") `
    (Join-Path $unpackedRoot "config\gameinfo.gi") `
    "GameInfo"

$configText = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot "config\gameinfo.gi")
if ($configText -notmatch '(?im)^\s*citadel_show_survey\s+"false"') {
    throw "GameInfo does not disable the playtester survey."
}
# citadel_show_survey only stops the survey being forced on outside matchmaking, so the kill switch
# is what actually keeps the panel from appearing.
if ($configText -notmatch '(?im)^\s*citadel_enable_survey\s+"false"') {
    throw "GameInfo does not throw the playtester survey kill switch."
}
if ($configText -notmatch '(?im)^\s*cl_phys_enabled\s+"true"') {
    throw "GameInfo does not keep client physics enabled."
}
if ($configText -match '(?im)^\s*cl_phys_enabled\s+"false"') {
    throw "GameInfo still actively disables client physics."
}
if (([regex]::Matches($configText, '\{')).Count -ne ([regex]::Matches($configText, '\}')).Count) {
    throw "GameInfo braces are unbalanced."
}

$fpsProfile = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot "runtime\deadlock-fps.cfg")
if ($fpsProfile -notmatch '(?im)^\s*cl_ragdoll_limit\s+"8"') {
    throw "The FPS profile must retain cl_ragdoll_limit 8."
}
if ($fpsProfile -match '(?im)^\s*cl_phys_enabled\s+') {
    throw "The FPS profile must not override client physics."
}

foreach ($script in @(
    (Join-Path $sourceRoot "runtime\select-skybox.ps1"),
    (Join-Path $repositoryRoot "tools\build-base-veil-override.ps1"),
    (Join-Path $sourceRoot "runtime\install-fps-config.ps1"),
    (Join-Path $repositoryRoot "tests\first-run.ps1"),
    (Join-Path $repositoryRoot "tests\fps-config.ps1"),
    (Join-Path $repositoryRoot "tests\onefile-integration.ps1")
)) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -ne 0) {
        throw "PowerShell parse error in $script`: $(@($errors | ForEach-Object Message) -join '; ')"
    }
}

$checksumLines = Get-Content -LiteralPath (Join-Path $runtimeRoot "runtime-checksums.sha256")
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') {
        throw "Invalid runtime checksum line: $line"
    }
    $path = Join-Path $runtimeRoot $Matches[2]
    Assert-File $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Matches[1].ToLowerInvariant()) {
        throw "Runtime payload hash mismatch: $($Matches[2])"
    }
}

$ignoredRoots = @(
    ([IO.Path]::GetFullPath((Join-Path $repositoryRoot ".build")).TrimEnd('\') + '\'),
    ([IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist")).TrimEnd('\') + '\'),
    ([IO.Path]::GetFullPath((Join-Path $repositoryRoot ".retarget-backup")).TrimEnd('\') + '\'),
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
