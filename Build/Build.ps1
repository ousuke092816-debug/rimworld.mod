param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$CompilerPath = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe',
    [switch]$WithSmokeTests,
    [switch]$WithWorld,
    [switch]$WithColony
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$managedPath = Join-Path $GamePath 'RimWorldWin64_Data\Managed'
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw "C# compiler not found: $CompilerPath" }
$referenceNames = @('Assembly-CSharp.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll', 'UnityEngine.TextRenderingModule.dll', 'netstandard.dll')
$compilerReferences = foreach ($name in $referenceNames) {
    $path = Join-Path $managedPath $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Game reference not found: $path" }
    '/reference:' + $path
}
$assemblyDir = Join-Path $projectRoot 'Mods\Inheritance.Core\Assemblies'
New-Item -ItemType Directory -Path $assemblyDir -Force | Out-Null
$assemblyPath = Join-Path $assemblyDir 'Inheritance.Core.dll'
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Source\Core') -Filter '*.cs' -File | ForEach-Object FullName)
& $CompilerPath /nologo /target:library /langversion:5 /optimize+ ("/out:" + $assemblyPath) $compilerReferences $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Core build failed' }
Write-Output "Core built: $assemblyPath"
if ($WithWorld) {
    $worldAssemblyDir = Join-Path $projectRoot 'Mods\Inheritance.World\Assemblies'
    New-Item -ItemType Directory -Path $worldAssemblyDir -Force | Out-Null
    $worldAssemblyPath = Join-Path $worldAssemblyDir 'Inheritance.World.dll'
    $worldSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Source\World') -Filter '*.cs' -File | ForEach-Object FullName)
    & $CompilerPath /nologo /target:library /langversion:5 /optimize+ ("/out:" + $worldAssemblyPath) $compilerReferences ("/reference:" + $assemblyPath) $worldSources
    if ($LASTEXITCODE -ne 0) { throw 'World build failed' }
}
if ($WithSmokeTests) {
    $testAssemblyDir = Join-Path $projectRoot 'Tests\SmokeMod\Assemblies'
    New-Item -ItemType Directory -Path $testAssemblyDir -Force | Out-Null
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Tests\SmokeMod\Source') -Filter '*.cs' -File | ForEach-Object FullName)
    & $CompilerPath /nologo /target:library /langversion:5 /optimize+ ("/out:" + (Join-Path $testAssemblyDir 'Inheritance.Smoke.dll')) $compilerReferences ("/reference:" + $assemblyPath) $testSources
    if ($LASTEXITCODE -ne 0) { throw 'Smoke harness build failed' }
    if ($WithWorld) {
        $worldTestDir = Join-Path $projectRoot 'Tests\WorldSmoke\Assemblies'
        New-Item -ItemType Directory -Path $worldTestDir -Force | Out-Null
        $worldTestSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Tests\WorldSmoke\Source') -Filter '*.cs' -File | ForEach-Object FullName)
        & $CompilerPath /nologo /target:library /langversion:5 /optimize+ ("/out:" + (Join-Path $worldTestDir 'Inheritance.WorldSmoke.dll')) $compilerReferences ("/reference:" + $assemblyPath) ("/reference:" + $worldAssemblyPath) $worldTestSources
        if ($LASTEXITCODE -ne 0) { throw 'World smoke harness build failed' }
    }
}
if ($WithColony) {
    $colonyAssemblyDir = Join-Path $projectRoot 'Mods\Inheritance.Colony\Assemblies'
    New-Item -ItemType Directory -Path $colonyAssemblyDir -Force | Out-Null
    $colonyAssembly = Join-Path $colonyAssemblyDir 'Inheritance.Colony.dll'
    $colonySources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Source\Colony') -Filter '*.cs' -File | ForEach-Object FullName)
    & $CompilerPath /nologo /target:library /langversion:5 /optimize+ ("/out:" + $colonyAssembly) $compilerReferences ("/reference:" + $assemblyPath) $colonySources
    if ($LASTEXITCODE -ne 0) { throw 'Colony build failed' }
    if ($WithSmokeTests) {
        $colonyTestDir = Join-Path $projectRoot 'Tests\ColonySmoke\Assemblies'
        New-Item -ItemType Directory -Path $colonyTestDir -Force | Out-Null
        $colonyTestSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Tests\ColonySmoke\Source') -Filter '*.cs' -File | ForEach-Object FullName)
        & $CompilerPath /nologo /target:library /langversion:5 /optimize+ ("/out:" + (Join-Path $colonyTestDir 'Inheritance.ColonySmoke.dll')) $compilerReferences ("/reference:" + (Join-Path $managedPath 'UnityEngine.ScreenCaptureModule.dll')) ("/reference:" + $assemblyPath) ("/reference:" + $colonyAssembly) $colonyTestSources
        if ($LASTEXITCODE -ne 0) { throw 'Colony smoke harness build failed' }
    }
}
