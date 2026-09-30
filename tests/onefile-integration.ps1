[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path $PSScriptRoot -Parent
$selector = Join-Path $projectRoot "source\runtime\select-skybox.ps1"
$assetRoot = Join-Path $projectRoot "unpacked\assets"
$testRoot = Join-Path $projectRoot ".onefile-integration-test"
$fakeDeadlock = Join-Path $testRoot "Deadlock"
$fakeCitadel = Join-Path $fakeDeadlock "game\citadel"
$fakeAddons = Join-Path $fakeCitadel "addons"
$fakeCache = Join-Path $fakeDeadlock "dlskybox"
$fakeBackups = Join-Path $fakeCache "backups"

function Assert-SafeTestPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe integration-test path: $fullPath"
    }
}

function Invoke-Selector([string]$Action, [string]$Selection = "") {
    $escapedSelector = $selector.Replace("'", "''")
    $escapedRoot = $fakeDeadlock.Replace("'", "''")
    $escapedCache = $fakeCache.Replace("'", "''")
    $escapedBackups = $fakeBackups.Replace("'", "''")
    $command = @"
function Get-Process {
    [CmdletBinding()]
    param([string[]]`$Name)
    return @()
}
& '$escapedSelector' -Action '$Action' -Selection '$Selection' -DeadlockRoot '$escapedRoot' -CacheRoot '$escapedCache' -BackupRoot '$escapedBackups'
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $token = [Guid]::NewGuid().ToString("N")
    $stdoutPath = Join-Path $testRoot "selector-$token.stdout.txt"
    $stderrPath = Join-Path $testRoot "selector-$token.stderr.txt"
    try {
        $process = Start-Process -FilePath "powershell.exe" `
            -ArgumentList @("-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", $encoded) `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -Wait `
            -PassThru
        $output = @(
            if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw }
            if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw }
        ) -join [Environment]::NewLine
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $output.Trim()
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Assert-Exit([object]$Result, [int]$Expected, [string]$Step) {
    if ($Result.ExitCode -ne $Expected) {
        throw "$Step returned $($Result.ExitCode), expected $Expected`n$($Result.Output)"
    }
}

Assert-SafeTestPath $testRoot
if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

try {
    New-Item -ItemType Directory -Force -Path $fakeAddons, $fakeCache, $fakeBackups | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot "source\config\gameinfo.gi") -Destination $fakeCitadel
    Copy-Item -LiteralPath (Join-Path $assetRoot "manifest.json") -Destination $fakeCache
    $readyHash = (Get-FileHash -LiteralPath (Join-Path $assetRoot "manifest.json") -Algorithm SHA256).Hash
    Set-Content -LiteralPath (Join-Path $fakeCache ".ready.sha256") -Value $readyHash -Encoding ASCII

    $manifest = Get-Content -LiteralPath (Join-Path $fakeCache "manifest.json") -Raw | ConvertFrom-Json
    foreach ($id in @("anime_01", "anime_02")) {
        $variant = $manifest.variants | Where-Object id -eq $id | Select-Object -First 1
        $relative = ([string]$variant.entry).Replace('/', '\')
        $source = Join-Path $assetRoot $relative
        $target = Join-Path $fakeCache $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }

    $freshStatus = Invoke-Selector "status"
    if ($freshStatus.Output -notmatch "skybox mod is not installed" -or
        (Test-Path -LiteralPath (Join-Path $fakeAddons "pak01_dir.vpk"))) {
        throw "Fresh status did not report a clean Vanilla installation`n$($freshStatus.Output)"
    }
    Assert-Exit (Invoke-Selector "select" "anime_01") 0 "Initial selection"

    $targetVpk = Join-Path $fakeAddons "pak01_dir.vpk"
    $anime01 = $manifest.variants | Where-Object id -eq "anime_01" | Select-Object -First 1
    $anime02 = $manifest.variants | Where-Object id -eq "anime_02" | Select-Object -First 1
    if ((Get-FileHash -LiteralPath $targetVpk -Algorithm SHA256).Hash -ne ([string]$anime01.sha256).ToUpperInvariant()) {
        throw "Initial selection hash mismatch"
    }

    $anime02Path = Join-Path $fakeCache (([string]$anime02.entry).Replace('/', '\'))
    [IO.File]::WriteAllBytes($anime02Path, [byte[]](1, 2, 3, 4))
    $failedSwitch = Invoke-Selector "select" "anime_02"
    Assert-Exit $failedSwitch 1 "Corrupt-source rollback"
    if ((Get-FileHash -LiteralPath $targetVpk -Algorithm SHA256).Hash -ne ([string]$anime01.sha256).ToUpperInvariant()) {
        throw "Failed selection did not restore the previous skybox"
    }

    $realAnime02Path = Join-Path $assetRoot (([string]$anime02.entry).Replace('/', '\'))
    Copy-Item -LiteralPath $realAnime02Path -Destination $anime02Path -Force
    Assert-Exit (Invoke-Selector "select" "anime_02") 0 "Valid switch"
    if ((Get-FileHash -LiteralPath $targetVpk -Algorithm SHA256).Hash -ne ([string]$anime02.sha256).ToUpperInvariant()) {
        throw "Valid switch hash mismatch"
    }

    Assert-Exit (Invoke-Selector "select" "vanilla") 0 "Vanilla restore"
    if (Test-Path -LiteralPath $targetVpk) {
        throw "Vanilla restore left the managed VPK installed"
    }

    [IO.File]::WriteAllBytes($targetVpk, [byte[]](9, 8, 7, 6, 5))
    $unknownHash = (Get-FileHash -LiteralPath $targetVpk -Algorithm SHA256).Hash
    Assert-Exit (Invoke-Selector "select" "anime_01") 0 "Unknown-mod override"
    $preserved = Get-ChildItem -LiteralPath $fakeBackups -Recurse -File -Filter "pak01_dir.vpk" |
        Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -eq $unknownHash } |
        Select-Object -First 1
    if (-not $preserved) {
        throw "Unknown mod was not preserved in a verified backup"
    }

    $veil = $manifest.baseVeilOverride
    $veilCached = Join-Path $fakeCache (([string]$veil.entry).Replace('/', '\'))
    New-Item -ItemType Directory -Force -Path (Split-Path $veilCached -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $assetRoot (([string]$veil.entry).Replace('/', '\'))) -Destination $veilCached
    $veilTarget = Join-Path $fakeAddons "pak03_dir.vpk"
    [IO.File]::WriteAllBytes($veilTarget, [byte[]](9, 8, 7, 6))
    $foreignVeilHash = (Get-FileHash -LiteralPath $veilTarget -Algorithm SHA256).Hash
    Assert-Exit (Invoke-Selector "veil-hide") 1 "Foreign veil-slot refusal"
    if ((Get-FileHash -LiteralPath $veilTarget -Algorithm SHA256).Hash -ne $foreignVeilHash) {
        throw "The foreign pak03_dir.vpk was modified"
    }
    Remove-Item -LiteralPath $veilTarget -Force
    Assert-Exit (Invoke-Selector "veil-hide") 0 "Hide base veil"
    if ((Get-FileHash -LiteralPath $veilTarget -Algorithm SHA256).Hash -ne
        ([string]$veil.sha256).ToUpperInvariant()) {
        throw "Base-veil override hash mismatch"
    }
    Assert-Exit (Invoke-Selector "veil-show") 0 "Restore base veil"
    if (Test-Path -LiteralPath $veilTarget) {
        throw "Base-veil override was not removed"
    }

    $smoke = $manifest.factorySmokeOverride
    $smokeCached = Join-Path $fakeCache (([string]$smoke.entry).Replace('/', '\'))
    Copy-Item -LiteralPath (Join-Path $assetRoot (([string]$smoke.entry).Replace('/', '\'))) -Destination $smokeCached
    $smokeTarget = Join-Path $fakeAddons "pak04_dir.vpk"
    [IO.File]::WriteAllBytes($smokeTarget, [byte[]](6, 7, 8, 9))
    $foreignSmokeHash = (Get-FileHash -LiteralPath $smokeTarget -Algorithm SHA256).Hash
    Assert-Exit (Invoke-Selector "smoke-hide") 1 "Foreign smoke-slot refusal"
    if ((Get-FileHash -LiteralPath $smokeTarget -Algorithm SHA256).Hash -ne $foreignSmokeHash) {
        throw "The foreign pak04_dir.vpk was modified"
    }
    Remove-Item -LiteralPath $smokeTarget -Force
    Assert-Exit (Invoke-Selector "veil-hide") 0 "Hide veil independently"
    Assert-Exit (Invoke-Selector "smoke-hide") 0 "Hide factory smoke"
    if ((Get-FileHash -LiteralPath $smokeTarget -Algorithm SHA256).Hash -ne
        ([string]$smoke.sha256).ToUpperInvariant() -or -not (Test-Path -LiteralPath $veilTarget)) {
        throw "Factory-smoke override or independent veil state is invalid"
    }
    Assert-Exit (Invoke-Selector "smoke-show") 0 "Restore factory smoke"
    if ((Test-Path -LiteralPath $smokeTarget) -or -not (Test-Path -LiteralPath $veilTarget)) {
        throw "Restoring factory smoke changed the base veil"
    }
    Assert-Exit (Invoke-Selector "veil-show") 0 "Restore independent veil"

    $names = $manifest.hideNamesOverride
    $namesCached = Join-Path $fakeCache (([string]$names.entry).Replace('/', '\'))
    Copy-Item -LiteralPath (Join-Path $assetRoot (([string]$names.entry).Replace('/', '\'))) -Destination $namesCached
    $namesTarget = Join-Path $fakeAddons "pak05_dir.vpk"
    [IO.File]::WriteAllBytes($namesTarget, [byte[]](5, 4, 3, 2))
    $foreignNamesHash = (Get-FileHash -LiteralPath $namesTarget -Algorithm SHA256).Hash
    Assert-Exit (Invoke-Selector "names-hide") 1 "Foreign names-slot refusal"
    if ((Get-FileHash -LiteralPath $namesTarget -Algorithm SHA256).Hash -ne $foreignNamesHash) {
        throw "The foreign pak05_dir.vpk was modified"
    }
    Remove-Item -LiteralPath $namesTarget -Force
    Assert-Exit (Invoke-Selector "names-hide") 0 "Hide unit names"
    Assert-Exit (Invoke-Selector "names-hide") 0 "Hide unit names twice"
    if ((Get-FileHash -LiteralPath $namesTarget -Algorithm SHA256).Hash -ne
        ([string]$names.sha256).ToUpperInvariant()) {
        throw "Hide-names override hash mismatch"
    }
    Assert-Exit (Invoke-Selector "smoke-hide") 0 "Hide smoke independently from names"
    Assert-Exit (Invoke-Selector "names-show") 0 "Restore unit names"
    if ((Test-Path -LiteralPath $namesTarget) -or -not (Test-Path -LiteralPath $smokeTarget)) {
        throw "Restoring unit names changed the factory smoke"
    }
    Assert-Exit (Invoke-Selector "names-show") 0 "Restore unit names twice"
    Assert-Exit (Invoke-Selector "smoke-show") 0 "Restore independent smoke"

    Assert-Exit (Invoke-Selector "select" "vanilla") 0 "Final Vanilla restore"
    Write-Host "One-file integration passed: skybox switching, backup, Vanilla and three independent cosmetic toggles."
}
finally {
    Assert-SafeTestPath $testRoot
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
