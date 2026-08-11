[CmdletBinding()]
param(
    [ValidateSet("status", "select", "validate-cache")]
    [string]$Action = "select",
    [string]$Selection = "",
    [string]$DeadlockRoot = $(if ($env:DEADLOCK_ROOT) { $env:DEADLOCK_ROOT } else { "C:\Program Files (x86)\Steam\steamapps\common\Deadlock" }),
    [string]$CacheRoot = $(if ($env:SKYBOX_CACHE_ROOT) { $env:SKYBOX_CACHE_ROOT } else { "" }),
    [string]$BackupRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-ChildPath([string]$Path, [string]$Root, [string]$Description) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to access a path outside $Description`: $fullPath"
    }
}

function Resolve-CacheEntry([string]$Entry, [string]$Root) {
    if ([string]::IsNullOrWhiteSpace($Entry) -or [IO.Path]::IsPathRooted($Entry)) {
        throw "Unsafe cache entry: $Entry"
    }

    $normalized = $Entry.Replace('/', '\')
    if ($normalized -match '(^|\\)\.\.(\\|$)') {
        throw "Unsafe cache entry: $Entry"
    }

    $resolved = Join-Path $Root $normalized
    Assert-ChildPath $resolved $Root "the managed skybox cache"
    return $resolved
}

function Wait-ForManagedProcesses([string]$ManagedDeadlockRoot) {
    $waiting = $false
    while ($true) {
        $running = @()
        $managedRoot = [IO.Path]::GetFullPath($ManagedDeadlockRoot).TrimEnd('\') + '\'
        foreach ($process in @(Get-Process -Name deadlock -ErrorAction SilentlyContinue)) {
            try {
                $processPath = [IO.Path]::GetFullPath($process.Path)
                if ($processPath.StartsWith($managedRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    $running += $process
                }
            } catch {
                # If Windows hides the executable path, waiting is safer than modifying live files.
                $running += $process
            }
        }
        $running += @(Get-Process -Name dmm, deadlock-modmanager -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) {
            if ($waiting) {
                Write-Host "Deadlock and Deadlock Mod Manager are closed. Continuing." -ForegroundColor Green
            }
            return
        }

        if (-not $waiting) {
            $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ", "
            Write-Host "Waiting for Deadlock and Deadlock Mod Manager to close: $names" -ForegroundColor Yellow
            Write-Host "Checking once per second. Press Ctrl+C to cancel."
            $waiting = $true
        }
        Start-Sleep -Seconds 1
    }
}

function Copy-VerifiedVariant(
    [string]$Source,
    [string]$Target,
    [long]$ExpectedBytes,
    [string]$ExpectedHash
) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Cached VPK is missing: $Source"
    }

    $sourceItem = Get-Item -LiteralPath $Source
    if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Cached VPK must not be a reparse point: $Source"
    }
    if ($sourceItem.Length -ne $ExpectedBytes -or (Get-Sha256 $Source) -ne $ExpectedHash) {
        throw "Cached VPK failed verification: $Source"
    }

    $temporary = $Target + ".patchwin-new"
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $Source -Destination $temporary -Force
    if ((Get-Item -LiteralPath $temporary).Length -ne $ExpectedBytes -or (Get-Sha256 $temporary) -ne $ExpectedHash) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        throw "Copied VPK failed verification: $Target"
    }

    Remove-Item -LiteralPath $Target -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $temporary -Destination $Target -Force
    if ((Get-Sha256 $Target) -ne $ExpectedHash) {
        throw "Installed VPK failed final verification: $Target"
    }
}

try {
    $DeadlockRoot = [IO.Path]::GetFullPath($DeadlockRoot).TrimEnd('\')
    if (-not $CacheRoot) {
        $CacheRoot = Join-Path $DeadlockRoot "patchwin.cc-skyboxes"
    }
    $CacheRoot = [IO.Path]::GetFullPath($CacheRoot).TrimEnd('\')
    Assert-ChildPath $CacheRoot $DeadlockRoot "the Deadlock installation"

    if (-not $BackupRoot) {
        $BackupRoot = Join-Path $CacheRoot "backups"
    }
    $BackupRoot = [IO.Path]::GetFullPath($BackupRoot).TrimEnd('\')
    Assert-ChildPath $BackupRoot $CacheRoot "the managed skybox cache"

    $manifestPath = Join-Path $CacheRoot "manifest.json"
    $readyPath = Join-Path $CacheRoot ".ready.sha256"
    foreach ($path in @($manifestPath, $readyPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "The skybox cache is incomplete. Restart SkyboxSelector.exe: $path"
        }
    }

    $readyHash = (Get-Content -Raw -LiteralPath $readyPath).Trim().ToUpperInvariant()
    if ($readyHash -notmatch '^[0-9A-F]{64}$') {
        throw "The skybox cache readiness marker is invalid."
    }
    if ($env:SKYBOX_ASSET_SHA256 -and $readyHash -ne $env:SKYBOX_ASSET_SHA256.ToUpperInvariant()) {
        throw "The skybox cache belongs to a different application build."
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $variants = @($manifest.variants)
    if ($manifest.formatVersion -ne 2 -or $variants.Count -ne 32) {
        throw "Unsupported or incomplete skybox cache manifest."
    }

    $knownManagedHashes = @{}
    $variantsById = @{}
    $variantPaths = @{}
    foreach ($variant in $variants) {
        $id = [string]$variant.id
        $category = [string]$variant.category
        $hash = ([string]$variant.sha256).ToUpperInvariant()
        $bytes = [long]$variant.bytes
        if ($id -notmatch '^(anime_(0[1-9]|1[0-3])|realistic_(0[1-9]|1[0-9]))$') {
            throw "Invalid variant id in cache manifest: $id"
        }
        if ($category -notin @("anime", "realistic") -or $hash -notmatch '^[0-9A-F]{64}$' -or $bytes -le 0) {
            throw "Invalid variant metadata in cache manifest: $id"
        }
        if ($variantsById.ContainsKey($id) -or $knownManagedHashes.ContainsKey($hash)) {
            throw "Duplicate variant metadata in cache manifest: $id"
        }

        $variantPath = Resolve-CacheEntry ([string]$variant.entry) $CacheRoot
        $knownManagedHashes[$hash] = $id
        $variantsById[$id] = $variant
        $variantPaths[$id] = $variantPath
    }

    if (@($variants | Where-Object category -eq "anime").Count -ne 13 -or
        @($variants | Where-Object category -eq "realistic").Count -ne 19) {
        throw "The skybox cache category counts are invalid."
    }

    if ($Action -eq "validate-cache") {
        foreach ($variant in $variants) {
            $id = [string]$variant.id
            $path = $variantPaths[$id]
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Cached VPK is missing: $path"
            }
            if ((Get-Item -LiteralPath $path).Length -ne [long]$variant.bytes -or
                (Get-Sha256 $path) -ne ([string]$variant.sha256).ToUpperInvariant()) {
                throw "Cached VPK failed verification: $id"
            }
        }
        Write-Host "Cache verification passed: 32 skyboxes." -ForegroundColor Green
        exit 0
    }

    $validSelections = @("vanilla") + @($variantsById.Keys)
    if ($Action -eq "select" -and $validSelections -notcontains $Selection) {
        throw "Unknown skybox selection: $Selection"
    }

    $citadelRoot = Join-Path $DeadlockRoot "game\citadel"
    $addonsRoot = Join-Path $citadelRoot "addons"
    $gameInfo = Join-Path $citadelRoot "gameinfo.gi"
    $managedTarget = Join-Path $addonsRoot "pak01_dir.vpk"
    $selectionFile = Join-Path $CacheRoot "selected-skybox.txt"

    if (-not (Test-Path -LiteralPath $gameInfo -PathType Leaf)) {
        throw "Deadlock gameinfo.gi is missing: $gameInfo"
    }
    $gameInfoText = Get-Content -Raw -LiteralPath $gameInfo
    $addonsMounted = $gameInfoText -match '(?im)^\s*Game\s+"?citadel/addons"?\s*$'

    if (Test-Path -LiteralPath $addonsRoot -PathType Container) {
        Assert-ChildPath $managedTarget $addonsRoot "the Deadlock addons directory"
    }

    $currentSelection = "vanilla"
    $unknownManagedHash = $null
    if (Test-Path -LiteralPath $managedTarget -PathType Leaf) {
        $currentHash = Get-Sha256 $managedTarget
        if ($knownManagedHashes.ContainsKey($currentHash)) {
            $currentSelection = $knownManagedHashes[$currentHash]
        } else {
            $unknownManagedHash = $currentHash
        }
    }

    $legacyDefinitions = @(
        [ordered]@{ Path = (Join-Path $addonsRoot "pak49_dir.vpk"); Hash = "C9749F68343056B0582F7D0DDFDC11C97E3D3F8EFAEBFCF691AFBB9BF7EA5C0E" },
        [ordered]@{ Path = (Join-Path $addonsRoot "pak50_dir.vpk"); Hash = "4A4885756F4991266014BCC7FB06ACAE9633FD3918A23C8651E60455B91475DB" },
        [ordered]@{ Path = (Join-Path $addonsRoot "pak51_dir.vpk"); Hash = "972DAB7C46AC5D0EBCA7E318C87C970124B3D3C8405D8F59F1C9E4DA974D347E" }
    )
    $legacyPresent = @()
    $unknownLegacy = @()
    foreach ($legacy in $legacyDefinitions) {
        Assert-ChildPath $legacy.Path $addonsRoot "the Deadlock addons directory"
        if (Test-Path -LiteralPath $legacy.Path -PathType Leaf) {
            $actualLegacyHash = Get-Sha256 $legacy.Path
            if ($actualLegacyHash -eq $legacy.Hash) {
                $legacyPresent += $legacy.Path
            } else {
                $unknownLegacy += "$($legacy.Path) [$actualLegacyHash]"
            }
        }
    }

    if ($Action -eq "status") {
        if (-not $addonsMounted) {
            Write-Host "Warning: gameinfo.gi does not mount citadel/addons. Use the GameInfo installer." -ForegroundColor Yellow
        }
        if ($unknownManagedHash) {
            Write-Host "Status: unknown addons\pak01_dir.vpk [$unknownManagedHash]" -ForegroundColor Red
            exit 12
        }
        if ($unknownLegacy.Count -gt 0) {
            Write-Host "Status: an unknown file uses a reserved selector VPK name." -ForegroundColor Red
            $unknownLegacy | ForEach-Object { Write-Host $_ }
            exit 12
        }
        if ($currentSelection -eq "vanilla" -and $legacyPresent.Count -eq 0) {
            Remove-Item -LiteralPath $selectionFile -Force -ErrorAction SilentlyContinue
            Write-Host "Status: skybox mod is not installed." -ForegroundColor Yellow
            exit 10
        }

        Set-Content -LiteralPath $selectionFile -Value $currentSelection -Encoding ASCII
        Write-Host "Status: installed - $currentSelection" -ForegroundColor Green
        if ($legacyPresent.Count -gt 0) {
            Write-Host "Legacy selector files will be removed on the next selection." -ForegroundColor Yellow
            exit 11
        }
        exit 0
    }

    if (-not $addonsMounted) {
        throw "gameinfo.gi does not mount citadel/addons. Run the GameInfo installer first."
    }

    Wait-ForManagedProcesses $DeadlockRoot
    if ($unknownManagedHash) {
        throw "Refusing to overwrite unknown addons\pak01_dir.vpk (SHA-256 $unknownManagedHash)."
    }
    if ($unknownLegacy.Count -gt 0) {
        throw "Refusing to remove an unknown file that uses a reserved selector VPK name."
    }

    if ($currentSelection -eq $Selection -and $legacyPresent.Count -eq 0) {
        Set-Content -LiteralPath $selectionFile -Value $Selection -Encoding ASCII
        Write-Host "Already selected: $Selection" -ForegroundColor Green
        exit 0
    }

    New-Item -ItemType Directory -Force -Path $addonsRoot | Out-Null
    Assert-ChildPath $managedTarget $addonsRoot "the Deadlock addons directory"

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
    $backupPath = Join-Path $BackupRoot "skybox-$timestamp"
    New-Item -ItemType Directory -Force -Path $backupPath | Out-Null

    $managedPaths = @($managedTarget) + @($legacyDefinitions | ForEach-Object { $_.Path })
    $originalFiles = @{}
    $backedUp = @()
    foreach ($path in $managedPaths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $pathHash = Get-Sha256 $path
            if ([string]::Equals($path, $managedTarget, [StringComparison]::OrdinalIgnoreCase) -and
                $currentSelection -ne "vanilla") {
                $restoreVariant = $variantsById[$currentSelection]
                $restoreSource = $variantPaths[$currentSelection]
                $restoreBytes = [long]$restoreVariant.bytes
                $restoreHash = ([string]$restoreVariant.sha256).ToUpperInvariant()
                if (-not (Test-Path -LiteralPath $restoreSource -PathType Leaf) -or
                    (Get-Item -LiteralPath $restoreSource).Length -ne $restoreBytes -or
                    (Get-Sha256 $restoreSource) -ne $restoreHash) {
                    throw "The cached rollback source failed verification: $currentSelection"
                }

                $originalFiles[$path] = [ordered]@{
                    kind = "cache"
                    source = $restoreSource
                    bytes = $restoreBytes
                    sha256 = $restoreHash
                }
                $backedUp += [ordered]@{
                    source = $path
                    backup = $null
                    restoreFrom = "verified-cache/$currentSelection"
                    sha256 = $pathHash.ToLowerInvariant()
                }
            } else {
                $backupFile = Join-Path $backupPath (Split-Path $path -Leaf)
                Copy-Item -LiteralPath $path -Destination $backupFile -Force
                $originalFiles[$path] = [ordered]@{
                    kind = "backup"
                    source = $backupFile
                }
                $backedUp += [ordered]@{
                    source = $path
                    backup = $backupFile
                    restoreFrom = "backup"
                    sha256 = $pathHash.ToLowerInvariant()
                }
            }
        } else {
            $originalFiles[$path] = $null
        }
    }

    try {
        if ($Selection -eq "vanilla") {
            Remove-Item -LiteralPath $managedTarget -Force -ErrorAction SilentlyContinue
        } else {
            $selectedVariant = $variantsById[$Selection]
            Copy-VerifiedVariant `
                $variantPaths[$Selection] `
                $managedTarget `
                ([long]$selectedVariant.bytes) `
                (([string]$selectedVariant.sha256).ToUpperInvariant())
        }

        foreach ($legacyPath in $legacyPresent) {
            Remove-Item -LiteralPath $legacyPath -Force
        }

        if ($Selection -eq "vanilla") {
            if (Test-Path -LiteralPath $managedTarget) {
                throw "Vanilla verification failed: managed override still exists."
            }
        } else {
            $expectedHash = ([string]$variantsById[$Selection].sha256).ToUpperInvariant()
            if ((Get-Sha256 $managedTarget) -ne $expectedHash) {
                throw "Final selected skybox verification failed."
            }
        }
    } catch {
        $operationError = $_
        foreach ($path in $managedPaths) {
            $restore = $originalFiles[$path]
            if ($restore -and $restore.kind -eq "cache") {
                Copy-VerifiedVariant `
                    ([string]$restore.source) `
                    $path `
                    ([long]$restore.bytes) `
                    (([string]$restore.sha256).ToUpperInvariant())
            } elseif ($restore) {
                Copy-Item -LiteralPath ([string]$restore.source) -Destination $path -Force
            } else {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
        throw $operationError
    }

    Set-Content -LiteralPath $selectionFile -Value $Selection -Encoding ASCII
    $switchManifest = [ordered]@{
        changedAtUtc = [DateTime]::UtcNow.ToString("o")
        selection = $Selection
        previousSelection = $currentSelection
        deadlockRoot = $DeadlockRoot
        cacheRoot = $CacheRoot
        gameWasLaunched = $false
        assetArchiveSha256 = $readyHash.ToLowerInvariant()
        backupPath = $backupPath
        removedLegacy = $legacyPresent
        backedUp = $backedUp
    }
    $switchManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backupPath "switch-manifest.json") -Encoding UTF8

    Write-Host "Selected: $Selection" -ForegroundColor Green
    Write-Host "Backup: $backupPath"
    Write-Host "The new skybox will be active on the next Deadlock launch."
    exit 0
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception -is [UnauthorizedAccessException]) {
        Write-Host "Run SkyboxSelector.exe as Administrator." -ForegroundColor Yellow
    }
    exit 1
}
