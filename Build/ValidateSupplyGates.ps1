param(
 [ValidateSet('Abstract','Physical','Legacy','ReservationScope')][string]$Group='Abstract',
 [ValidateSet('start','load','final','checkpoint')][string]$Step='start',
 [switch]$WithDlc,
 [string]$InputDataPath,
 [string]$WorkPath,
 [string]$GamePath='C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$workspace=Split-Path (Split-Path $project -Parent) -Parent
$config=if($WithDlc) {'four-dlc'} else {'base-only'}
if(@(Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue).Count) {throw 'Finish all game tests before modifying an isolated runtime'}
if($Group -eq 'Legacy' -and $Step -eq 'start') {throw 'Legacy uses an actual preserved released15 save; it does not manufacture an old-version fixture'}
if($Group -eq 'ReservationScope' -and $Step -ne 'load' -and $Step -ne 'final') {throw 'ReservationScope starts from an actual committed reservation final save'}
if($Step -eq 'checkpoint' -and $Group -ne 'Abstract') {throw 'Only Abstract has this committed checkpoint fixture'}
if(!$WorkPath) {$WorkPath=Join-Path $workspace ('work/inheritance-release16-repro-'+$Group.ToLowerInvariant())}
$resolvedWork=[IO.Path]::GetFullPath($WorkPath)
$allowed=[IO.Path]::GetFullPath((Join-Path $workspace 'work')).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
if(!$resolvedWork.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) {throw 'Supply validation WorkPath must stay inside workspace/work'}
# Check before creating directories or copying runtime files. A lexical prefix
# alone does not keep a junction or linked child inside the isolated workspace.
$cursor=$resolvedWork
$workBoundary=$allowed.TrimEnd('\','/')
while($true) {
 if(Test-Path -LiteralPath $cursor) {
  if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw ('Refuse linked validation ancestor: '+$cursor)}
 }
 if($cursor.Equals($workBoundary,[StringComparison]::OrdinalIgnoreCase)) {break}
 $cursor=[IO.Path]::GetDirectoryName($cursor)
 if(!$cursor) {throw 'Validation ancestor walk escaped its workspace boundary'}
}
if(Test-Path -LiteralPath $resolvedWork) {
 foreach($entry in Get-ChildItem -LiteralPath $resolvedWork -Force -Recurse) {
  if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw ('Refuse linked validation child: '+$entry.FullName)}
 }
}
if(!$InputDataPath) {
 $inputRoot=if($Group -eq 'Legacy') {'inheritance-legacy-pending-supply-smoke'} elseif($Group -eq 'ReservationScope') {'inheritance-release16-legacy-smoke'} else {'inheritance-smoke'}
 $InputDataPath=Join-Path $workspace ('work/'+$inputRoot+'/'+$config+'/SaveData')
}
$data=Join-Path $resolvedWork ($config+'/SaveData')
$harness=Join-Path $resolvedWork ($config+'/Harness')
New-Item -ItemType Directory -Path $harness,(Join-Path $data 'Saves') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $project 'Tests/ColonySmoke') -Force | Copy-Item -Destination $harness -Recurse -Force
foreach($module in @('Core','Colony')) {
 $about=[xml][IO.File]::ReadAllText((Join-Path $project ('Mods/Inheritance.'+$module+'/About/About.xml')))
 if($about.ModMetaData.modVersion -ne '0.4.16') {throw 'This validator expects the final 0.4.16 product'}
}
$prefix=if($Group -eq 'Abstract') {'inh-abstract-supply-'} elseif($Group -eq 'Physical') {'inh-physical-supply-'} elseif($Group -eq 'ReservationScope') {'inh-reservation-scope-'} else {'inh-pending-supply-'}
$save=if($Step -eq 'start') {'INH_SupplyIntentPrepared'} elseif($Group -eq 'Abstract') {
 if($Step -eq 'load') {'INH_AbstractSupplyArmed'} elseif($Step -eq 'checkpoint') {'INH_AbstractSupplyCheckpoint'} else {'INH_AbstractSupplyFinal'}
} elseif($Group -eq 'Physical') {if($Step -eq 'load') {'INH_PhysicalSupplyHeld'} else {'INH_PhysicalSupplyReleased'}}
elseif($Group -eq 'ReservationScope') {'INH_LegacyPendingSupplyFinal'}
else {if($Step -eq 'load') {'INH_LegacyPendingSupplyArmed'} else {'INH_LegacyPendingSupplyFinal'}}
$inputFile=$null; $inputHash=$null
if(($Step -eq 'start') -or (($Group -eq 'Legacy' -or $Group -eq 'ReservationScope') -and $Step -eq 'load')) {
 $inputFile=Join-Path $InputDataPath ('Saves/'+$save+'.rws')
 $inputHash=(Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash
 Copy-Item -LiteralPath $inputFile -Destination (Join-Path $data ('Saves/'+$save+'.rws')) -Force
 Get-ChildItem -LiteralPath $InputDataPath -Filter '*.txt' -File | Copy-Item -Destination $data -Force
}
$checkpointSource=$null; $checkpointHash=$null
if($Step -eq 'checkpoint') {
 $checkpointSource=Join-Path $data 'Saves/INH_AbstractSupplyArmed.rws'
 $checkpointSourceHash=(Get-FileHash -LiteralPath $checkpointSource -Algorithm SHA256).Hash
 $xml=New-Object Xml.XmlDocument; $xml.Load($checkpointSource)
 $payload=$xml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceModulePayloads/li[owner='inheritance.colony']/data")
 $state=New-Object Xml.XmlDocument; $state.LoadXml($payload.InnerText)
 $row=$state.SelectSingleNode("/ColonyState/transports/li[cause='abstract-supply-transit']")
 if(!$row -or $row.SelectSingleNode('stage').InnerText -ne 'InTransit') {throw 'Expected actual in-transit source checkpoint'}
 $row.SelectSingleNode('stage').InnerText='Departing'; $payload.InnerText=$state.OuterXml
 $checkpointFile=Join-Path $data ('Saves/'+$save+'.rws'); $xml.Save($checkpointFile)
 $checkpointHash=(Get-FileHash -LiteralPath $checkpointFile -Algorithm SHA256).Hash
}
$loader=Join-Path $harness 'Source/ColonySmoke.cs'; $source=[IO.File]::ReadAllText($loader)
$anchor='            string save = '
if(([regex]::Matches($source,[regex]::Escape($anchor))).Count -ne 1) {throw 'Loader anchor changed'}
$replacement='            string save = GenCommandLine.CommandLineArgPassed("'+$prefix+$Step+'") ? "'+$save+'" :'+[Environment]::NewLine
[IO.File]::WriteAllText($loader,$source.Replace($anchor,$replacement),[Text.UTF8Encoding]::new($false))
if($Group -eq 'Physical') {
 $path=Join-Path $harness 'Source/PhysicalProbe.cs'; $legacy=[IO.File]::ReadAllText($path)
 $gate='if (done || LongEventHandler.AnyEventNowOrWaiting'
 if(!$legacy.Contains($gate)) {throw 'Legacy physical probe gate changed'}
 $exclude='if (GenCommandLine.CommandLineArgPassed("'+$prefix+$Step+'") || done || LongEventHandler.AnyEventNowOrWaiting'
 [IO.File]::WriteAllText($path,$legacy.Replace($gate,$exclude),[Text.UTF8Encoding]::new($false))
}
$managed=Join-Path $GamePath 'RimWorldWin64_Data/Managed'
$refs=@(foreach($name in @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','netstandard.dll')) {'/reference:'+(Join-Path $managed $name)})
foreach($module in @('Core','Colony')) {$refs+=('/reference:'+(Join-Path $project ('Mods/Inheritance.'+$module+'/Assemblies/Inheritance.'+$module+'.dll')))}
$files=@(Get-ChildItem -LiteralPath (Join-Path $harness 'Source') -Filter '*.cs' -File | ForEach-Object FullName)
& 'C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe' /nologo /target:library /langversion:5 /optimize+ ('/out:'+(Join-Path $harness 'Assemblies/Inheritance.ColonySmoke.dll')) $refs $files
if($LASTEXITCODE -ne 0) {throw 'Supply validation test-only compilation failed'}
$flags='-inh-supply-transfer -'+$prefix+$Step
if($Group -eq 'Physical') {$flags+=' -inh-physical-probe -inh-delegated-custody'}
if($Group -eq 'Legacy') {$flags+=' -inh-supply-transfer-producer16'}
$runner=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'RunSmoke.ps1'))
$replacements=@(
 @('$projectRoot = Split-Path $PSScriptRoot -Parent',('$projectRoot = '+"'"+$project.Replace("'","''")+"'")),
 @("    'SupplyTransferArm' { '-inh-supply-transfer -inh-supply-transfer-arm' }",("    'SupplyTransferArm' { '"+$flags+"' }")),
 @('(Join-Path $projectRoot ''Tests\ColonySmoke'')',("'"+$harness.Replace("'","''")+"'"))
)
foreach($replacement in $replacements) {
 if(([regex]::Matches($runner,[regex]::Escape($replacement[0]))).Count -ne 1) {throw 'Runner anchor changed'}
 $runner=$runner.Replace($replacement[0],$replacement[1])
}
$generated=Join-Path $resolvedWork ($config+'/Runner.generated.ps1')
[IO.File]::WriteAllText($generated,$runner,[Text.UTF8Encoding]::new($false))
$options=@{Mode='SupplyTransferArm';WithColony=$true;WorkPath=$resolvedWork;GamePath=$GamePath}
if($WithDlc) {$options.WithDlc=$true}
& $generated @options
$logFile=Join-Path $resolvedWork ($config+'/SupplyTransferArm-Player.log')
$raw=[IO.File]::ReadAllText($logFile)
$tag=if($Group -eq 'Abstract') {'INH-ABSTRACT-SUPPLY'} elseif($Group -eq 'Physical') {'INH-PHYSICAL-SUPPLY'} elseif($Group -eq 'ReservationScope') {'INH-RESERVATION-SCOPE'} else {'INH-PENDING-SUPPLY'}
if(!$raw.Contains('['+$tag+'] PASS') -or $raw.Contains('['+$tag+'] FAIL') -or !$raw.Contains('[Inheritance] Core 0.4.16 loaded;')) {throw 'Missing explicit final16 supply-group success'}
if($inputFile -and (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -ne $inputHash) {throw 'Original input save changed'}
if($checkpointSource -and ((Get-FileHash -LiteralPath $checkpointSource -Algorithm SHA256).Hash -ne $checkpointSourceHash -or (Get-FileHash -LiteralPath $checkpointFile -Algorithm SHA256).Hash -ne $checkpointHash)) {throw 'Original checkpoint source or clone changed'}
Copy-Item -LiteralPath $logFile -Destination (Join-Path $resolvedWork ($config+'/'+$Group+'-'+$Step+'-Player.log')) -Force
Write-Output ('Final16 '+$Group+' '+$config+' '+$Step+' PASS; input originals preserved')
