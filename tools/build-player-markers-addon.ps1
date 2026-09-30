param(
    [ValidateSet('PlayerMarkers', 'GravesMarkers', 'CombinedMarkers', 'HealthLines', 'ClassicAbilityFill')]
    [string]$StyleKind = 'PlayerMarkers',
    [string]$BaseStyle = '',
    [string]$OutputVpk = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
function Fit-Rule([string]$Original, [string]$Modified) {
    if ($Modified.Length -gt $Original.Length) {
        throw "A replacement rule is too long: $Modified"
    }
    return $Modified.PadRight($Original.Length)
}

if ($StyleKind -eq 'ClassicAbilityFill') {
    $styleName = 'citadel_hud_ability_upgrade_pips'
    $vpkName = 'classic-ability-fill_dir.vpk'
    $expectedBaseHash = 'BC4AF182AAE5A8E4F78E1B56D8ED8AF2A65E0FFD16E4EFCE466F17247F7E858D'
    $oldFill = 'background-color: deadlockGreenDarker;'
    $newFill = 'background-color: #362147;'
    $oldCheck = 'wash-color: deadlockGreenDark;wash-color: offBlack;opacity: 0.5;'
    $newCheck = 'wash-color: spiritLightColor;opacity: 1;'
    $oldClickable = "@keyframes 'IconClickable'{0%{wash-color: deadlockGreen&50;}50%{wash-color: deadlockGreen;}100%{wash-color: deadlockGreen&50;}}"
    $newClickable = "@keyframes 'IconClickable'{0%{wash-color: #C99AF2&50;}50%{wash-color: #C99AF2;}100%{wash-color: #C99AF2&50;}}"
    $oldHover = "@keyframes 'IconHover'{0%{wash-color: deadlockGreen;}50%{wash-color: deadlockGreen;}100%{wash-color: deadlockGreen;}}"
    $newHover = "@keyframes 'IconHover'{0%{wash-color: #C99AF2;}50%{wash-color: #C99AF2;}100%{wash-color: #C99AF2;}}"
    $oldBuild = 'wash-color: buildsLightColor;border: 1px solid #ffffff;brightness: 1.5;'
    $newBuild = 'wash-color:#FC5;animation-name:BuildIconHover;border:2px solid #FFF;'
    $oldBuildHover = "@keyframes 'BuildIconHover'{0%{wash-color: buildsLightColor;}50%{wash-color: white;}100%{wash-color: buildsLightColor;}}"
    $newBuildHover = "@keyframes 'BuildIconHover'{0%{wash-color: #FFCC55;}50%{wash-color: #FFF3C1;}100%{wash-color: #FFCC55;}}"
    $replacements = @(
        @($oldFill, (Fit-Rule $oldFill $newFill)),
        @($oldCheck, (Fit-Rule $oldCheck $newCheck)),
        @($oldClickable, (Fit-Rule $oldClickable $newClickable)),
        @($oldHover, (Fit-Rule $oldHover $newHover)),
        @($oldBuild, (Fit-Rule $oldBuild $newBuild)),
        @($oldBuildHover, (Fit-Rule $oldBuildHover $newBuildHover))
    )
    $headerStyle = Join-Path $repositoryRoot 'source\overrides\hud_abilities.vcss_c'
    $expectedHeaderHash = 'D846371B9F5E992FEF38E9885BE0A322F67CD839D8B017341F9054BDB60B4C99'
    $oldHeader = 'border-top: 6px solid deadlockGreen;'
    $newHeader = 'border-top: 6px solid #CE90FF;'
    $headerReplacements = ,@($oldHeader, ($newHeader + (' ' * ($oldHeader.Length - $newHeader.Length))))
    $apHudStyle = Join-Path $repositoryRoot 'source\overrides\hud.vcss_c'
    $expectedApHudHash = '4CBF7AD312EC9EB81377122185296102668DBB94D233FA7FEC0EC07081AF27B1'
    $apRules = @(
        @('wash-color: deadlockGreen;}.APCurrencyAmount{', 'wash-color: #d8b8ff;}.APCurrencyAmount{'),
        @('color: deadlockGreen;}.infiniteMoney .APCurrencyAmount{', 'color: #d8b8ff;}.infiniteMoney .APCurrencyAmount{'),
        @('#hudAPInfinite{visibility: collapse;wash-color: deadlockGreen&80;}', '#hudAPInfinite{visibility: collapse;wash-color: #d8b8ff&80;}')
    )
    foreach ($rule in $apRules) {
        $rule[1] += ' ' * ($rule[0].Length - $rule[1].Length)
    }
    $oldHint = '.unlockMessage,.upgradeMessage{visibility: collapse;margin-top: 260px;horizontal-align: center;background-color: deadlockGreenDark;'
    $newHint = '.unlockMessage,.upgradeMessage{visibility: collapse;margin-top: 260px;horizontal-align: center;background-color: #955BC3;'
    $oldHintPulse = "@keyframes 'unlockAvailable'{0%{pre-transform-scale2d: 1.2;background-color: deadlockGreen;}50%{pre-transform-scale2d: 1;background-color: deadlockGreenDark;}100%{pre-transform-scale2d: 1.2;background-color: deadlockGreen;}}"
    $newHintPulse = "@keyframes 'unlockAvailable'{0%{pre-transform-scale2d: 1.2;background-color: #B77BDF;}50%{pre-transform-scale2d: 1;background-color: #955BC3;}100%{pre-transform-scale2d: 1.2;background-color: #B77BDF;}}"
    $apRules += ,@($oldHint, (Fit-Rule $oldHint $newHint))
    $apRules += ,@($oldHintPulse, (Fit-Rule $oldHintPulse $newHintPulse))

    $iconStyle = Join-Path $repositoryRoot 'source\overrides\hud_ability_icon.vcss_c'
    $expectedIconHash = '1C3BFFE6357371A3BAA03FFB75D8118AADD49EB2AB016FBDF122C07FB0601B04'
    $oldIconPulse = "@keyframes 'FlashAbilityBG'{0%{opacity: 0.3;wash-color: deadlockGreenDark;pre-transform-scale2d: 1;}50%{opacity: 1.0;wash-color: deadlockGreenDark;pre-transform-scale2d: 1.1;}100%{opacity: 0.3;wash-color: deadlockGreenDark;pre-transform-scale2d: 1;}}"
    $newIconPulse = "@keyframes 'FlashAbilityBG'{0%{opacity: 0.3;wash-color: #8E59B0;pre-transform-scale2d: 1;}50%{opacity: 1.0;wash-color: #8E59B0;pre-transform-scale2d: 1.1;}100%{opacity: 0.3;wash-color: #8E59B0;pre-transform-scale2d: 1;}}"
    $iconRules = @(
        @('background-color: #E1A0FF;background-color: deadlockGreen;', (Fit-Rule 'background-color: #E1A0FF;background-color: deadlockGreen;' 'background-color: #E1A0FF;background-color: #C99AF2;')),
        @('pre-transform-scale2d: 1.03;wash-color: deadlockGreen;', (Fit-Rule 'pre-transform-scale2d: 1.03;wash-color: deadlockGreen;' 'pre-transform-scale2d: 1.03;wash-color: #C99AF2;')),
        @($oldIconPulse, (Fit-Rule $oldIconPulse $newIconPulse)),
        @('.ability_upgradable .ability_bg{saturation: 1;wash-color: deadlockGreen;', (Fit-Rule '.ability_upgradable .ability_bg{saturation: 1;wash-color: deadlockGreen;' '.ability_upgradable .ability_bg{saturation: 1;wash-color: #B575D6;'))
    )

    $tooltipStyle = Join-Path $repositoryRoot 'source\overrides\citadel_tooltip_ability_upgrade_button.vcss_c'
    $expectedTooltipHash = '1504D0FFB33CDD3EB0C98FAA38CF477BD64CB359B68E244B682A6CFEA3964809'
    $oldAvailable = 'CitadelAbilityUpgradeButton.UpgradeAvailable .abilityUpgradeContent{background-image: url("s2r://panorama/images/tooltips/ability/ap_rect_available_png.vtex");background-size: 100% 100%;color: offBlack;}'
    $newAvailable = 'CitadelAbilityUpgradeButton.UpgradeAvailable .abilityUpgradeContent{background-image:none;background-color:#BA8FE0;}.build_suggestion .UpgradeAvailable .abilityUpgradeContent{background-color:#FFD166;}'
    $oldBuildTag = '.build_next{font-weight: bold;text-transform: uppercase;font-size: 14px;text-align: center;horizontal-align: center;background-color: buildsDarkColor;padding: 0px 5px 1px 5px;border-radius: 2px;text-overflow: shrink;max-width: 110px;}'
    $newBuildTag = '.build_next{font-weight:bold;text-transform:uppercase;font-size:14px;text-align:center;horizontal-align:center;background-color:#FFD166;color:#241600;padding:0px 5px 1px 5px;border-radius:2px;text-overflow:shrink;max-width:110px;}'
    $oldCardBase = 'CitadelAbilityUpgradeButton .abilityUpgradeContent{background-image: url("s2r://panorama/images/tooltips/ability/ap_rect_default_png.vtex");background-size: 100% 100%;}'
    $newCardBase = 'CitadelAbilityUpgradeButton .abilityUpgradeContent{background-image:none;background-color:#28242B;border:1px solid #694F7C;}'
    $oldBacker = 'background-image: url("s2r://panorama/images/tooltips/ability/ap_backer_available_png.vtex");'
    $oldDefaultCard = 'CitadelAbilityUpgradeButton.canAffordUpgrade .abilityUpgradeContent,CitadelAbilityUpgradeButton.abilityNotTrained .abilityUpgradeContent,CitadelAbilityUpgradeButton.abilityNotTrained.cantAffordUpgrade .abilityUpgradeContent{background-image: url("s2r://panorama/images/tooltips/ability/ap_rect_default_png.vtex");}'
    $newDefaultCard = 'CitadelAbilityUpgradeButton.canAffordUpgrade .abilityUpgradeContent,CitadelAbilityUpgradeButton.abilityNotTrained .abilityUpgradeContent{background-image:none;background-color:gradient( linear, 0% 0%, 0% 100%, from( #302C34 ), to( #1B1B20 ) );border:1px solid #694F7C;}'
    $oldRadialCard = 'CitadelTooltipAbilityDetails#CitadelTooltipAbilitiesDetailRadial CitadelAbilityUpgradeButton.canAffordUpgrade .abilityUpgradeContent,CitadelTooltipAbilityDetails#CitadelTooltipAbilitiesDetailRadial CitadelAbilityUpgradeButton.abilityNotTrained .abilityUpgradeContent,CitadelTooltipAbilityDetails#CitadelTooltipAbilitiesDetailRadial CitadelAbilityUpgradeButton.abilityNotTrained.cantAffordUpgrade .abilityUpgradeContent{background-image: url("s2r://panorama/images/tooltips/ability/ap_backer_default_png.vtex");}'
    $newRadialCard = 'CitadelTooltipAbilityDetails#CitadelTooltipAbilitiesDetailRadial CitadelAbilityUpgradeButton.canAffordUpgrade .abilityUpgradeContent,CitadelTooltipAbilityDetails#CitadelTooltipAbilitiesDetailRadial CitadelAbilityUpgradeButton.abilityNotTrained .abilityUpgradeContent{background-image:none;background-color:gradient( linear, 0% 0%, 0% 100%, from( #302C34 ), to( #1B1B20 ) );border:1px solid #694F7C;}'
    $tooltipRules = @(
        @('background-color: deadlockGreenDarker;', (Fit-Rule 'background-color: deadlockGreenDarker;' 'background-color: #3F274D;')),
        @('background-image: url("s2r://panorama/images/hud/ap_icon.vsvg");wash-color: deadlockGreen;', (Fit-Rule 'background-image: url("s2r://panorama/images/hud/ap_icon.vsvg");wash-color: deadlockGreen;' 'background-image: url("s2r://panorama/images/hud/ap_icon.vsvg");wash-color: #D8B8FF;')),
        @('background-image: url("s2r://panorama/images/hud/ability_upgrade_check_psd.vtex");wash-color: fortitudeBrightColor;', (Fit-Rule 'background-image: url("s2r://panorama/images/hud/ability_upgrade_check_psd.vtex");wash-color: fortitudeBrightColor;' 'background-image: url("s2r://panorama/images/hud/ability_upgrade_check_psd.vtex");wash-color: #D8B8FF;')),
        @($oldCardBase, (Fit-Rule $oldCardBase $newCardBase)),
        @($oldAvailable, (Fit-Rule $oldAvailable $newAvailable)),
        @($oldBacker, (Fit-Rule $oldBacker 'background-image:none;')),
        @($oldDefaultCard, (Fit-Rule $oldDefaultCard $newDefaultCard)),
        @($oldRadialCard, (Fit-Rule $oldRadialCard $newRadialCard)),
        @($oldBuildTag, (Fit-Rule $oldBuildTag $newBuildTag))
    )
    $tooltipDetailsStyle = Join-Path $repositoryRoot 'source\overrides\citadel_tooltip_ability_details.vcss_c'
    $expectedTooltipDetailsHash = '90FCFFF8FBCB9EF0DE7C7A2E9166E88688B5E489B16998B3EBB4A4D0D8753BF0'
    $oldTexturePanel = '.TexturePanel{width: 100%;height: 100%;background-image: url("s2r://panorama/images/tooltips/ability/ability_tooltip_bg_png.vtex");background-size: 100% 100%;img-shadow: 0px 0px 20px 3.0 #000000;margin: 10px 0px;animation-duration: 1s;animation-iteration-count: infinite;animation-direction: alternate;}'
    $newTexturePanel = '.TexturePanel{width:100%;height:100%;background-image:url("s2r://panorama/images/tooltips/ability/ability_tooltip_bg_png.vtex");background-size:100% 100%;img-shadow:0px 0px 20px 3.0 #000000;margin:10px 0px;animation-duration:1s;animation-iteration-count:infinite;hue-rotation:110deg;}'
    $tooltipDetailsRules = ,@($oldTexturePanel, (Fit-Rule $oldTexturePanel $newTexturePanel))
} elseif ($StyleKind -eq 'HealthLines') {
    $styleName = 'hud_health'
    $vpkName = 'hide-health-lines_dir.vpk'
    $expectedBaseHash = 'DB6613A280BDBB8EB37011407D8E75F40AD6B8230AF259FECC828BF998A72674'
    $replacements = @(
        @('#healthLines .line_large{width: 100%;height: 3px;background-color: offBlack;opacity: 0.7;}',
          '#healthLines .line_large{width: 100%;height: 3px;background-color: offBlack;opacity: 0.0;}'),
        @('#healthLines .line_small{width: 30%;height: 3px;background-color: offBlack;opacity: 0.7;}',
          '#healthLines .line_small{width: 30%;height: 3px;background-color: offBlack;opacity: 0.0;}')
    )
} else {
    $styleName = 'hud_unit_indicators_v2'
    $vpkName = if ($StyleKind -eq 'GravesMarkers') { 'hide-graves-markers_dir.vpk' } elseif ($StyleKind -eq 'CombinedMarkers') { 'hide-combined-markers_dir.vpk' } else { 'hide-player-markers_dir.vpk' }
    $expectedBaseHash = '0AEEBCEF780BB10B29B2F256E042555BB1A2C5B44B6AAD6A40162BBB602B000A'
    $oldGraveStone = '.OtherEntry.NecroGraveStone #Icon{background-color: necroColor&60;background-image: url("s2r://panorama/images/heroes/necro_tombstone_psd.vtex");background-size: 80% 80%;background-position: center center;background-repeat: no-repeat;}'
    $oldZombie = '.OtherEntry.NecroZombie #Icon{background-color: necroColor;background-image: url("s2r://panorama/images/heroes/necro_shambler_psd.vtex");background-size: 74% 74%;background-position: center center;background-repeat: no-repeat;}'
    $replacements = @()
    if ($StyleKind -in @('PlayerMarkers', 'CombinedMarkers')) {
        $replacements += ,@('.PlayerEntry.visible{visibility: visible;}', '.PlayerEntry.visible{visibility:collapse;}')
    }
    if ($StyleKind -in @('GravesMarkers', 'CombinedMarkers')) {
        $replacements += ,@($oldGraveStone, (Fit-Rule $oldGraveStone '.OtherEntry.NecroGraveStone{visibility: collapse;}'))
        $replacements += ,@($oldZombie, (Fit-Rule $oldZombie '.OtherEntry.NecroZombie{visibility: collapse;}'))
    }
}
if ([String]::IsNullOrWhiteSpace($BaseStyle)) {
    $BaseStyle = Join-Path $repositoryRoot "source\overrides\$styleName.vcss_c"
}
if ([String]::IsNullOrWhiteSpace($OutputVpk)) {
    $OutputVpk = Join-Path $repositoryRoot "unpacked\assets\overrides\$vpkName"
}
# The compiled VCSS stores its rules as plain text inside the DATA block.
# Same-length substitutions preserve every resource offset.
function Get-PatchedStyle([string]$Path, [string]$ExpectedHash, [object[]]$Rules) {
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedHash) {
        throw "The captured Deadlock stylesheet changed: $actualHash"
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $sourceText = [Text.Encoding]::ASCII.GetString($bytes)
    foreach ($replacement in $Rules) {
        $oldRule = [string]$replacement[0]
        $newRule = [string]$replacement[1]
        if ($oldRule.Length -ne $newRule.Length) {
            throw 'A replacement rule changed the byte length.'
        }
        $ruleIndex = $sourceText.IndexOf($oldRule, [StringComparison]::Ordinal)
        if ($ruleIndex -lt 0 -or $sourceText.LastIndexOf($oldRule, [StringComparison]::Ordinal) -ne $ruleIndex) {
            throw "Expected exactly one occurrence of $oldRule"
        }
        [Array]::Copy([Text.Encoding]::ASCII.GetBytes($newRule), 0, $bytes, $ruleIndex, $newRule.Length)
    }
    return ,$bytes
}

$entries = @([pscustomobject]@{
    Name = $styleName
    Directory = 'panorama/styles'
    Payload = (Get-PatchedStyle $BaseStyle $expectedBaseHash $replacements)
})
if ($StyleKind -eq 'ClassicAbilityFill') {
    $entries += [pscustomobject]@{
        Name = 'hud_abilities'
        Directory = 'panorama/styles'
        Payload = (Get-PatchedStyle $headerStyle $expectedHeaderHash $headerReplacements)
    }
    $entries += [pscustomobject]@{
        Name = 'hud'
        Directory = 'panorama/styles'
        Payload = (Get-PatchedStyle $apHudStyle $expectedApHudHash $apRules)
    }
    $entries += [pscustomobject]@{
        Name = 'hud_ability_icon'
        Directory = 'panorama/styles'
        Payload = (Get-PatchedStyle $iconStyle $expectedIconHash $iconRules)
    }
    $entries += [pscustomobject]@{
        Name = 'citadel_tooltip_ability_upgrade_button'
        Directory = 'panorama/styles/tooltips'
        Payload = (Get-PatchedStyle $tooltipStyle $expectedTooltipHash $tooltipRules)
    }
    $entries += [pscustomobject]@{
        Name = 'citadel_tooltip_ability_details'
        Directory = 'panorama/styles/tooltips'
        Payload = (Get-PatchedStyle $tooltipDetailsStyle $expectedTooltipDetailsHash $tooltipDetailsRules)
    }
}

Add-Type -TypeDefinition @'
using System;
public static class PlayerMarkerVpkCrc32
{
    private static readonly uint[] Table = BuildTable();
    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            table[i] = value;
        }
        return table;
    }
    public static uint Compute(byte[] bytes)
    {
        uint value = 0xFFFFFFFFu;
        foreach (var b in bytes)
            value = Table[(value ^ b) & 0xFF] ^ (value >> 8);
        return ~value;
    }
}
'@

