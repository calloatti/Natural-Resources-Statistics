param(
    # Auto-detects the exact name of the parent folder
    [string] $ModName = (Split-Path (Split-Path $PSScriptRoot -Parent) -Leaf),
    
    # Base path for Unity projects (each version gets its own project folder)
    [string] $UnityProjectBasePath = "C:\Users\calloatti\source\repos",
    
    # Output folder defaults to the current script directory
    [string] $OutputFolder = $PSScriptRoot,

    # Auto-detects version from the current folder leaf (e.g., 'Version-1.0' -> '1.0')
    [string] $CompatibilityVersion = ((Split-Path $PSScriptRoot -Leaf) -replace '^Version-', '')
)

# Unity editor version + changeset required to build asset bundles for each game
# version. Matches the game runtime's own Unity build (from UnityPlayer.dll).
$UnityVersionByCompatibilityVersion = @{
    "1.0" = "6000.3.6f1"
    "1.1" = "6000.5.5f1"
}
$UnityChangesetByCompatibilityVersion = @{
    "1.0" = "bbb010bdb8a3"
    "1.1" = "d16e074b49fd"
}

$UnityVersion = $UnityVersionByCompatibilityVersion[$CompatibilityVersion]
$UnityChangeset = $UnityChangesetByCompatibilityVersion[$CompatibilityVersion]
if (-not $UnityVersion) {
    Write-Error "No Unity version mapped for compatibility '$CompatibilityVersion'."
    exit 1
}

# Derive Unity project path from version (each project at timberborn-modding-<UnityVersion>)
$UnityProjectPath = Join-Path $UnityProjectBasePath "timberborn-modding-$UnityVersion"

$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath($UnityProjectPath)
$targetOutput = [System.IO.Path]::GetFullPath($OutputFolder)
$logPath = Join-Path $targetOutput "unitybuild.log"
$errPath = Join-Path $targetOutput "unitybuild.err"
if (Test-Path $errPath) { Remove-Item -LiteralPath $errPath -Force }

# 0. Ensure clean Library when Unity version changes
$versionFile = Join-Path $projectRoot "ProjectSettings\ProjectVersion.txt"
$libraryDir = Join-Path $projectRoot "Library"
if (Test-Path $versionFile) {
    $currentVersion = Get-Content $versionFile -Raw
    if (-not $currentVersion.Contains($UnityVersion)) {
        Write-Host "Unity version mismatch (project: $($currentVersion.Trim()) vs target: $UnityVersion). Deleting Library..."
        if (Test-Path $libraryDir) { Remove-Item -Recurse -Force $libraryDir }
    }
}

Write-Host "======================================================="
# FIXED: Wrapped in $() so the colon doesn't break the parser
Write-Host "$($ModName): UNITY ASSET BUNDLE EXPORT STARTING"
Write-Host "======================================================="

# 0b. Copy AssetBundles source (excluding built bundles) + manifest to Unity project
$sourceAssetBundles = Join-Path $targetOutput "AssetBundles"
$destModRoot = Join-Path $projectRoot "Assets\Mods\$ModName"
$destAssetBundles = Join-Path $destModRoot "AssetBundles"

if (Test-Path $sourceAssetBundles) {
    Write-Host "Copying AssetBundles source to Unity project..."
    if (Test-Path $destAssetBundles) { Remove-Item -Recurse -Force $destAssetBundles }
    New-Item -ItemType Directory -Path $destAssetBundles -Force | Out-Null
    # Copy all except built bundle binaries and manifests
    Get-ChildItem -Path $sourceAssetBundles -Recurse -File | 
        Where-Object { $_.Name -notlike '*_win' -and $_.Name -notlike '*_mac' -and $_.Extension -ne '.manifest' } |
        ForEach-Object {
            $relPath = $_.FullName.Substring($sourceAssetBundles.Length + 1)
            $destPath = Join-Path $destAssetBundles $relPath
            $destDir = Split-Path $destPath -Parent
            if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
            Copy-Item -LiteralPath $_.FullName -Destination $destPath -Force
        }
}

