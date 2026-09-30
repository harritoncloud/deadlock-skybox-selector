[CmdletBinding()]
param(
    [string]$SelectorPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path $PSScriptRoot -Parent
$SelectorPath = if ($SelectorPath) {
    [IO.Path]::GetFullPath($SelectorPath)
} else {
    Join-Path $projectRoot "dist\SkyboxSelector.exe"
}
$testRoot = Join-Path $projectRoot ".first-run-audit"
$fakeDeadlock = Join-Path $testRoot "Deadlock"
$fakeCitadel = Join-Path $fakeDeadlock "game\citadel"
$cacheRoot = Join-Path $fakeDeadlock "dlskybox"
$sourceGameInfo = Join-Path $projectRoot "source\config\gameinfo.gi"
$selectorScript = Join-Path $projectRoot "source\runtime\select-skybox.ps1"

function Assert-SafeTestPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe first-run test path: $fullPath"
    }
}

function Invoke-Prepare([string]$Step) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $SelectorPath `
        -ArgumentList @("--prepare-only", "--deadlock-root", $fakeDeadlock) `
        -PassThru `
        -Wait
    $timer.Stop()
    if ($process.ExitCode -ne 0) {
        throw "$Step failed with exit code $($process.ExitCode)."
    }
    [pscustomobject]@{
        Step = $Step
        ExitCode = $process.ExitCode
        Seconds = [math]::Round($timer.Elapsed.TotalSeconds, 3)
    }
}

Assert-SafeTestPath $testRoot
if (-not (Test-Path -LiteralPath $SelectorPath -PathType Leaf)) {
    throw "Selector executable is missing: $SelectorPath"
}
if (-not (Test-Path -LiteralPath $sourceGameInfo -PathType Leaf)) {
    throw "Deadlock gameinfo.gi is missing: $sourceGameInfo"
}

if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

try {
    New-Item -ItemType Directory -Force -Path $fakeCitadel | Out-Null
    Copy-Item -LiteralPath $sourceGameInfo -Destination (Join-Path $fakeCitadel "gameinfo.gi")

    $results = @()
    $results += Invoke-Prepare "fresh-extract"

    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action validate-cache `
        -DeadlockRoot $fakeDeadlock `
        -CacheRoot $cacheRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Fresh cache validation failed."
    }

    $manifest = Get-Content -LiteralPath (Join-Path $cacheRoot "manifest.json") -Raw | ConvertFrom-Json
    $thumbnails = @(Get-ChildItem -LiteralPath (Join-Path $cacheRoot ".thumbnails-v1") -File -Filter "*.jpg")
    $vpkFiles = @(Get-ChildItem -LiteralPath $cacheRoot -Recurse -File -Filter "*.vpk")
    if (@($manifest.variants).Count -ne 32 -or $thumbnails.Count -ne 32 -or $vpkFiles.Count -ne 39) {
        throw "First-run cache is incomplete."
    }

    $readyBefore = (Get-FileHash -LiteralPath (Join-Path $cacheRoot ".ready.sha256") -Algorithm SHA256).Hash
    $results += Invoke-Prepare "warm-start"
    $readyAfter = (Get-FileHash -LiteralPath (Join-Path $cacheRoot ".ready.sha256") -Algorithm SHA256).Hash
    if ($readyBefore -ne $readyAfter) {
        throw "Warm start unexpectedly changed the readiness marker."
    }

    $legacyCache = Join-Path $fakeDeadlock "deadlockcustomskybox"
    Move-Item -LiteralPath $cacheRoot -Destination $legacyCache
    $results += Invoke-Prepare "legacy-deadlockcustomskybox-migration"
    if (-not (Test-Path -LiteralPath $cacheRoot) -or (Test-Path -LiteralPath $legacyCache)) {
        throw "deadlockcustomskybox migration failed."
    }

    $olderLegacyCache = Join-Path $fakeDeadlock "patchwin.cc-skyboxes"
    Move-Item -LiteralPath $cacheRoot -Destination $olderLegacyCache
    $results += Invoke-Prepare "legacy-patchwin-migration"
    if (-not (Test-Path -LiteralPath $cacheRoot) -or (Test-Path -LiteralPath $olderLegacyCache)) {
        throw "patchwin.cc-skyboxes migration failed."
    }

    # A previously installed package has the old SHA, even when its variant id
    # matches the requested one. Reapplying must replace it and retain a backup.
    $syntheticVariant = @($manifest.variants | Where-Object id -eq "anime_01")[0]
    $managedTarget = Join-Path $fakeCitadel "addons\pak01_dir.vpk"
    New-Item -ItemType Directory -Force -Path (Split-Path $managedTarget -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $cacheRoot $syntheticVariant.entry) -Destination $managedTarget
    $oldBytes = [IO.File]::ReadAllBytes($managedTarget)
    $oldBytes[$oldBytes.Length - 1] = $oldBytes[$oldBytes.Length - 1] -bxor 1
    [IO.File]::WriteAllBytes($managedTarget, $oldBytes)
    $syntheticLegacyHash = (Get-FileHash -LiteralPath $managedTarget -Algorithm SHA256).Hash
    $cachedManifestPath = Join-Path $cacheRoot "manifest.json"
    $cachedManifest = Get-Content -LiteralPath $cachedManifestPath -Raw | ConvertFrom-Json
    @($cachedManifest.variants | Where-Object id -eq "anime_01")[0].legacySha256 = $syntheticLegacyHash.ToLowerInvariant()
    $cachedManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cachedManifestPath -Encoding UTF8

    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action select -Selection anime_01 -DeadlockRoot $fakeDeadlock `
        -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or
        (Get-FileHash -LiteralPath $managedTarget -Algorithm SHA256).Hash -ne $syntheticVariant.sha256.ToUpperInvariant()) {
        throw "Reapplying an installed legacy skybox failed."
    }
    $oldBackups = @(Get-ChildItem -LiteralPath (Join-Path $cacheRoot "backups") -Recurse -File -Filter "pak01_dir.vpk")
    if ($oldBackups.Count -ne 1 -or
        (Get-FileHash -LiteralPath $oldBackups[0].FullName -Algorithm SHA256).Hash -ne $syntheticLegacyHash) {
        throw "The old installed skybox was not backed up."
    }

    $veilTarget = Join-Path $fakeCitadel "addons\pak03_dir.vpk"
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action veil-hide -DeadlockRoot $fakeDeadlock -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or
        (Get-FileHash -LiteralPath $veilTarget -Algorithm SHA256).Hash -ne
            ([string]$manifest.baseVeilOverride.sha256).ToUpperInvariant()) {
        throw "Hiding the base veil failed."
    }
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action veil-show -DeadlockRoot $fakeDeadlock -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $veilTarget)) {
        throw "Restoring the base veil failed."
    }

    $smokeTarget = Join-Path $fakeCitadel "addons\pak04_dir.vpk"
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action smoke-hide -DeadlockRoot $fakeDeadlock -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or
        (Get-FileHash -LiteralPath $smokeTarget -Algorithm SHA256).Hash -ne
            ([string]$manifest.factorySmokeOverride.sha256).ToUpperInvariant()) {
        throw "Hiding the factory smoke failed."
    }
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action smoke-show -DeadlockRoot $fakeDeadlock -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $smokeTarget)) {
        throw "Restoring the factory smoke failed."
    }

    $namesTarget = Join-Path $fakeCitadel "addons\pak05_dir.vpk"
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action names-hide -DeadlockRoot $fakeDeadlock -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or
        (Get-FileHash -LiteralPath $namesTarget -Algorithm SHA256).Hash -ne
            ([string]$manifest.hideNamesOverride.sha256).ToUpperInvariant()) {
        throw "Hiding unit names failed."
    }
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $selectorScript `
        -Action names-show -DeadlockRoot $fakeDeadlock -CacheRoot $cacheRoot -WaitTimeoutSeconds 0
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $namesTarget)) {
        throw "Restoring unit names failed."
    }

    [pscustomobject]@{
        Variants = @($manifest.variants).Count
        VpkFiles = $vpkFiles.Count
        Thumbnails = $thumbnails.Count
        ReadyHash = (Get-Content -LiteralPath (Join-Path $cacheRoot ".ready.sha256") -Raw).Trim()
    } | Format-List
    $results | Format-Table -AutoSize
    Write-Host "First-run test passed: extract, validate, cache migration, old skybox reapply and three cosmetic toggles."
}
finally {
    Assert-SafeTestPath $testRoot
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