function Write-ZString([IO.BinaryWriter]$Writer, [string]$Value) {
    $Writer.Write([Text.Encoding]::ASCII.GetBytes($Value))
    $Writer.Write([byte]0)
}

$treeStream = [IO.MemoryStream]::new()
$treeWriter = [IO.BinaryWriter]::new($treeStream, [Text.Encoding]::ASCII, $true)
Write-ZString $treeWriter 'vcss_c'
$offset = 0
foreach ($directory in @($entries | Select-Object -ExpandProperty Directory -Unique)) {
    Write-ZString $treeWriter $directory
    foreach ($entry in @($entries | Where-Object { $_.Directory -eq $directory })) {
        $payload = [byte[]]$entry.Payload
        Write-ZString $treeWriter $entry.Name
        $treeWriter.Write([uint32][PlayerMarkerVpkCrc32]::Compute($payload))
        $treeWriter.Write([uint16]0)
        $treeWriter.Write([uint16]0x7fff)
        $treeWriter.Write([uint32]$offset)
        $treeWriter.Write([uint32]$payload.Length)
        $treeWriter.Write([uint16]0xffff)
        $offset += $payload.Length
    }
    $treeWriter.Write([byte]0)
}
$treeWriter.Write([byte]0)
$treeWriter.Write([byte]0)
$treeWriter.Flush()
$tree = $treeStream.ToArray()
$treeWriter.Dispose()
$treeStream.Dispose()

