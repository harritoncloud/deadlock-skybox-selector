[CmdletBinding()]
param(
    [ValidateSet("status", "select", "validate-cache", "veil-hide", "veil-show", "smoke-hide", "smoke-show", "names-hide", "names-show", "book-hide", "book-show", "markers-hide", "markers-show", "graves-hide", "graves-show", "lines-hide", "lines-show", "fill-hide", "fill-show", "colorfix-on", "colorfix-off")]
    [string]$Action = "select",
    [string]$Selection = "",
    [string]$DeadlockRoot = $(if ($env:DEADLOCK_ROOT) { $env:DEADLOCK_ROOT } else { "C:\Program Files (x86)\Steam\steamapps\common\Deadlock" }),
    [string]$CacheRoot = $(if ($env:SKYBOX_CACHE_ROOT) { $env:SKYBOX_CACHE_ROOT } else { "" }),
    [string]$BackupRoot = "",
    [ValidateRange(0, 3600)]
    [int]$WaitTimeoutSeconds = 45
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

function Wait-ForManagedProcesses([string]$ManagedDeadlockRoot, [int]$TimeoutSeconds) {
    $waiting = $false
    $deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
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

        $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ", "
        # The selector starts this script hidden, so an unbounded wait would look like a frozen
        # application. Fail with a message the interface can show instead.
        if ((Get-Date) -ge $deadline) {
            throw "Close Deadlock and Deadlock Mod Manager, then try again. Still running: $names"
        }

        if (-not $waiting) {
            Write-Host "Waiting for Deadlock and Deadlock Mod Manager to close: $names" -ForegroundColor Yellow
            Write-Host "Checking once per second for up to $TimeoutSeconds seconds."
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
        $CacheRoot = Join-Path $DeadlockRoot "dlskybox"
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
    $legacyManagedHashes = @{}
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
        if ($variant.PSObject.Properties.Name -contains "legacySha256") {
            $legacyHash = ([string]$variant.legacySha256).ToUpperInvariant()
            if ($legacyHash -notmatch '^[0-9A-F]{64}$' -or
                $knownManagedHashes.ContainsKey($legacyHash)) {
                throw "Invalid or duplicate legacy variant hash in cache manifest: $id"
            }
            $knownManagedHashes[$legacyHash] = $id
            $legacyManagedHashes[$legacyHash] = $id
        }
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
        $veil = $manifest.baseVeilOverride
        if (-not $veil -or ([string]$veil.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The base-veil override metadata is invalid."
        }
        $veilPath = Resolve-CacheEntry ([string]$veil.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $veilPath -PathType Leaf) -or
            (Get-Item -LiteralPath $veilPath).Length -ne [long]$veil.bytes -or
            (Get-Sha256 $veilPath) -ne ([string]$veil.sha256).ToUpperInvariant()) {
            throw "The cached base-veil override failed verification."
        }
        $smoke = $manifest.factorySmokeOverride
        if (-not $smoke -or ([string]$smoke.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The factory-smoke override metadata is invalid."
        }
        $smokePath = Resolve-CacheEntry ([string]$smoke.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $smokePath -PathType Leaf) -or
            (Get-Item -LiteralPath $smokePath).Length -ne [long]$smoke.bytes -or
            (Get-Sha256 $smokePath) -ne ([string]$smoke.sha256).ToUpperInvariant()) {
            throw "The cached factory-smoke override failed verification."
        }
        $names = $manifest.hideNamesOverride
        if (-not $names -or ([string]$names.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The hide-names override metadata is invalid."
        }
        $namesPath = Resolve-CacheEntry ([string]$names.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $namesPath -PathType Leaf) -or
            (Get-Item -LiteralPath $namesPath).Length -ne [long]$names.bytes -or
            (Get-Sha256 $namesPath) -ne ([string]$names.sha256).ToUpperInvariant()) {
            throw "The cached hide-names override failed verification."
        }
        $book = $manifest.hidePickupBookOverride
        if (-not $book -or ([string]$book.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The pickup-book override metadata is invalid."
        }
        $bookPath = Resolve-CacheEntry ([string]$book.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $bookPath -PathType Leaf) -or
            (Get-Item -LiteralPath $bookPath).Length -ne [long]$book.bytes -or
            (Get-Sha256 $bookPath) -ne ([string]$book.sha256).ToUpperInvariant()) {
            throw "The cached pickup-book override failed verification."
        }
        $markers = $manifest.hidePlayerMarkersOverride
        if (-not $markers -or ([string]$markers.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The player-marker override metadata is invalid."
        }
        $markersPath = Resolve-CacheEntry ([string]$markers.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $markersPath -PathType Leaf) -or
            (Get-Item -LiteralPath $markersPath).Length -ne [long]$markers.bytes -or
            (Get-Sha256 $markersPath) -ne ([string]$markers.sha256).ToUpperInvariant()) {
            throw "The cached player-marker override failed verification."
        }
        foreach ($gravesVariant in @($manifest.hideGravesMarkersOverride, $manifest.hideCombinedMarkersOverride)) {
            if (-not $gravesVariant -or ([string]$gravesVariant.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
                throw "The Graves-marker override metadata is invalid."
            }
            $gravesPath = Resolve-CacheEntry ([string]$gravesVariant.entry) $CacheRoot
            if (-not (Test-Path -LiteralPath $gravesPath -PathType Leaf) -or
                (Get-Item -LiteralPath $gravesPath).Length -ne [long]$gravesVariant.bytes -or
                (Get-Sha256 $gravesPath) -ne ([string]$gravesVariant.sha256).ToUpperInvariant()) {
                throw "The cached Graves-marker override failed verification."
            }
        }
        $healthLines = $manifest.hideHealthLinesOverride
        if (-not $healthLines -or ([string]$healthLines.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The health-line override metadata is invalid."
        }
        $healthLinesPath = Resolve-CacheEntry ([string]$healthLines.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $healthLinesPath -PathType Leaf) -or
            (Get-Item -LiteralPath $healthLinesPath).Length -ne [long]$healthLines.bytes -or
            (Get-Sha256 $healthLinesPath) -ne ([string]$healthLines.sha256).ToUpperInvariant()) {
            throw "The cached health-line override failed verification."
        }
        $classicFill = $manifest.classicAbilityFillOverride
        if (-not $classicFill -or ([string]$classicFill.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The classic ability-fill override metadata is invalid."
        }
        $classicFillPath = Resolve-CacheEntry ([string]$classicFill.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $classicFillPath -PathType Leaf) -or
            (Get-Item -LiteralPath $classicFillPath).Length -ne [long]$classicFill.bytes -or
            (Get-Sha256 $classicFillPath) -ne ([string]$classicFill.sha256).ToUpperInvariant()) {
            throw "The cached classic ability-fill override failed verification."
        }
        $colorFix = $manifest.colorFixOverride
        if (-not $colorFix -or ([string]$colorFix.sha256).ToUpperInvariant() -notmatch '^[0-9A-F]{64}$') {
            throw "The ColorFix override metadata is invalid."
        }
        $colorFixPath = Resolve-CacheEntry ([string]$colorFix.entry) $CacheRoot
        if (-not (Test-Path -LiteralPath $colorFixPath -PathType Leaf) -or
            (Get-Item -LiteralPath $colorFixPath).Length -ne [long]$colorFix.bytes -or
            (Get-Sha256 $colorFixPath) -ne ([string]$colorFix.sha256).ToUpperInvariant()) {
            throw "The cached ColorFix override failed verification."
        }
        Write-Host "Cache verification passed: 32 skyboxes and 9 visual fixes." -ForegroundColor Green
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
    $currentIsLegacy = $false
    $unknownManagedHash = $null
    if (Test-Path -LiteralPath $managedTarget -PathType Leaf) {
        $currentHash = Get-Sha256 $managedTarget
        if ($knownManagedHashes.ContainsKey($currentHash)) {
            $currentSelection = $knownManagedHashes[$currentHash]
            $currentIsLegacy = $legacyManagedHashes.ContainsKey($currentHash)
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
    $legacyPathsPresent = @()
    $unknownLegacy = @()
    foreach ($legacy in $legacyDefinitions) {
        Assert-ChildPath $legacy.Path $addonsRoot "the Deadlock addons directory"
        if (Test-Path -LiteralPath $legacy.Path -PathType Leaf) {
            $legacyPathsPresent += $legacy.Path
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
        if ($currentIsLegacy) {
            Write-Host "The old skybox package will be updated on the next selection." -ForegroundColor Yellow
        }
        if ($legacyPresent.Count -gt 0) {
            Write-Host "Legacy selector files will be removed on the next selection." -ForegroundColor Yellow
            exit 11
        }
        exit 0
    }

    if ($Action -in @("markers-hide", "markers-show", "graves-hide", "graves-show")) {
        if (-not $addonsMounted) {
            throw "gameinfo.gi does not mount citadel/addons. Run the GameInfo installer first."
        }
        $target = Join-Path $addonsRoot "pak07_dir.vpk"
        Assert-ChildPath $target $addonsRoot "the Deadlock addons directory"
        Wait-ForManagedProcesses $DeadlockRoot $WaitTimeoutSeconds
        $playerHash = ([string]$manifest.hidePlayerMarkersOverride.sha256).ToUpperInvariant()
        $gravesHash = ([string]$manifest.hideGravesMarkersOverride.sha256).ToUpperInvariant()
        $combinedHash = ([string]$manifest.hideCombinedMarkersOverride.sha256).ToUpperInvariant()
        $installedHash = if (Test-Path -LiteralPath $target -PathType Leaf) { Get-Sha256 $target } else { "" }
        if ($installedHash -and $installedHash -notin @($playerHash, $gravesHash, $combinedHash)) {
            throw "pak07_dir.vpk belongs to another mod. Refusing to overwrite or remove it."
        }
        $hidePlayers = $installedHash -in @($playerHash, $combinedHash)
        $hideGraves = $installedHash -in @($gravesHash, $combinedHash)
        if ($Action.StartsWith("markers-", [StringComparison]::Ordinal)) {
            $hidePlayers = $Action -eq "markers-hide"
        } else {
            $hideGraves = $Action -eq "graves-hide"
        }
        $desired = if ($hidePlayers -and $hideGraves) { $manifest.hideCombinedMarkersOverride } elseif ($hidePlayers) { $manifest.hidePlayerMarkersOverride } elseif ($hideGraves) { $manifest.hideGravesMarkersOverride } else { $null }
        $desiredHash = if ($desired) { ([string]$desired.sha256).ToUpperInvariant() } else { "" }
        if ($installedHash -eq $desiredHash) {
            Write-Host "Marker settings are already applied." -ForegroundColor Green
            exit 0
        }
        if (Test-Path -LiteralPath ($target + ".patchwin-new")) {
            throw "A temporary pak07_dir.vpk already exists; refusing to replace it."
        }
        New-Item -ItemType Directory -Path $addonsRoot -Force | Out-Null
        if ($installedHash) {
            $backupPath = Join-Path $BackupRoot ("markers-" + (Get-Date -Format "yyyyMMdd-HHmmss-fff"))
            New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
            $backupFile = Join-Path $backupPath "pak07_dir.vpk"
            Copy-Item -LiteralPath $target -Destination $backupFile
            if ((Get-Sha256 $backupFile) -ne $installedHash) { throw "Marker backup failed verification." }
        }
        if ($desired) {
            $source = Resolve-CacheEntry ([string]$desired.entry) $CacheRoot
            Copy-VerifiedVariant $source $target ([long]$desired.bytes) $desiredHash
        } else {
            Remove-Item -LiteralPath $target -Force
        }
        Write-Host "Marker settings applied. Restart Deadlock to see the change." -ForegroundColor Green
        exit 0
    }

    if ($Action -in @("veil-hide", "veil-show", "smoke-hide", "smoke-show", "names-hide", "names-show", "book-hide", "book-show", "lines-hide", "lines-show", "fill-hide", "fill-show", "colorfix-on", "colorfix-off")) {
        if (-not $addonsMounted) {
            throw "gameinfo.gi does not mount citadel/addons. Run the GameInfo installer first."
        }
        $isSmoke = $Action.StartsWith("smoke-", [StringComparison]::Ordinal)
        $isNames = $Action.StartsWith("names-", [StringComparison]::Ordinal)
        $isBook = $Action.StartsWith("book-", [StringComparison]::Ordinal)
        $isLines = $Action.StartsWith("lines-", [StringComparison]::Ordinal)
        $isFill = $Action.StartsWith("fill-", [StringComparison]::Ordinal)
        $isColorFix = $Action.StartsWith("colorfix-", [StringComparison]::Ordinal)
        $effectName = if ($isColorFix) { "ColorFix" } elseif ($isFill) { "Modern ability fill" } elseif ($isLines) { "HP-bar lines" } elseif ($isBook) { "Pickup book" } elseif ($isNames) { "Unit names" } elseif ($isSmoke) { "Factory smoke" } else { "Base veil" }
        $override = if ($isColorFix) { $manifest.colorFixOverride } elseif ($isFill) { $manifest.classicAbilityFillOverride } elseif ($isLines) { $manifest.hideHealthLinesOverride } elseif ($isBook) { $manifest.hidePickupBookOverride } elseif ($isNames) { $manifest.hideNamesOverride } elseif ($isSmoke) { $manifest.factorySmokeOverride } else { $manifest.baseVeilOverride }
        $targetName = if ($isColorFix) { "pak10_dir.vpk" } elseif ($isFill) { "pak09_dir.vpk" } elseif ($isLines) { "pak08_dir.vpk" } elseif ($isBook) { "pak06_dir.vpk" } elseif ($isNames) { "pak05_dir.vpk" } elseif ($isSmoke) { "pak04_dir.vpk" } else { "pak03_dir.vpk" }
        $backupPrefix = if ($isColorFix) { "colorfix-" } elseif ($isFill) { "ability-fill-" } elseif ($isLines) { "hp-bar-lines-" } elseif ($isBook) { "pickup-book-" } elseif ($isNames) { "unit-names-" } elseif ($isSmoke) { "factory-smoke-" } else { "base-veil-" }
        if (-not $override) { throw "The skybox cache has no $effectName override." }
        $overrideBytes = [long]$override.bytes
        $overrideHash = ([string]$override.sha256).ToUpperInvariant()
        if ($overrideBytes -le 0 -or $overrideHash -notmatch '^[0-9A-F]{64}$') {
            throw "The $effectName override metadata is invalid."
        }
        $overrideSource = Resolve-CacheEntry ([string]$override.entry) $CacheRoot
        $overrideTarget = Join-Path $addonsRoot $targetName
        Assert-ChildPath $overrideTarget $addonsRoot "the Deadlock addons directory"
        Wait-ForManagedProcesses $DeadlockRoot $WaitTimeoutSeconds

        $overrideInstalled = Test-Path -LiteralPath $overrideTarget -PathType Leaf
        $installedHash = if ($overrideInstalled) { Get-Sha256 $overrideTarget } else { "" }
        $legacyFillHashes = @(
            'AA69FCB3F9E488654D66DD6C6D3B501109106377A12D03B74F6C2B4C65F185CB',
            'BC31A1C4A901759671E6D13E10A0DCF3C806326110B42BC4F738C6B736F4DB3C',
            'C2732D4F9DF4E8899A3EB3BC220686BC9F43513191BBC640B80B3045F2257C25',
            '56A6BA1858D3DFCDE0FC3DF67298831663BD4A1BA3AF864EFCC9614029079C7B',
            '860E312D79820B322BA6A6F4914E333794211A3B87279F7822F379578BCCAC91'
        )
        $legacyFillInstalled = $isFill -and $installedHash -in $legacyFillHashes
        if ($overrideInstalled -and $installedHash -ne $overrideHash -and -not $legacyFillInstalled) {
            throw "$targetName belongs to another mod. Refusing to overwrite or remove it."
        }
        if (($isColorFix -and $Action -eq "colorfix-on") -or $Action.EndsWith("-hide", [StringComparison]::Ordinal)) {
            if ($overrideInstalled -and -not $legacyFillInstalled) {
                Write-Host "$effectName is already enabled." -ForegroundColor Green
                exit 0
            }
            if (Test-Path -LiteralPath ($overrideTarget + ".patchwin-new")) {
                throw "A temporary $targetName already exists; refusing to replace it."
            }
            New-Item -ItemType Directory -Path $addonsRoot -Force | Out-Null
            if ($legacyFillInstalled) {
                $backupPath = Join-Path $BackupRoot ($backupPrefix + (Get-Date -Format "yyyyMMdd-HHmmss-fff"))
                New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
                $backupFile = Join-Path $backupPath $targetName
                Copy-Item -LiteralPath $overrideTarget -Destination $backupFile
                if ((Get-Sha256 $backupFile) -ne $installedHash) { throw "$effectName backup failed verification." }
            }
            Copy-VerifiedVariant $overrideSource $overrideTarget $overrideBytes $overrideHash
            Write-Host "$effectName enabled. Restart Deadlock to see the change." -ForegroundColor Green
        } else {
            if (-not $overrideInstalled) {
                Write-Host "$effectName is already disabled." -ForegroundColor Green
                exit 0
            }
            $backupPath = Join-Path $BackupRoot ($backupPrefix + (Get-Date -Format "yyyyMMdd-HHmmss-fff"))
            New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
            $backupFile = Join-Path $backupPath $targetName
            Copy-Item -LiteralPath $overrideTarget -Destination $backupFile
            if ((Get-Sha256 $backupFile) -ne $installedHash) { throw "$effectName backup failed verification." }
            Remove-Item -LiteralPath $overrideTarget -Force
            if (Test-Path -LiteralPath $overrideTarget) { throw "$effectName override was not removed." }
            Write-Host "$effectName disabled. Restart Deadlock to see the change." -ForegroundColor Green
            Write-Host "Backup: $backupPath"
        }
        exit 0
    }

    if (-not $addonsMounted) {
        throw "gameinfo.gi does not mount citadel/addons. Run the GameInfo installer first."
    }

    Wait-ForManagedProcesses $DeadlockRoot $WaitTimeoutSeconds

    if ($currentSelection -eq $Selection -and -not $currentIsLegacy -and
        -not $unknownManagedHash -and $legacyPathsPresent.Count -eq 0) {
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
                $currentSelection -ne "vanilla" -and -not $currentIsLegacy) {
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
                if ((Get-Sha256 $backupFile) -ne $pathHash) {
                    throw "Backup verification failed: $backupFile"
                }
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

        foreach ($legacyPath in $legacyPathsPresent) {
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
        removedLegacy = $legacyPathsPresent
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