# Copy manifest.json from script's folder (Version-*/manifest.json)
$manifestSrc = Join-Path $PSScriptRoot "manifest.json"
if (Test-Path $manifestSrc) {
    $manifestDst = Join-Path $destModRoot "manifest.json"
    Copy-Item -LiteralPath $manifestSrc -Destination $manifestDst -Force
}

# 1. Resolve Unity Editor (download via Unity Hub if missing)
$unityExe = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"

if (-not (Test-Path $unityExe)) {
    Write-Host "Editor $UnityVersion not installed. Installing via Unity Hub..."
    & "C:\Program Files\Unity Hub\Unity Hub.exe" -- --headless install --version $UnityVersion --changeset $UnityChangeset

    # Hub install is asynchronous: poll until the editor is fully extracted
    # (Unity.exe alone appears mid-install), up to 30 minutes.
    $installFiles = @(
        "$unityExe",
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\Resources\PackageManager\Server\UnityPackageManager.exe",
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\Managed\UnityEngine.dll"
    )
    for ($i = 0; $i -lt 180; $i++) {
        if (($installFiles | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) { break }
        Start-Sleep -Seconds 10
    }

    if (-not (Test-Path $unityExe)) {
        Write-Error "Editor $UnityVersion still missing after Unity Hub install. Check unitybuild.log"
        exit 1
    }
}

# 2. Command Line Arguments for Native Wrapper
$unityArguments = @(
    "-batchmode", "-quit",
    "-projectPath", "`"$projectRoot`"",
    "-executeMethod", "NativeModBuilderBatch.Build",
    "-mod", "`"$ModName`"",
    "-logFile", "`"$logPath`""
)

if ($CompatibilityVersion) {
    $unityArguments += @("-compatibilityVersion", "`"$CompatibilityVersion`"")
}

Write-Host "Running Unity export for $ModName..."
$process = Start-Process -FilePath $unityExe -ArgumentList ($unityArguments -join " ") -WindowStyle Hidden -Wait -PassThru

if ($process.ExitCode -eq 0) {
    # 3. Post-Build Routing
    $liveModFolder = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Timberborn\Mods\$ModName"
    if ($CompatibilityVersion) {
        # Windows is case-insensitive: Unity's lowercase 'version-<compat>' and the
        # C# build's 'Version-<compat>' are the same folder, so match case-insensitively.
        $generatedBundles = Get-ChildItem -Path $liveModFolder -Directory -Filter "version-$CompatibilityVersion" | Select-Object -First 1
        if ($null -ne $generatedBundles) {
            $generatedBundles = Join-Path $generatedBundles.FullName "AssetBundles"
        }
    } else {
        $generatedBundles = Join-Path $liveModFolder "AssetBundles"
    }
    $generatedBundles = Get-Item -LiteralPath $generatedBundles -ErrorAction SilentlyContinue

    if ($null -ne $generatedBundles) {
        Write-Host "Routing AssetBundles to $targetOutput..."
        
        $finalDestination = Join-Path $targetOutput "AssetBundles"
        if (-not (Test-Path $finalDestination)) { New-Item -ItemType Directory -Path $finalDestination -Force | Out-Null }

        # Copy the bundles to your source code directory
        Copy-Item -Path "$($generatedBundles.FullName)\*" -Destination $finalDestination -Recurse -Force
        
        # ALL REMOVE-ITEM/DELETE COMMANDS HAVE BEEN REMOVED FROM THIS SCRIPT.
        
        Write-Host "Unity Pipeline Complete! AssetBundles copied safely." -ForegroundColor Green
    } else {
        Write-Warning "Could not find AssetBundles folder in $liveModFolder. Check unitybuild.log"
    }
} else {
    Write-Warning "Unity build failed with exit code $($process.ExitCode)."
    Write-Host "--- LAST 20 LINES OF UNITY LOG ---" -ForegroundColor Yellow
    if (Test-Path $logPath) {
        Get-Content $logPath -Tail 20 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    }
    "Unity build failed with exit code $($process.ExitCode)" | Out-File -FilePath $errPath -Encoding UTF8
    exit $process.ExitCode
}

Write-Host "======================================================="
Write-Host " ASSET EXPORT COMPLETE." -ForegroundColor Green