$outputDirectory = Split-Path -Parent $OutputVpk
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$fileStream = [IO.File]::Open($OutputVpk, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $writer = [IO.BinaryWriter]::new($fileStream, [Text.Encoding]::ASCII, $true)
    $writer.Write([uint32]0x55aa1234)
    $writer.Write([uint32]2)
    $writer.Write([uint32]$tree.Length)
    $writer.Write([uint32]$offset)
    $writer.Write([uint32]0)
    $writer.Write([uint32]48)
    $writer.Write([uint32]0)
    $writer.Write($tree)
    foreach ($entry in $entries) {
        $writer.Write([byte[]]$entry.Payload)
    }
    $md5 = [Security.Cryptography.MD5]::Create()
    $writer.Write($md5.ComputeHash($tree))
    $writer.Write($md5.ComputeHash([byte[]]@()))
    $writer.Flush()
    $writer.Dispose()
} finally {
    $fileStream.Dispose()
}
$wholeHash = $md5.ComputeHash([IO.File]::ReadAllBytes($OutputVpk))
$md5.Dispose()
$appendStream = [IO.File]::Open($OutputVpk, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $appendStream.Write($wholeHash, 0, $wholeHash.Length)
} finally {
    $appendStream.Dispose()
}

Write-Output "Built $OutputVpk"
