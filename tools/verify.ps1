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
}

$nameMap = [object[]](Get-Content -LiteralPath (Join-Path $sourceRoot "skyboxes.json") -Raw | ConvertFrom-Json)
$namedVariants = @($nameMap | Where-Object { $_.id -ne "vanilla" })
if ($namedVariants.Count -ne 32 -or
    @($namedVariants.displayName | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count -ne 32) {
    throw "The readable skybox map must provide 32 unique display names."
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
