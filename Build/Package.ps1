param([string]$Version = '0.4.16')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Split-Path $projectRoot -Parent
$moduleNames = @('Inheritance.Core', 'Inheritance.World', 'Inheritance.Colony')
foreach ($moduleName in $moduleNames) {
    $moduleRoot = Join-Path $projectRoot "Mods/$moduleName"
    $metadata = [xml](Get-Content -LiteralPath (Join-Path $moduleRoot 'About/About.xml') -Raw)
    if ($metadata.ModMetaData.modVersion -ne $Version) { throw "Version mismatch: $moduleName" }
    if (-not (Test-Path -LiteralPath (Join-Path $moduleRoot "Assemblies/$moduleName.dll"))) { throw "Build missing: $moduleName" }
}
foreach ($xmlFile in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Mods') -Filter '*.xml' -Recurse -File) {
    $null = [xml](Get-Content -LiteralPath $xmlFile.FullName -Raw)
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function New-VerifiedArchive([string]$Path, [object[]]$Items) {
    if (Test-Path -LiteralPath $Path) { throw "Archive already exists; preserve it or select another version: $Path" }
    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Items) {
            if ($item.File.Extension -in @('.rws', '.exe', '.log') -or
                ($item.File.Extension -eq '.dll' -and $item.File.Name -notmatch '^Inheritance\.[A-Za-z]+\.dll$')) {
                throw "Unexpected runtime or save file: $($item.File.FullName)"
            }
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.File.FullName,
                $item.Entry.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
    $check = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if ($check.Entries.Count -ne $Items.Count) { throw 'Archive entry count mismatch' }
        foreach ($entry in $check.Entries) {
            if ($entry.FullName -match '(^|/)(work|Runtime|SaveData)(/|$)|Assembly-CSharp|UnityPlayer|\.rws$') {
                throw "Forbidden archive entry: $($entry.FullName)"
            }
        }
        Write-Output "Verified $([System.IO.Path]::GetFileName($Path)): $($check.Entries.Count) files"
    } finally { $check.Dispose() }
}
$prototypeItems = @(foreach ($moduleName in $moduleNames) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot "Mods/$moduleName") -Recurse -File) {
        [pscustomobject]@{File=$file; Entry=$file.FullName.Substring((Join-Path $projectRoot 'Mods').Length + 1)}
    }
})
$prototypeItems += [pscustomobject]@{File=(Get-Item -LiteralPath (Join-Path $projectRoot 'README.md')); Entry='README.md'}
$sourceItems = @(foreach ($folder in @('Build', 'Docs', 'Mods', 'Source', 'Tests')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -Recurse -File) {
        [pscustomobject]@{File=$file; Entry=$file.FullName.Substring($projectRoot.Length + 1)}
    }
})
$sourceItems += [pscustomobject]@{File=(Get-Item -LiteralPath (Join-Path $projectRoot 'README.md')); Entry='README.md'}
$prototypePath = Join-Path $outputRoot "Inheritance-$Version-prototype.zip"
$sourcePath = Join-Path $outputRoot "Inheritance-P04-$Version-source.zip"
New-VerifiedArchive $prototypePath $prototypeItems
New-VerifiedArchive $sourcePath $sourceItems
$coreDeclaration=[xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Mods/Inheritance.Core/Defs/ModuleDefs.xml') -Raw)
$coreSchema=$coreDeclaration.Defs.'Inheritance.Core.ModuleDef'.moduleSchemaVersion
$hashLines = @("Inheritance $Version; Core schema $coreSchema; World/Colony schema 1; protocol 2", '')
foreach ($archivePath in @($prototypePath, $sourcePath)) {
    $hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    $hashLines += "$($hash.Hash)  $([System.IO.Path]::GetFileName($archivePath))"
}
$hashLines | Set-Content -LiteralPath (Join-Path $outputRoot "Inheritance-$Version-SHA256.txt") -Encoding utf8
