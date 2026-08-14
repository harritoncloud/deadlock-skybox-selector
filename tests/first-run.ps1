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
    Join-Path $projectRoot "onefile\dist\SkyboxSelector.exe"
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
    if (@($manifest.variants).Count -ne 32 -or $thumbnails.Count -ne 32 -or $vpkFiles.Count -ne 32) {
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

    [pscustomobject]@{
        Variants = @($manifest.variants).Count
        VpkFiles = $vpkFiles.Count
        Thumbnails = $thumbnails.Count
        ReadyHash = (Get-Content -LiteralPath (Join-Path $cacheRoot ".ready.sha256") -Raw).Trim()
    } | Format-List
    $results | Format-Table -AutoSize
    Write-Host "First-run test passed: extract, validate, warm start and legacy cache migration."
}
finally {
    Assert-SafeTestPath $testRoot
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
