param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$WorkPath = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'work\inheritance-smoke'),
    [switch]$WithDlc,
    [switch]$WithWorld,
    [switch]$WithColony,
    [ValidateSet('SupplyTransferStaleArmed','SupplyTransferStaleArmedLoad','SupplyTransferCheckpoint','SupplyTransferCheckpointReload','SupplyTransferFault','SupplyTransferArm','SupplyTransferLoad','SupplyTransferFinalLoad','SupplyTransferStale','SupplyTransferAnimal','SupplyIntentStalePending','SupplyIntentStalePendingLoad','SupplyIntentPrepare','SupplyIntentLoad','SupplyIntentCancelledLoad','SupplyIntentStale','SupplyIntentStaleLoad','SupplyIntentCityLock','SupplyIntentMissingJournal','SupplyIntentLegacyMixed','SupplyIntentUnknownField','SupplyIntentCountChanged','SupplyPreview','SupplyPreviewCityLock','SupplyPreviewRenderEnglish','SupplyPreviewRenderJapanese','SupplyPreviewCityRenderEnglish','SupplyPreviewCityRenderJapanese','DelegatedUiCitySetupPrepared','DelegatedUiCitySetupApplied','DelegatedUiCityCleanPrepared','DelegatedUiCityCleanApplying','DelegatedUiCityPendingPreparedProofPrepared','DelegatedUiCityPendingPreparedProofApplying','DelegatedUiCityPendingAppliedProofPrepared','DelegatedUiCityPendingAppliedProofApplying','DelegatedUiCityReload','DelegatedUiCityRenderEnglish','DelegatedUiCityRenderJapanese','DelegatedUiStart','DelegatedUiStore','DelegatedUiPrepareRelease','DelegatedUiRelease','DelegatedUiReload','DelegatedUiRegister','DelegatedUiRegisteredReload','DelegatedUiRenderEnglish','DelegatedUiRenderJapanese','MaintenanceCitySetup','MaintenanceCityLockCleanPrepared','MaintenanceCityLockCleanApplying','MaintenanceCityLockOtherPrepared','MaintenanceCityLockOtherApplying','MaintenanceFreshStart','MaintenancePendingStopPrepared','MaintenancePendingStopApplied','MaintenancePendingRecoverPrepared','MaintenancePendingRecoverApplied','MaintenanceReentry','MaintenanceStart','MaintenanceDay','MaintenanceShortage','MaintenanceResume','MaintenanceStop','MaintenanceReload','MaintenanceGap','MaintenanceGapResume','MaintenanceRenderEnglish','MaintenanceRenderJapanese','MaintenanceFault','RationPrepare','RationApply','RationCommit','RationRelease','RationReload','RationReceiptLoad','RationCheckpoint','RationFault','DelegatedRouteFault','DelegatedRouteCheckpoint','DelegatedRouteUncertifiedSave','DelegatedRouteReserve','DelegatedRouteTransit','DelegatedRouteArrival','DelegatedRouteStored','DelegatedRouteReturn','DelegatedRouteEntering','DelegatedRouteFinal','DelegatedRouteReloadFinal','SchemaFivePayloadStamp','SchemaFivePayloadValid','DelegatedCustodyFault','DelegatedCustodyPrepare','DelegatedCustodyApply','DelegatedCustodyCommit','DelegatedCustodyReleasePrepare','DelegatedCustodyReleaseApply','DelegatedCustodyRelease','DelegatedCustodyFinal','DelegatedCustodyReloadFinal','Header','WorldSave','WorldLoad','CompletedLoad','LegacyLoad','LegacyReload','FutureLoad','RejectLoad','ReconcileLoad','SchemaTwoLoad','PrototypeSave','PrototypeLoad','PrototypeCompletedLoad','PlanResume','VanillaAttach','WorldFaultLoad','RequiredWorldLoad','ColonySave','ColonyLoad','ColonyCompletedLoad','ColonyFinalLoad','ColonyResume','ColonyFaultLoad','RequiredColonyLoad','PhysicalSave','PhysicalLoad','PhysicalFinalLoad','PhysicalReservedLoad','PhysicalDepartureIntent','PhysicalArrivalIntent','PhysicalFaultLoad','PhysicalDepartedCheckpoint','PhysicalDeliveredCheckpoint','PhysicalAutoLoad','UiSave','UiLoad','UiFinalLoad','UiRenderEnglish','UiRenderJapanese','UiLossLoad','UiLossFinalLoad','LossAccept','LossLoad','LossFinal','LossCheckpoint','LossDelivered','LossZero','LossZeroFinal','LossRenderEnglish','LossRenderJapanese','AnimalSave','AnimalReserved','AnimalLoad','AnimalFinal','AnimalRenderEnglish','AnimalRenderJapanese','AnimalDepartureCheckpoint','AnimalArrivalCheckpoint','JourneySave','JourneyLoad','JourneyFinal','JourneyDepletedSave','JourneyDepletedLoad','CustodySave','CustodyLoad','CustodyFinal','CoreCustodyPrepare','CoreCustodyApply','CoreCustodyCommit','CoreCustodyReleasePrepare','CoreCustodyRelease','CoreCustodyFinal','CoreCustodyReload','CoreCustodyFault','SchemaFourPayloadStamp','SchemaFourPayloadValid','LocalCustodySave','LocalCustodyLoad','LocalCustodyFinal','LocalCustodyRenderEnglish','LocalCustodyRenderJapanese','LocalCustodyPendingSave','LocalCustodyPendingLoad','LocalCustodyPendingFinal')][string]$Mode = 'Header',
    [ValidateSet('HeaderMissing','LedgerMissing','NextBackwards','ModuleFuture','QuantityMissing','StatusMissing','PreconditionsMissing','ForeignScope','OwnerMissing','TablesMissing','DuplicateId','MissingModule')][string]$Fault = 'HeaderMissing',
    [string]$LegacyFixturePath,
    [ValidateSet('MissingComponent','MissingModuleStamp','PlanChanged','HistoryMissing','IncomeDuplicate','WrongIncomeController')][string]$WorldFault = 'MissingComponent',
    [ValidateSet('MissingPayload','DuplicateTransport','AmountChanged','DestinationChanged','CountChanged','DueChanged')][string]$ColonyFault = 'MissingPayload',
    [ValidateSet('AmountChanged','DestinationChanged','PersonBinding','MissingPhysical','ReceiptMissing','CargoIdentity','ForgedArrival','SupplyPurpose','LossAmount','LossMissing','LossParent','AnimalBinding','AnimalManifest','AnimalMissing')][string]$PhysicalFault = 'AmountChanged',
    [ValidateSet('MissingVault','CountChanged','JournalMissing','StageChanged','ResidentMissing')][string]$CoreCustodyFault = 'MissingVault',
    [ValidateSet('CaravanChanged','TileChanged','EndpointChanged','ResidentMissing','SiteChanged')][string]$DelegatedCustodyFault = 'CaravanChanged',
    [ValidateSet('DestinationTile','ReturnCause','ReturnStage','ReturnJournal','ReturnCount')][string]$DelegatedRouteFault = 'DestinationTile',
    [ValidateSet('ReturnMissing','ArrivedMissing','UncertifiedHome')][string]$DelegatedRouteCheckpoint = 'ReturnMissing',
    [ValidateSet('DayChanged','BeforeCount','SpentMissing','StageChanged','JournalMissing','ProofChanged','ProofIdentity','MissingComponent')][string]$RationFault='DayChanged',
    [ValidateSet('Prepared','Applying')][string]$RationCheckpoint='Prepared',
    [ValidateSet('StartDay','ControlKind','ControlCause','ControlParent','ControlMissing','PlansMissing','ControlCount','ControlReceipt')][string]$MaintenanceFault='StartDay',
    [ValidateSet('MissingJournal','LegacyMixed','UnknownField','CountChanged','Orphan','ManifestChanged','Duplicate','FutureStage')][string]$SupplyTransferFault='MissingJournal',
    [ValidateSet('ArmPrepared','ArmApplying','DisarmPrepared','DisarmApplying','ArmReconcile')][string]$SupplyTransferCheckpoint='ArmPrepared',
    [switch]$SkipRuntimeCopy
)
$ErrorActionPreference = 'Stop'
if ($Mode.StartsWith('Maintenance') -and -not $WithColony) { throw 'Maintenance tests require -WithColony' }
if ($Mode.StartsWith('Ration') -and -not $WithColony) { throw 'Ration tests require -WithColony' }
if ($Mode.StartsWith('LocalCustody') -and -not $WithColony) { throw 'Local custody tests require -WithColony' }
if ($Mode.StartsWith('DelegatedRoute') -and -not $WithColony) { throw 'Delegated route tests require -WithColony' }
if ($Mode.StartsWith('DelegatedCustody') -and -not $WithColony) { throw 'Delegated custody tests require -WithColony' }
if ($Mode -in @('SchemaFourPayloadStamp','SchemaFourPayloadValid','SchemaFivePayloadStamp','SchemaFivePayloadValid') -and -not $WithColony) { throw 'Historical payload fixtures require -WithColony so refusal tests saved metadata, not an absent module' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$runName = if ($WithDlc) { 'four-dlc' } else { 'base-only' }
$runRoot = Join-Path $WorkPath $runName
$runtimePath = Join-Path $runRoot 'Runtime'
$savePath = Join-Path $runRoot 'SaveData'
$configPath = Join-Path $savePath 'Config'
New-Item -ItemType Directory -Path $runtimePath,$configPath,(Join-Path $runtimePath 'Mods') -Force | Out-Null
foreach ($fileName in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt')) {
    $gameFile=Join-Path $GamePath $fileName
    $runtimeFile=Join-Path $runtimePath $fileName
    if (Test-Path -LiteralPath $runtimeFile) {
        if ((Get-FileHash -LiteralPath $gameFile -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $runtimeFile -Algorithm SHA256).Hash) { continue }
        if ($SkipRuntimeCopy) { continue }
    }
    Copy-Item -LiteralPath $gameFile -Destination $runtimeFile -Force
}
function Copy-TestDirectory([string]$destinationPath, [string]$sourcePath) {
    if (Test-Path -LiteralPath $destinationPath) {
        if ((Get-Item -LiteralPath $destinationPath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to write through a filesystem link: $destinationPath"
        }
    }
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    Get-ChildItem -LiteralPath $sourcePath -Force | Copy-Item -Destination $destinationPath -Recurse -Force
}
foreach ($directoryName in @('Data','RimWorldWin64_Data','MonoBleedingEdge')) {
    if ($SkipRuntimeCopy -and (Test-Path -LiteralPath (Join-Path $runtimePath $directoryName))) { continue }
    Copy-TestDirectory (Join-Path $runtimePath $directoryName) (Join-Path $GamePath $directoryName)
}
Copy-TestDirectory (Join-Path $runtimePath 'Mods\Inheritance.Core') (Join-Path $projectRoot 'Mods\Inheritance.Core')
Copy-TestDirectory (Join-Path $runtimePath 'Mods\Inheritance.Smoke') (Join-Path $projectRoot 'Tests\SmokeMod')
if ($WithWorld) {
    Copy-TestDirectory (Join-Path $runtimePath 'Mods\Inheritance.World') (Join-Path $projectRoot 'Mods\Inheritance.World')
    Copy-TestDirectory (Join-Path $runtimePath 'Mods\Inheritance.WorldSmoke') (Join-Path $projectRoot 'Tests\WorldSmoke')
}
if ($WithColony) {
    Copy-TestDirectory (Join-Path $runtimePath 'Mods\Inheritance.Colony') (Join-Path $projectRoot 'Mods\Inheritance.Colony')
    Copy-TestDirectory (Join-Path $runtimePath 'Mods\Inheritance.ColonySmoke') (Join-Path $projectRoot 'Tests\ColonySmoke')
}
$activeMods = '<li>ludeon.rimworld</li>'
if ($WithDlc) { $activeMods += '<li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.odyssey</li>' }
$activeMods += '<li>inheritance.core</li><li>inheritance.smoketest</li>'
if ($WithWorld) { $activeMods += '<li>inheritance.world</li><li>inheritance.worldsmoketest</li>' }
if ($WithColony) { $activeMods += '<li>inheritance.colony</li><li>inheritance.colonysmoketest</li>' }
$configXml = '<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeMods + '</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $configPath 'ModsConfig.xml'), $configXml, (New-Object Text.UTF8Encoding($false)))
# Every mode resets its own isolated language, including Core-only runs after a Japanese render.
& {
    $renderPrefsPath=Join-Path $configPath 'Prefs.xml'
    $renderPrefs=New-Object Xml.XmlDocument
    if (Test-Path -LiteralPath $renderPrefsPath) { $renderPrefs.Load($renderPrefsPath) } else { $renderPrefs.LoadXml('<PrefsData />') }
    $renderLanguage=$renderPrefs.DocumentElement.SelectSingleNode('langFolderName')
    if ($null -eq $renderLanguage) { $renderLanguage=$renderPrefs.CreateElement('langFolderName'); $null=$renderPrefs.DocumentElement.AppendChild($renderLanguage) }
    $renderLanguage.InnerText=if ($Mode -in @('UiRenderJapanese','LossRenderJapanese','AnimalRenderJapanese','LocalCustodyRenderJapanese','MaintenanceRenderJapanese','DelegatedUiRenderJapanese','DelegatedUiCityRenderJapanese','SupplyPreviewRenderJapanese','SupplyPreviewCityRenderJapanese')) { 'Japanese (日本語)' } else { 'English' }
    if ($Mode.Contains('Render')) {
        # Match the previously passing isolated native-resolution renderer.
        # A 1280x720 switch while hidden reported 0x0 and produced no Repaint;
        # minimum-resolution behavior remains a separate unverified test.
        foreach ($preference in @(@{Name='screenWidth';Value='0'},@{Name='screenHeight';Value='0'},@{Name='fullscreen';Value='False'},@{Name='uiScale';Value='1'})) {
            $node=$renderPrefs.DocumentElement.SelectSingleNode($preference.Name)
            if ($null -eq $node) { $node=$renderPrefs.CreateElement($preference.Name); $null=$renderPrefs.DocumentElement.AppendChild($node) }
            $node.InnerText=$preference.Value
        }
    }
    $renderPrefs.Save($renderPrefsPath)
}
if ($Mode -eq 'LegacyLoad') {
    if (-not $LegacyFixturePath) { $LegacyFixturePath = Join-Path (Split-Path $WorkPath -Parent) 'p02-fixtures\P01-base-v1.rws' }
    $legacyXml = New-Object Xml.XmlDocument; $legacyXml.Load($LegacyFixturePath)
    $legacyScope = $legacyXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceHeader/saveScopeId").InnerText
    [IO.File]::WriteAllText((Join-Path $savePath 'expected-legacy-id.txt'),$legacyScope)
    Copy-Item -LiteralPath $LegacyFixturePath -Destination (Join-Path $savePath 'Saves\INH_LegacySmoke.rws') -Force
}
if ($Mode -in @('SchemaTwoLoad','CompletedLoad')) {
    if ($Mode -eq 'SchemaTwoLoad') {
        $historicalFixture = Join-Path (Split-Path $WorkPath -Parent) 'p02-fixtures\P02-base-v2.rws'
        Copy-Item -LiteralPath $historicalFixture -Destination (Join-Path $savePath 'Saves\INH_SchemaTwoSmoke.rws') -Force
    }
    else { $historicalFixture = Join-Path $savePath 'Saves\INH_CompletedSmoke.rws' }
    # Each case derives expectations from its own source save. Shared sidecars
    # may contain a different world's identity after a preceding migration case.
    $historical = New-Object Xml.XmlDocument; $historical.Load($historicalFixture)
    $historicalCore = $historical.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $historyLedger = $historicalCore.SelectSingleNode('inheritanceLedger')
    $expectations = @{
        'world-id' = $historicalCore.SelectSingleNode('inheritanceHeader/saveScopeId').InnerText
        'pawn' = $historyLedger.SelectSingleNode("entities/li[name='Resident']/binding").InnerText
        'departure' = $historyLedger.SelectSingleNode("operations/li[cause='departure']/id").InnerText
        'person-op' = $historyLedger.SelectSingleNode("operations/li[cause='person-move']/id").InnerText
        'arrival' = $historyLedger.SelectSingleNode("operations/li[cause='arrival']/id").InnerText
        'child' = $historyLedger.SelectSingleNode("operations/li[cause='departure']/result").InnerText
        'last-id' = (@($historyLedger.SelectNodes('entities/li/id')) | Sort-Object { [long]$_.InnerText.Split(':')[-1] } -Descending | Select-Object -First 1).InnerText
    }
    foreach ($expectationName in $expectations.Keys) { [IO.File]::WriteAllText((Join-Path $savePath ('expected-' + $expectationName + '.txt')),$expectations[$expectationName]) }
}
if ($Mode -eq 'VanillaAttach') {
    $attachXml = New-Object Xml.XmlDocument
    $attachXml.Load((Join-Path (Split-Path $WorkPath -Parent) 'p02-fixtures\P02-base-v2.rws'))
    $attachNode = $attachXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $null = $attachNode.ParentNode.RemoveChild($attachNode)
    $attachXml.Save((Join-Path $savePath 'Saves\INH_VanillaAttachSmoke.rws'))
}
if ($Mode -eq 'PlanResume') {
    $planXml = New-Object Xml.XmlDocument
    $planXml.Load((Join-Path $savePath 'Saves\INH_PlanReadySmoke.rws'))
    $payloadNode = $planXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceWorldPayload")
    $payloadXml = New-Object Xml.XmlDocument; $payloadXml.LoadXml($payloadNode.InnerText)
    $worldNode = $payloadXml.DocumentElement
    $ledgerNode = $planXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceLedger")
    $cityNode = $worldNode.SelectSingleNode('cities/li[last()]')
    $opId = $cityNode.SelectSingleNode('createOperationId').InnerText
    $resultId = $cityNode.SelectSingleNode('entityId').InnerText
    $opNode = $ledgerNode.SelectSingleNode("operations/li[id='$opId']")
    $opNode.SelectSingleNode('stage').InnerText = 'Prepared'
    foreach ($stamp in $opNode.SelectNodes('before/li')) {
        $stampId = $stamp.SelectSingleNode('id').InnerText
        $entityNode = $ledgerNode.SelectSingleNode("entities/li[id='$stampId']")
        $entityNode.SelectSingleNode('revision').InnerText = $stamp.SelectSingleNode('revision').InnerText
    }
    $resultNode = $ledgerNode.SelectSingleNode("entities/li[id='$resultId']")
    foreach ($childNode in @($resultNode.ChildNodes)) { if ($childNode.Name -notin @('id','kind','provider','quantity','revision','status','authority')) { $null = $resultNode.RemoveChild($childNode) } }
    $resultNode.SelectSingleNode('status').InnerText = 'Allocated'
    $resultNode.SelectSingleNode('quantity').InnerText = '0'
    $null = $cityNode.RemoveChild($cityNode.SelectSingleNode('entityId'))
    $capitalNode = $worldNode.SelectSingleNode('nations/li[last()]/capitalCityId'); $null = $capitalNode.ParentNode.RemoveChild($capitalNode)
    $worldNode.SelectSingleNode('stage').InnerText = 'Generating'
    $payloadNode.InnerText = $payloadXml.OuterXml
    $planXml.Save((Join-Path $savePath 'Saves\INH_PlanCheckpointSmoke.rws'))
}
if ($Mode -in @('ColonyResume','ColonyFaultLoad','RequiredColonyLoad')) {
    $colonyXml = New-Object Xml.XmlDocument; $colonyXml.Load((Join-Path $savePath 'Saves\INH_ColonyReserved.rws'))
    $colonyCore = $colonyXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $moduleNode = $colonyCore.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']")
    $inner = New-Object Xml.XmlDocument; $inner.LoadXml($moduleNode.SelectSingleNode('data').InnerText)
    if ($Mode -eq 'ColonyResume') { $inner.SelectSingleNode('//transports/li[cause="A-B"]/stage').InnerText = 'Departing' }
    elseif ($Mode -eq 'ColonyFaultLoad') {
        switch ($ColonyFault) {
            'MissingPayload' { $null = $moduleNode.ParentNode.RemoveChild($moduleNode); $colonyCore.SelectSingleNode('inheritanceModulePayloadCount').InnerText = '0' }
            'DuplicateTransport' { $list = $inner.SelectSingleNode('//transports'); $null = $list.AppendChild($list.FirstChild.CloneNode($true)); $inner.SelectSingleNode('//transportCount').InnerText = $list.ChildNodes.Count.ToString() }
            'AmountChanged' { $inner.SelectSingleNode('//transports/li/amount').InnerText = '31' }
            'DestinationChanged' { $inner.SelectSingleNode('//transports/li/destinationId').InnerText = $inner.SelectSingleNode('//bases/li[last()]/cityId').InnerText }
            'CountChanged' { $inner.SelectSingleNode('//transportCount').InnerText = '99' }
            'DueChanged' { $inner.SelectSingleNode('//transports/li/dueTick').InnerText = '1' }
        }
    }
    else {
        if ($colonyCore.SelectSingleNode('inheritanceHeader/worldMode').InnerText -eq 'six-nations' -and -not $WithWorld) { throw 'Keep World enabled when testing missing Colony against a six-nation fixture' }
        foreach ($probeNode in @($colonyXml.SelectNodes("//li[starts-with(@Class,'Inheritance.ColonySmoke.')]"))) { $null = $probeNode.ParentNode.RemoveChild($probeNode) }
    }
    if ($Mode -ne 'ColonyFaultLoad' -or $ColonyFault -ne 'MissingPayload') { $moduleNode.SelectSingleNode('data').InnerText = $inner.OuterXml }
    $colonyTarget = if ($Mode -eq 'ColonyResume') { 'INH_ColonyCheckpoint' } else { 'INH_RejectSmoke' }
    $futureSave = Join-Path $savePath ('Saves\' + $colonyTarget + '.rws'); $colonyXml.Save($futureSave); $futureHash = (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -in @('PhysicalDepartedCheckpoint','PhysicalDeliveredCheckpoint')) {
    $checkpointSource = if ($Mode -eq 'PhysicalDepartedCheckpoint') { 'INH_PhysicalTransit' } else { 'INH_PhysicalDelivered' }
    $checkpointXml = New-Object Xml.XmlDocument; $checkpointXml.Load((Join-Path $savePath "Saves/$checkpointSource.rws"))
    $checkpointCore = $checkpointXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $checkpointPayload = $checkpointCore.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']/data")
    $checkpointData = New-Object Xml.XmlDocument; $checkpointData.LoadXml($checkpointPayload.InnerText)
    $checkpointTrip = $checkpointData.SelectSingleNode('//physical/li[cause="physical-trip"]')
    $checkpointPhase = if ($Mode -eq 'PhysicalDepartedCheckpoint') { 'Departing' } else { 'Arriving' }
    $checkpointReceipt = if ($Mode -eq 'PhysicalDepartedCheckpoint') { 'departureReceipt' } else { 'arrivalReceipt' }
    $checkpointTrip.SelectSingleNode('stage').InnerText = $checkpointPhase
    $null = $checkpointTrip.RemoveChild($checkpointTrip.SelectSingleNode($checkpointReceipt))
    $checkpointPayload.InnerText = $checkpointData.OuterXml
    [IO.File]::WriteAllText((Join-Path $savePath 'p04-physical-checkpoint-count.txt'),$checkpointCore.SelectSingleNode('inheritanceLedger/entityCount').InnerText)
    $checkpointXml.Save((Join-Path $savePath 'Saves/INH_PhysicalCheckpoint.rws'))
}
if ($Mode -eq 'PhysicalFaultLoad') {
    $physicalSource = if ($PhysicalFault -in @('AnimalBinding','AnimalManifest','AnimalMissing')) { 'INH_AnimalTransit' } elseif ($PhysicalFault -in @('LossAmount','LossMissing','LossParent')) { 'INH_LossAccepted' } elseif ($PhysicalFault -eq 'SupplyPurpose') { 'INH_UiTransit' } elseif ($PhysicalFault -in @('ReceiptMissing','CargoIdentity')) { 'INH_PhysicalTransit' } else { 'INH_PhysicalReserved' }
    $physicalXml = New-Object Xml.XmlDocument; $physicalXml.Load((Join-Path $savePath "Saves/$physicalSource.rws"))
    $physicalCore = $physicalXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $physicalPayload = $physicalCore.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']/data")
    $physicalData = New-Object Xml.XmlDocument; $physicalData.LoadXml($physicalPayload.InnerText)
    $physicalTrip = if ($PhysicalFault -in @('AnimalBinding','AnimalManifest','AnimalMissing')) { $physicalData.SelectSingleNode('//physical/li[animalCount="1" and stage="InTransit"]') } elseif ($PhysicalFault -in @('LossAmount','LossMissing','LossParent')) { $physicalData.SelectSingleNode('//physical/li[lossCount="2"]') } elseif ($PhysicalFault -eq 'SupplyPurpose') { $physicalData.SelectSingleNode('//physical/li[stage="InTransit"]') } else { $physicalData.SelectSingleNode('//physical/li[cause="physical-trip"]') }
    switch ($PhysicalFault) {
        'AnimalBinding' { $physicalData.SelectSingleNode('//ColonyState/animals/li/nativeId').InnerText='Thing_ForgedAnimal' }
        'AnimalManifest' { $physicalTrip.SelectSingleNode('animals').RemoveAll(); $physicalTrip.SelectSingleNode('animalCount').InnerText='0' }
        'AnimalMissing' { $physicalData.SelectSingleNode('//ColonyState/animals').RemoveAll(); $physicalData.SelectSingleNode('//ColonyState/animalCount').InnerText='0' }
        'AmountChanged' { $physicalTrip.SelectSingleNode('cargo/li/count').InnerText = '31' }
        'DestinationChanged' { $physicalTrip.SelectSingleNode('destinationId').InnerText = $physicalTrip.SelectSingleNode('sourceId').InnerText }
        'PersonBinding' { $physicalData.SelectSingleNode('//people/li/nativeId').InnerText = 'Thing_ForgedPawn' }
        'MissingPhysical' { $physicalData.SelectSingleNode('//physical').RemoveAll(); $physicalData.SelectSingleNode('//physicalCount').InnerText = '0' }
        'ReceiptMissing' { $null = $physicalTrip.RemoveChild($physicalTrip.SelectSingleNode('departureReceipt')) }
        'CargoIdentity' { $physicalTrip.SelectSingleNode('cargo/li/cargoNativeId').InnerText = 'Thing_ForgedCargo' }
        'ForgedArrival' { $physicalTrip.SelectSingleNode('stage').InnerText = 'Arrived' }
        'SupplyPurpose' { $physicalTrip.SelectSingleNode('cargo/li[journeySupply="true"]/journeySupply').InnerText = 'false' }
        'LossAmount' {
            $lossCargoIndex=1
            foreach ($lossCargo in $physicalTrip.SelectNodes('cargo/li')) { if ($lossCargo.SelectSingleNode('journeySupply').InnerText -eq 'false') { break }; $lossCargoIndex++ }
            $physicalTrip.SelectSingleNode("losses/li[2]/remaining/li[$lossCargoIndex]").InnerText = '26'
        }
        'LossMissing' { $physicalTrip.SelectSingleNode('losses').RemoveAll(); $physicalTrip.SelectSingleNode('lossCount').InnerText='0' }
        'LossParent' { $physicalTrip.SelectSingleNode('losses/li[2]/previousReceipt').InnerText='forged-loss-parent' }
    }
    $physicalPayload.InnerText = $physicalData.OuterXml
    $futureSave = Join-Path $savePath 'Saves/INH_RejectSmoke.rws'; $physicalXml.Save($futureSave)
    $futureHash = (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -in @('AnimalDepartureCheckpoint','AnimalArrivalCheckpoint')) {
    $animalCheckpointSource=if ($Mode -eq 'AnimalDepartureCheckpoint') { 'INH_AnimalTransit' } else { 'INH_AnimalDelivered' }
    $animalCheckpointXml=New-Object Xml.XmlDocument; $animalCheckpointXml.Load((Join-Path $savePath "Saves/$animalCheckpointSource.rws"))
    $animalCheckpointCore=$animalCheckpointXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $animalCheckpointPayload=$animalCheckpointCore.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']/data")
    $animalCheckpointData=New-Object Xml.XmlDocument; $animalCheckpointData.LoadXml($animalCheckpointPayload.InnerText)
    $animalCheckpointTrip=$animalCheckpointData.SelectSingleNode('//physical/li[animalCount="1" and (stage="InTransit" or stage="Arrived")]')
    $animalCheckpointPhase=if ($Mode -eq 'AnimalDepartureCheckpoint') { 'Departing' } else { 'Arriving' }
    $animalCheckpointReceipt=if ($Mode -eq 'AnimalDepartureCheckpoint') { 'departureReceipt' } else { 'arrivalReceipt' }
    $animalCheckpointTrip.SelectSingleNode('stage').InnerText=$animalCheckpointPhase
    $null=$animalCheckpointTrip.RemoveChild($animalCheckpointTrip.SelectSingleNode($animalCheckpointReceipt))
    $animalCheckpointPayload.InnerText=$animalCheckpointData.OuterXml
    [IO.File]::WriteAllText((Join-Path $savePath 'p04-animal-checkpoint-count.txt'),$animalCheckpointCore.SelectSingleNode('inheritanceLedger/entityCount').InnerText)
    $animalCheckpointXml.Save((Join-Path $savePath 'Saves/INH_AnimalCheckpoint.rws'))
}
if ($Mode -eq 'LossCheckpoint') {
    $checkpointXml=New-Object Xml.XmlDocument; $checkpointXml.Load((Join-Path $savePath 'Saves/INH_LossAccepted.rws'))
    $checkpointPayload=$checkpointXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceModulePayloads/li[owner='inheritance.colony']/data")
    $checkpointData=New-Object Xml.XmlDocument; $checkpointData.LoadXml($checkpointPayload.InnerText)
    $checkpointTrip=$checkpointData.SelectSingleNode('//physical/li[lossCount="2"]')
    $null=$checkpointTrip.SelectSingleNode('losses/li[2]').RemoveChild($checkpointTrip.SelectSingleNode('losses/li[2]/receipt'))
    $checkpointTrip.SelectSingleNode('stage').InnerText='NeedsAttention'
    $checkpointPayload.InnerText=$checkpointData.OuterXml
    $checkpointXml.Save((Join-Path $savePath 'Saves/INH_LossCheckpoint.rws'))
}
if ($Mode -in @('WorldFaultLoad','RequiredWorldLoad')) {
    $worldFaultXml = New-Object Xml.XmlDocument
    $sourceWorldSave = if ($Mode -eq 'WorldFaultLoad' -and $WorldFault -in @('HistoryMissing','IncomeDuplicate','WrongIncomeController')) { 'INH_PrototypeCompletedSmoke' } else { 'INH_PlanReadySmoke' }
    $worldFaultXml.Load((Join-Path $savePath ('Saves\' + $sourceWorldSave + '.rws')))
    $coreNode = $worldFaultXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $payloadNode = $coreNode.SelectSingleNode('inheritanceWorldPayload')
    $payloadXml = New-Object Xml.XmlDocument; $payloadXml.LoadXml($payloadNode.InnerText)
    $worldNode = $payloadXml.DocumentElement
    if ($Mode -eq 'WorldFaultLoad') {
        switch ($WorldFault) {
            'MissingComponent' { $null = $payloadNode.ParentNode.RemoveChild($payloadNode) }
            'MissingModuleStamp' { $node = $coreNode.SelectSingleNode("inheritanceHeader/moduleStamps/li[packageId='inheritance.world']"); $null = $node.ParentNode.RemoveChild($node) }
            'PlanChanged' { $worldNode.SelectSingleNode('cities/li/tile').InnerText = '0' }
            'HistoryMissing' { $node = $coreNode.SelectSingleNode('inheritanceLedger/entities/li[controllerHistory/li]/controllerHistory'); $null = $node.ParentNode.RemoveChild($node) }
            'IncomeDuplicate' { $node = $worldNode.SelectSingleNode('income'); $null = $node.AppendChild($node.FirstChild.CloneNode($true)); $worldNode.SelectSingleNode('incomeCount').InnerText = $node.ChildNodes.Count.ToString() }
            'WrongIncomeController' { $worldNode.SelectSingleNode('income/li/controllerId').InnerText = $worldNode.SelectSingleNode('nations/li[last()]/entityId').InnerText }
        }
        if ($WorldFault -ne 'MissingComponent') { $payloadNode.InnerText = $payloadXml.OuterXml }
    }
    else {
        # This development-only component is not included in either distributed mod.
        foreach ($testNode in @($worldFaultXml.SelectNodes("//li[@Class='Inheritance.WorldSmoke.SmokeComponent']"))) { $null = $testNode.ParentNode.RemoveChild($testNode) }
    }
    $futureSave = Join-Path $savePath 'Saves\INH_RejectSmoke.rws'
    $worldFaultXml.Save($futureSave); $futureHash = (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -eq 'CoreCustodyFault') {
    # A missing empty vault tests the root node without deleting deep-saved originals.
    $custodyFaultSource = if ($CoreCustodyFault -eq 'MissingVault') { 'INH_CoreCustodyPrepared' } else { 'INH_CoreCustodyStored' }
    $custodyFaultXml = New-Object Xml.XmlDocument
    $custodyFaultXml.Load((Join-Path $savePath "Saves/$custodyFaultSource.rws"))
    $custodyFaultCore = $custodyFaultXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $declaredCore=[xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Mods/Inheritance.Core/Defs/ModuleDefs.xml') -Raw)
    $fixtureSchema=[int]$custodyFaultCore.SelectSingleNode('inheritanceHeader/schemaVersion').InnerText
    if ($fixtureSchema -lt 5 -or $fixtureSchema -gt [int]$declaredCore.Defs.'Inheritance.Core.ModuleDef'.moduleSchemaVersion) { throw 'Core custody faults require a supported custody schema' }
    $custodyVault = $custodyFaultCore.SelectSingleNode('inheritancePawnCustody')
    if ($null -eq $custodyVault) { throw 'Fixture has no Core custody root to mutate' }
    switch ($CoreCustodyFault) {
        'MissingVault' { $null = $custodyFaultCore.RemoveChild($custodyVault) }
        'CountChanged' { $custodyVault.SelectSingleNode('residentCount').InnerText = '99' }
        'JournalMissing' {
            $custodyJournal = $custodyVault.SelectSingleNode('transfers')
            $custodyEntry = $custodyJournal.SelectSingleNode('li')
            if ($null -eq $custodyEntry) { throw 'Stored fixture has no custody journal record' }
            $null = $custodyJournal.RemoveChild($custodyEntry)
            $custodyVault.SelectSingleNode('transferCount').InnerText = $custodyJournal.SelectNodes('li').Count.ToString()
        }
        'StageChanged' { $custodyVault.SelectSingleNode('transfers/li/stage').InnerText = 'Prepared' }
        'ResidentMissing' {
            $custodyResidents = $custodyVault.SelectSingleNode('residents')
            $custodyResident = $custodyResidents.SelectSingleNode('li')
            if ($null -eq $custodyResident) { throw 'Stored fixture has no resident record' }
            $null = $custodyResidents.RemoveChild($custodyResident)
            $custodyVault.SelectSingleNode('residentCount').InnerText = $custodyResidents.SelectNodes('li').Count.ToString()
        }
    }
    $futureSave = Join-Path $savePath 'Saves/INH_RejectSmoke.rws'
    $custodyFaultXml.Save($futureSave)
    $futureHash = (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -in @('DelegatedRouteFault','DelegatedRouteCheckpoint')) {
    $routeXml=New-Object Xml.XmlDocument
    $routeInput=if ($Mode -eq 'DelegatedRouteFault' -or $DelegatedRouteCheckpoint -eq 'ReturnMissing') { 'INH_DelegatedRouteReturn' } elseif ($DelegatedRouteCheckpoint -eq 'ArrivedMissing') { 'INH_DelegatedRouteHome' } else { 'INH_DelegatedRouteUncertifiedHome' }
    $routeXml.Load((Join-Path $savePath ('Saves/'+$routeInput+'.rws')))
    $routeCore=$routeXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $routePayload=$routeCore.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']/data")
    $routeState=New-Object Xml.XmlDocument; $routeState.LoadXml($routePayload.InnerText)
    $routeReturn=$routeState.SelectSingleNode('/ColonyState/returns/li')
    $routeOutward=$routeState.SelectSingleNode("/ColonyState/physical/li[cause='delegated-route-outward']")
    if ($null -eq $routeReturn -or $null -eq $routeOutward) { throw 'Missing integrated delegated route fixture' }
    if ($Mode -eq 'DelegatedRouteFault') {
        switch ($DelegatedRouteFault) {
            'DestinationTile' { $routeOutward.SelectSingleNode('destinationTile').InnerText=([int]$routeOutward.SelectSingleNode('destinationTile').InnerText+1).ToString() }
            'ReturnCause' { $routeReturn.SelectSingleNode('cause').InnerText='forged-return-cause' }
            'ReturnStage' { $routeReturn.SelectSingleNode('stage').InnerText='Returned' }
            'ReturnJournal' { $null=$routeReturn.ParentNode.RemoveChild($routeReturn); $routeState.SelectSingleNode('/ColonyState/returnCount').InnerText='0' }
            'ReturnCount' { $routeState.SelectSingleNode('/ColonyState/returnCount').InnerText='2' }
        }
    } elseif ($DelegatedRouteCheckpoint -eq 'ReturnMissing') { $null=$routeReturn.RemoveChild($routeReturn.SelectSingleNode('departureReceipt')) }
    elseif ($DelegatedRouteCheckpoint -eq 'ArrivedMissing') { $null=$routeReturn.RemoveChild($routeReturn.SelectSingleNode('arrivalReceipt')) }
    $routePayload.InnerText=$routeState.OuterXml
    $futureSave=Join-Path $savePath ('Saves/'+$(if ($Mode -eq 'DelegatedRouteFault') {'INH_RejectSmoke'} else {'INH_DelegatedRouteCheckpoint'})+'.rws')
    $routeXml.Save($futureSave); $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -eq 'DelegatedCustodyFault') {
    $delegatedFaultXml=New-Object Xml.XmlDocument
    $delegatedFaultXml.Load((Join-Path $savePath 'Saves/INH_DelegatedCustodyStored.rws'))
    $delegatedFaultCore=$delegatedFaultXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $declaredCore=[xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Mods/Inheritance.Core/Defs/ModuleDefs.xml') -Raw)
    if ($delegatedFaultCore.SelectSingleNode('inheritanceHeader/schemaVersion').InnerText -ne $declaredCore.Defs.'Inheritance.Core.ModuleDef'.moduleSchemaVersion) { throw 'Delegated faults need a fixture at the declared current Core schema' }
    $delegatedVault=$delegatedFaultCore.SelectSingleNode('inheritancePawnCustody')
    $delegatedIntent=$delegatedVault.SelectSingleNode("transfers/li[string-length(caravanId)>0 and caravanId!='null']")
    if ($null -eq $delegatedIntent) { throw 'No delegated intent in fixture' }
    switch ($DelegatedCustodyFault) {
        'CaravanChanged' { $delegatedIntent.SelectSingleNode('caravanId').InnerText='Caravan_ForgedAnchor' }
        'TileChanged' { $delegatedIntent.SelectSingleNode('tile').InnerText=([int]$delegatedIntent.SelectSingleNode('tile').InnerText+1).ToString() }
        'EndpointChanged' { $delegatedIntent.SelectSingleNode('mapId').InnerText='0' }
        'ResidentMissing' {
            $delegatedResidents=$delegatedVault.SelectSingleNode('residents')
            $delegatedResident=$delegatedResidents.SelectSingleNode('li')
            $null=$delegatedResidents.RemoveChild($delegatedResident)
            $delegatedVault.SelectSingleNode('residentCount').InnerText=$delegatedResidents.SelectNodes('li').Count.ToString()
        }
        'SiteChanged' {
            $delegatedPayload=$delegatedFaultCore.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']/data")
            $delegatedState=New-Object Xml.XmlDocument; $delegatedState.LoadXml($delegatedPayload.InnerText)
            $delegatedSite=$delegatedState.SelectSingleNode('/ColonyState/bases/li[tile >= 0]')
            $delegatedSite.SelectSingleNode('tile').InnerText=([int]$delegatedSite.SelectSingleNode('tile').InnerText+1).ToString()
            $delegatedPayload.InnerText=$delegatedState.OuterXml
        }
    }
    $futureSave=Join-Path $savePath 'Saves/INH_RejectSmoke.rws'; $delegatedFaultXml.Save($futureSave)
    $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -in @('SchemaFourPayloadStamp','SchemaFourPayloadValid','SchemaFivePayloadStamp','SchemaFivePayloadValid')) {
    # Promote this historical Core-only schema-three/four fixture to schema four,
    # then add a valid empty Colony payload with no Colony-owned ledger assets.
    # This isolates payload-owner stamp checking from ledger-owner stamp checking.
    $payloadStampXml = New-Object Xml.XmlDocument
    $payloadSchema=if ($Mode.StartsWith('SchemaFive')) { '5' } else { '4' }
    $payloadFixture=if ($payloadSchema -eq '5') { Join-Path (Split-Path $WorkPath -Parent) "p04-delegated-fixtures/$runName-schema-five-empty.rws" } else { Join-Path $savePath 'Saves/INH_CompletedSmoke.rws' }
    $payloadStampXml.Load($payloadFixture)
    $payloadStampCore = $payloadStampXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $payloadStampHeader = $payloadStampCore.SelectSingleNode('inheritanceHeader')
    if ($payloadSchema -eq '5') {
        if ($payloadStampHeader.SelectSingleNode('schemaVersion').InnerText -ne '5' -or $payloadStampCore.SelectSingleNode('inheritancePawnCustody/transferCount').InnerText -ne '0') { throw 'Expected the preserved schema-five empty custody fixture' }
    }
    elseif ($payloadStampHeader.SelectSingleNode('schemaVersion').InnerText -notin @('2','3','4')) { throw 'Payload stamp test requires the historical schema-two/three/four CompletedSmoke fixture' }
    $payloadStampLedger = $payloadStampCore.SelectSingleNode('inheritanceLedger')
    if ($payloadStampLedger.SelectNodes("entities/li[provider!='inheritance.core'] | operations/li[actor!='inheritance.core']").Count -ne 0) { throw 'Payload stamp fixture must have Core-only ledger owners' }
    $payloadStampHeader.SelectSingleNode('schemaVersion').InnerText = $payloadSchema
    $payloadStampHeader.SelectSingleNode("moduleStamps/li[packageId='inheritance.core']/schemaVersion").InnerText = $payloadSchema
    foreach ($payloadOldStamp in @($payloadStampHeader.SelectNodes("moduleStamps/li[packageId='inheritance.colony']"))) { $null = $payloadOldStamp.ParentNode.RemoveChild($payloadOldStamp) }
    if ($payloadSchema -eq '4') { foreach ($payloadOldVault in @($payloadStampCore.SelectNodes('inheritancePawnCustody'))) { $null = $payloadOldVault.ParentNode.RemoveChild($payloadOldVault) } }
    $payloadScope = $payloadStampHeader.SelectSingleNode('saveScopeId').InnerText
    $emptyColonyPayload = '<ColonyState><schema>1</schema><scope>'+ $payloadScope +'</scope><baseCount>0</baseCount><stockCount>0</stockCount><transportCount>0</transportCount><personCount>0</personCount><animalCount>0</animalCount><physicalCount>0</physicalCount><bases/><stocks/><transports/><people/><animals/><physical/></ColonyState>'
    $payloadTable = $payloadStampCore.SelectSingleNode('inheritanceModulePayloads')
    if ($null -eq $payloadTable) { $payloadTable = $payloadStampXml.CreateElement('inheritanceModulePayloads'); $null = $payloadStampCore.AppendChild($payloadTable) }
    $payloadTable.RemoveAll()
    $payloadEntry = $payloadStampXml.CreateElement('li'); $null = $payloadTable.AppendChild($payloadEntry)
    foreach ($payloadField in @(@('owner','inheritance.colony'),@('version','1'),@('data',$emptyColonyPayload))) {
        $payloadFieldNode = $payloadStampXml.CreateElement($payloadField[0]); $payloadFieldNode.InnerText = $payloadField[1]; $null = $payloadEntry.AppendChild($payloadFieldNode)
    }
    $payloadCount = $payloadStampCore.SelectSingleNode('inheritanceModulePayloadCount')
    if ($null -eq $payloadCount) { $payloadCount = $payloadStampXml.CreateElement('inheritanceModulePayloadCount'); $null = $payloadStampCore.AppendChild($payloadCount) }
    $payloadCount.InnerText = '1'
    if ($Mode -in @('SchemaFourPayloadValid','SchemaFivePayloadValid')) {
        $payloadValidStamp = $payloadStampXml.CreateElement('li'); $null = $payloadStampHeader.SelectSingleNode('moduleStamps').AppendChild($payloadValidStamp)
        foreach ($payloadStampField in @(@('packageId','inheritance.colony'),@('schemaVersion','1'))) {
            $payloadStampFieldNode = $payloadStampXml.CreateElement($payloadStampField[0]); $payloadStampFieldNode.InnerText = $payloadStampField[1]; $null = $payloadValidStamp.AppendChild($payloadStampFieldNode)
        }
        $payloadExpected = @($payloadScope,$payloadStampHeader.SelectSingleNode('nextSequence').InnerText,$payloadStampLedger.SelectSingleNode('entityCount').InnerText,$payloadStampLedger.SelectSingleNode('operationCount').InnerText)
        $payloadExpected += @($payloadStampLedger.SelectNodes('entities/li') | Sort-Object { $_.SelectSingleNode('id').InnerText } -CaseSensitive | ForEach-Object {
            $payloadBinding = $_.SelectSingleNode('binding')
            $_.SelectSingleNode('id').InnerText + '|' + $(if ($null -eq $payloadBinding -or $payloadBinding.InnerText -eq 'null') { '' } else { $payloadBinding.InnerText })
        })
        [IO.File]::WriteAllLines((Join-Path $savePath 'schema-four-payload-expected.txt'),[string[]]$payloadExpected)
        $payloadStampXml.Save((Join-Path $savePath 'Saves/INH_SchemaFourPayloadSmoke.rws'))
    }
    else {
        $futureSave = Join-Path $savePath 'Saves/INH_RejectSmoke.rws'; $payloadStampXml.Save($futureSave)
        $futureHash = (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
    }
}
if ($Mode -in @('SupplyTransferFault','FutureLoad','RejectLoad','ReconcileLoad')) {
    $normalSave = Join-Path $savePath 'Saves\INH_WorldSmoke.rws'
    $targetName = if ($Mode -eq 'FutureLoad') { 'INH_FutureSmoke' } elseif ($Mode -eq 'ReconcileLoad') { 'INH_ReconcileSmoke' } else { 'INH_RejectSmoke' }
    $futureSave = Join-Path $savePath ('Saves\' + $targetName + '.rws')
    if (-not (Test-Path -LiteralPath $normalSave)) { throw 'Run WorldSave first' }
    $futureXml = New-Object Xml.XmlDocument
    $futureXml.Load($normalSave)
    $schemaNode = $futureXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceHeader/schemaVersion")
    if ($null -eq $schemaNode) { throw 'Missing explicit schema version in world save' }
    $coreNode = $schemaNode.ParentNode.ParentNode
    if ($Mode -eq 'FutureLoad') { $schemaNode.InnerText = '99' }
    elseif ($Mode -eq 'ReconcileLoad') { $coreNode.SelectSingleNode("inheritanceLedger/entities/li[name='Stock']/quantity").InnerText = '99' }
    else {
        switch ($Fault) {
            'HeaderMissing' { $null = $coreNode.RemoveChild($coreNode.SelectSingleNode('inheritanceHeader')) }
            'LedgerMissing' { $null = $coreNode.RemoveChild($coreNode.SelectSingleNode('inheritanceLedger')) }
            'NextBackwards' { $coreNode.SelectSingleNode('inheritanceHeader/nextSequence').InnerText = '1' }
            'ModuleFuture' { $coreNode.SelectSingleNode('inheritanceHeader/moduleStamps/li/schemaVersion').InnerText = '99' }
            'QuantityMissing' { $node = $coreNode.SelectSingleNode("inheritanceLedger/entities/li[name='Stock']/quantity"); $null = $node.ParentNode.RemoveChild($node) }
            'StatusMissing' { $node = $coreNode.SelectSingleNode("inheritanceLedger/entities/li[name='Stock']/status"); $null = $node.ParentNode.RemoveChild($node) }
            'PreconditionsMissing' { $node = $coreNode.SelectSingleNode("inheritanceLedger/operations/li[kind='depart']/before"); $null = $node.ParentNode.RemoveChild($node) }
            'ForeignScope' { $coreNode.SelectSingleNode('inheritanceHeader/saveScopeId').InnerText = '11111111111111111111111111111111' }
            'OwnerMissing' { $coreNode.SelectSingleNode("inheritanceLedger/entities/li[name='Stock']/owner").InnerText = 'inh:missing:polity:1' }
            'TablesMissing' { $node = $coreNode.SelectSingleNode('inheritanceLedger/entities'); $null = $node.ParentNode.RemoveChild($node) }
            'DuplicateId' { $node = $coreNode.SelectSingleNode('inheritanceLedger/entities'); $null = $node.AppendChild($node.FirstChild.CloneNode($true)); $coreNode.SelectSingleNode('inheritanceLedger/entityCount').InnerText = $node.ChildNodes.Count.ToString() }
            'MissingModule' { $coreNode.SelectSingleNode('inheritanceLedger/entities/li/provider').InnerText = 'inheritance.absent' }
        }
    }
    $futureXml.Save($futureSave)
    $futureHash = (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -eq 'RationFault') {
    if (-not $WithColony) { throw 'Ration tests require Colony' }
    $rationFixture=Join-Path $savePath 'Saves/INH_RationApplied.rws'
    $rationXml=New-Object Xml.XmlDocument; $rationXml.Load($rationFixture)
    $rationCore=$rationXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $rationState=$rationCore.SelectSingleNode('inheritanceRationDebits')
    if ($null -eq $rationState) { throw 'Ration journal fixture is absent' }
    switch ($RationFault) {
        'DayChanged' { $n=$rationState.SelectSingleNode('debits/li/day'); $n.InnerText=([int]$n.InnerText+1).ToString() }
        'BeforeCount' { $n=$rationState.SelectSingleNode('debits/li/lines/li/before'); $n.InnerText=([int]$n.InnerText+1).ToString() }
        'SpentMissing' { $n=$rationState.SelectSingleNode('spent/innerList/li'); $null=$n.ParentNode.RemoveChild($n) }
        'StageChanged' { $rationState.SelectSingleNode('debits/li/stage').InnerText='Prepared' }
        'JournalMissing' { $n=$rationState.SelectSingleNode('debits'); $n.RemoveAll(); $rationState.SelectSingleNode('debitCount').InnerText='0' }
        'ProofChanged' { $rationState.SelectSingleNode('debits/li/appliedReceipt').InnerText=$rationState.SelectSingleNode('debits/li[last()]/appliedReceipt').InnerText }
        'ProofIdentity' {
            $proofId=$rationState.SelectSingleNode('debits/li/appliedReceipt').InnerText
            $rationCore.SelectSingleNode("inheritanceLedger/entities/li[id='$proofId']/identity").InnerText='altered-ration-proof'
        }
        'MissingComponent' { $null=$rationCore.RemoveChild($rationState) }
    }
    $futureSave=Join-Path $savePath 'Saves/INH_RejectSmoke.rws'
    $rationXml.Save($futureSave)
    $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -in @('RationReceiptLoad','RationReload')) {
    # Both modes read the released-resident save; restore that save's exact observations.
    # The preceding test may have restored an Applied or Committed checkpoint instead.
    foreach ($key in @('ids','biology','inventories','receipts','spent','count')) {
        Copy-Item -LiteralPath (Join-Path $savePath ('INH_RationReleased-ration-'+$key+'.txt')) -Destination (Join-Path $savePath ('p04-ration-'+$key+'.txt')) -Force
    }
}
if ($Mode -eq 'RationReceiptLoad') {
    if (-not $WithColony) { throw 'Ration tests require Colony' }
    $rationXml=New-Object Xml.XmlDocument
    $rationXml.Load((Join-Path $savePath 'Saves/INH_RationReleased.rws'))
    $rationState=$rationXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceRationDebits")
    foreach ($n in @($rationState.SelectNodes('debits/li/appliedReceipt'))) { $null=$n.ParentNode.RemoveChild($n) }
    $futureSave=Join-Path $savePath 'Saves/INH_RationReceiptMissing.rws'
    $rationXml.Save($futureSave)
    $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode -eq 'RationCheckpoint') {
    if (-not $WithColony) { throw 'Ration tests require Colony' }
    $rationXml=New-Object Xml.XmlDocument
    $rationXml.Load((Join-Path $savePath 'Saves/INH_RationApplied.rws'))
    $rationCore=$rationXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $rationRecord=$rationCore.SelectSingleNode('inheritanceRationDebits/debits/li[last()]')
    $proofRef=$rationRecord.SelectSingleNode('appliedReceipt'); $proofId=$proofRef.InnerText
    $rationLedger=$rationCore.SelectSingleNode('inheritanceLedger')
    $pendingProof=$rationLedger.SelectSingleNode("operations/li[result='$proofId']")
    $pendingProof.SelectSingleNode('stage').InnerText=$RationCheckpoint
    foreach ($stamp in $pendingProof.SelectNodes('before/li')) {
        $stampId=$stamp.SelectSingleNode('id').InnerText
        $rationLedger.SelectSingleNode("entities/li[id='$stampId']/revision").InnerText=$stamp.SelectSingleNode('revision').InnerText
    }
    $resultNode=$rationLedger.SelectSingleNode("entities/li[id='$proofId']")
    foreach ($child in @($resultNode.ChildNodes)) {
        if ($child.Name -notin @('id','kind','provider','quantity','revision','status','authority')) { $null=$resultNode.RemoveChild($child) }
    }
    $resultNode.SelectSingleNode('status').InnerText='Allocated'
    $resultNode.SelectSingleNode('quantity').InnerText='0'
    $null=$rationRecord.RemoveChild($proofRef)
    foreach ($key in @('ids','biology','inventories','receipts','spent','count')) {
        Copy-Item -LiteralPath (Join-Path $savePath ('INH_RationApplied-ration-'+$key+'.txt')) -Destination (Join-Path $savePath ('p04-ration-'+$key+'.txt')) -Force
    }
    $futureSave=Join-Path $savePath 'Saves/INH_RationCheckpoint.rws'
    $rationXml.Save($futureSave)
    $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
$logName = if ($Mode -eq 'RationCheckpoint') { $Mode+'-'+$RationCheckpoint } elseif ($Mode -eq 'RationFault') { $Mode+'-'+$RationFault } elseif ($Mode -eq 'DelegatedRouteFault') { $Mode+'-'+$DelegatedRouteFault } elseif ($Mode -eq 'DelegatedRouteCheckpoint') { $Mode+'-'+$DelegatedRouteCheckpoint } elseif ($Mode -eq 'RejectLoad') { $Mode+'-'+$Fault } elseif ($Mode -eq 'WorldFaultLoad') { $Mode+'-'+$WorldFault } elseif ($Mode -eq 'ColonyFaultLoad') { $Mode+'-'+$ColonyFault } elseif ($Mode -eq 'PhysicalFaultLoad') { $Mode+'-'+$PhysicalFault } elseif ($Mode -eq 'CoreCustodyFault') { $Mode+'-'+$CoreCustodyFault } elseif ($Mode -eq 'DelegatedCustodyFault') { $Mode+'-'+$DelegatedCustodyFault } else { $Mode }
if ($Mode.StartsWith('DelegatedUi')) {
    if (-not $WithColony) { throw 'Ordinary delegated UI tests require -WithColony' }
    $uiSource = switch ($Mode) {
        'DelegatedUiStore' { 'INH_DelegatedUiStorePrepared' }
        'DelegatedUiPrepareRelease' { 'INH_DelegatedUiStored' }
        'DelegatedUiRelease' { 'INH_DelegatedUiReleasePrepared' }
        'DelegatedUiReload' { 'INH_DelegatedUiReleased' }
        'DelegatedUiRegister' { 'INH_DelegatedUiReleased' }
        'DelegatedUiRegisteredReload' { 'INH_DelegatedUiRegistered' }
        'DelegatedUiRenderEnglish' { 'INH_DelegatedUiReleasePrepared' }
        'DelegatedUiRenderJapanese' { 'INH_DelegatedUiReleasePrepared' }
    }
    if ($uiSource) {
        foreach ($key in @('ids','count','states','owners','colony','custody')) {
            Copy-Item -LiteralPath (Join-Path $savePath ($uiSource+'-delegated-ui-'+$key+'.txt')) -Destination (Join-Path $savePath ('p04-delegated-ui-'+$key+'.txt')) -Force
        }
    }
}
if ($Mode.StartsWith('DelegatedUiCity') -or $Mode.StartsWith('SupplyPreviewCity') -or $Mode -eq 'SupplyIntentCityLock') {
    if ($Mode -ne 'DelegatedUiCityReload') {
        Copy-Item -LiteralPath (Join-Path $savePath 'INH_MaintenancePendingAppliedStopped-maintenance-ids.txt') -Destination (Join-Path $savePath 'p04-maintenance-ids.txt') -Force
    }
    if ($Mode -notin @('DelegatedUiCitySetupPrepared','DelegatedUiCitySetupApplied','DelegatedUiCityReload')) {
        $uiCitySource=if ($Mode.StartsWith('DelegatedUiCityPendingPrepared')) { 'INH_DelegatedUiCitySetupPrepared' } elseif ($Mode.StartsWith('DelegatedUiCityPendingApplied')) { 'INH_DelegatedUiCitySetupApplied' } else { 'INH_MaintenancePendingAppliedStopped' }
        $uiCityXml=New-Object Xml.XmlDocument
        $uiCityXml.Load((Join-Path $savePath ('Saves/'+$uiCitySource+'.rws')))
        $uiTick=$uiCityXml.SelectSingleNode('//tickManager/ticksGame')
        $uiTick.InnerText=([int]$uiTick.InnerText+60000).ToString([Globalization.CultureInfo]::InvariantCulture)
        $uiCore=$uiCityXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
        $uiRation=$uiCore.SelectSingleNode("inheritanceRationDebits/debits/li[cause='maintenance-pending-probe']")
        $uiProofRef=$uiRation.SelectSingleNode('appliedReceipt'); $uiProofId=$uiProofRef.InnerText
        $uiLedger=$uiCore.SelectSingleNode('inheritanceLedger')
        $uiProof=$uiLedger.SelectSingleNode("operations/li[result='$uiProofId']")
        $uiProof.SelectSingleNode('stage').InnerText=if ($Mode.EndsWith('Applying')) { 'Applying' } else { 'Prepared' }
        foreach ($stamp in $uiProof.SelectNodes('before/li')) {
            $stampId=$stamp.SelectSingleNode('id').InnerText
            $uiLedger.SelectSingleNode("entities/li[id='$stampId']/revision").InnerText=$stamp.SelectSingleNode('revision').InnerText
        }
        $uiResult=$uiLedger.SelectSingleNode("entities/li[id='$uiProofId']")
        foreach ($child in @($uiResult.ChildNodes)) { if ($child.Name -notin @('id','kind','provider','quantity','revision','status','authority')) { $null=$uiResult.RemoveChild($child) } }
        $uiResult.SelectSingleNode('status').InnerText='Allocated'; $uiResult.SelectSingleNode('quantity').InnerText='0'
        $null=$uiRation.RemoveChild($uiProofRef)
        $uiCityFixture=Join-Path $savePath 'Saves/INH_DelegatedUiCityLock.rws'
        $uiCityXml.Save($uiCityFixture)
        $uiCityFixtureHash=(Get-FileHash -LiteralPath $uiCityFixture -Algorithm SHA256).Hash
    }
}
$logPath = Join-Path $runRoot ($logName + '-Player.log')
if ($Mode -eq 'MaintenanceGap' -or $Mode -eq 'MaintenanceFault') {
    $maintenanceXml=New-Object Xml.XmlDocument
    $maintenanceXml.Load((Join-Path $savePath 'Saves/INH_MaintenanceStarted.rws'))
    if ($Mode -eq 'MaintenanceGap') {
        $tick=$maintenanceXml.SelectSingleNode('//tickManager/ticksGame')
        if ($null -eq $tick) { throw 'Missing game clock for gap fixture' }
        $tick.InnerText=([int]$tick.InnerText+180000).ToString([Globalization.CultureInfo]::InvariantCulture)
        $futureSave=Join-Path $savePath 'Saves/INH_MaintenanceGap.rws'
    } else {
        $component=$maintenanceXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
        $payload=$component.SelectSingleNode("inheritanceModulePayloads/li[owner='inheritance.colony']/data")
        $module=New-Object Xml.XmlDocument; $module.LoadXml($payload.InnerText)
        $plan=$module.SelectSingleNode('//maintenance/li[1]')
        if ($null -eq $plan) { throw 'Missing maintenance plan for refusal fixture' }
        switch ($MaintenanceFault) {
            'StartDay' { $plan.SelectSingleNode('startDay').InnerText=([int]$plan.SelectSingleNode('startDay').InnerText+1).ToString() }
            'ControlKind' { $plan.SelectSingleNode('controls/li[1]/kind').InnerText='Stop' }
            'ControlCause' { $plan.SelectSingleNode('controls/li[1]/cause').InnerText='changed-maintenance-cause' }
            'ControlParent' { $parent=$module.CreateElement('previousReceipt'); $parent.InnerText=$plan.SelectSingleNode('id').InnerText; $null=$plan.SelectSingleNode('controls/li[1]').AppendChild($parent) }
            'ControlMissing' { $node=$plan.SelectSingleNode('controls/li[1]'); $null=$node.ParentNode.RemoveChild($node); $plan.SelectSingleNode('controlCount').InnerText='0' }
            'PlansMissing' { $node=$module.SelectSingleNode('//maintenance'); $null=$node.ParentNode.RemoveChild($node); $module.SelectSingleNode('//maintenanceCount').InnerText='0' }
            'ControlCount' { $plan.SelectSingleNode('controlCount').InnerText='9' }
            'ControlReceipt' { $plan.SelectSingleNode('controls/li[1]/receipt').InnerText=$module.SelectSingleNode('//maintenance/li[2]/id').InnerText }
        }
        $payload.InnerText=$module.OuterXml
        $futureSave=Join-Path $savePath 'Saves/INH_RejectSmoke.rws'
    }
    $maintenanceXml.Save($futureSave)
    $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
}
if ($Mode.StartsWith('MaintenanceCityLock')) {
    $otherPrepared=$Mode.Contains('Other')
    $sourceName=if ($otherPrepared) { 'INH_MaintenanceCitySetup' } else { 'INH_MaintenancePendingAppliedStopped' }
    $cityXml=New-Object Xml.XmlDocument; $cityXml.Load((Join-Path $savePath ('Saves/'+$sourceName+'.rws')))
    if (-not $otherPrepared) { $tick=$cityXml.SelectSingleNode('//tickManager/ticksGame'); $tick.InnerText=([int]$tick.InnerText+60000).ToString([Globalization.CultureInfo]::InvariantCulture) }
    $cityCore=$cityXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
    $cityRation=$cityCore.SelectSingleNode("inheritanceRationDebits/debits/li[cause='maintenance-pending-probe']")
    $proofRef=$cityRation.SelectSingleNode('appliedReceipt'); $proofId=$proofRef.InnerText
    $cityLedger=$cityCore.SelectSingleNode('inheritanceLedger')
    $pendingProof=$cityLedger.SelectSingleNode("operations/li[result='$proofId']")
    $pendingProof.SelectSingleNode('stage').InnerText=if ($Mode.EndsWith('Applying')) { 'Applying' } else { 'Prepared' }
    foreach ($stamp in $pendingProof.SelectNodes('before/li')) {
        $stampId=$stamp.SelectSingleNode('id').InnerText
        $cityLedger.SelectSingleNode("entities/li[id='$stampId']/revision").InnerText=$stamp.SelectSingleNode('revision').InnerText
    }
    $result=$cityLedger.SelectSingleNode("entities/li[id='$proofId']")
    foreach ($child in @($result.ChildNodes)) { if ($child.Name -notin @('id','kind','provider','quantity','revision','status','authority')) { $null=$result.RemoveChild($child) } }
    $result.SelectSingleNode('status').InnerText='Allocated'; $result.SelectSingleNode('quantity').InnerText='0'
    $null=$cityRation.RemoveChild($proofRef)
    $futureSave=Join-Path $savePath 'Saves/INH_MaintenanceCityLock.rws'; $cityXml.Save($futureSave)
    $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
    Copy-Item -LiteralPath (Join-Path $savePath 'INH_MaintenancePendingAppliedStopped-maintenance-ids.txt') -Destination (Join-Path $savePath 'p04-maintenance-ids.txt') -Force
}
if ($Mode -eq 'MaintenanceFault') { $logPath=Join-Path $runRoot ($Mode+'-'+$MaintenanceFault+'-Player.log') }
$maintenanceBackup = switch ($Mode) {
    'MaintenanceDay' { 'INH_MaintenanceStarted' }
    'MaintenanceShortage' { 'INH_MaintenanceDayPaid' }
    'MaintenanceResume' { 'INH_MaintenanceShortage' }
    'MaintenanceStop' { 'INH_MaintenanceResumed' }
    'MaintenanceReload' { 'INH_MaintenanceStopped' }
    'MaintenanceGap' { 'INH_MaintenanceStarted' }
    'MaintenanceGapResume' { 'INH_MaintenanceGapHeld' }
    'MaintenanceRenderEnglish' { 'INH_MaintenanceShortage' }
    'MaintenanceRenderJapanese' { 'INH_MaintenanceShortage' }
    'MaintenancePendingStopPrepared' { 'INH_MaintenanceStarted' }
    'MaintenancePendingStopApplied' { 'INH_MaintenanceStarted' }
    'MaintenancePendingRecoverPrepared' { 'INH_MaintenancePendingStopped' }
    'MaintenancePendingRecoverApplied' { 'INH_MaintenancePendingAppliedStopped' }
    'MaintenanceReentry' { 'INH_MaintenanceStarted' }
    'MaintenanceCitySetup' { 'INH_MaintenancePendingAppliedStopped' }
}
if ($maintenanceBackup) {
    if (-not (Test-Path -LiteralPath (Join-Path $savePath ('Saves/'+$maintenanceBackup+'.rws')))) { throw 'Missing maintenance input save before game launch' }
    foreach ($key in @('ids','biology','inventories','receipts','spent','escrow','count','colony')) {
        Copy-Item -LiteralPath (Join-Path $savePath ($maintenanceBackup+'-maintenance-'+$key+'.txt')) -Destination (Join-Path $savePath ('p04-maintenance-'+$key+'.txt')) -Force
    }
}
if ($Mode.StartsWith('SupplyPreview')) {
    if (-not $WithColony) { throw 'Supply preview requires -WithColony' }
    $previewInput=if ($Mode.StartsWith('SupplyPreviewCity') -or $Mode -eq 'SupplyIntentCityLock') { 'INH_MaintenancePendingAppliedStopped' } else { 'INH_MaintenanceResumed' }
    Copy-Item -LiteralPath (Join-Path $savePath ($previewInput+'-maintenance-ids.txt')) -Destination (Join-Path $savePath 'p04-maintenance-ids.txt') -Force
}
if ($Mode.StartsWith('SupplyTransfer')) {
    if (-not $WithColony) { throw 'Supply transfer checks require -WithColony' }
    Copy-Item -LiteralPath (Join-Path $savePath 'INH_MaintenanceResumed-maintenance-ids.txt') -Destination (Join-Path $savePath 'p04-maintenance-ids.txt') -Force
    if ($Mode -eq 'SupplyTransferCheckpoint') {
        $phase=if ($SupplyTransferCheckpoint.StartsWith('Arm')) { 'arm' } else { 'disarm' }
        $inputStage=if ($SupplyTransferCheckpoint.EndsWith('Applying')) { 'Applying' } elseif ($SupplyTransferCheckpoint -eq 'ArmReconcile') { 'Reconcile' } else { 'Prepared' }
        $originalName=if ($phase -eq 'arm') { 'INH_SupplyTransferArmed' } else { 'INH_SupplyTransferDisarmed' }
        $checkpointSource=Join-Path $savePath ('Saves/'+$originalName+'.rws')
        $checkpointSourceHash=(Get-FileHash -LiteralPath $checkpointSource -Algorithm SHA256).Hash
        $checkpointXml=New-Object Xml.XmlDocument; $checkpointXml.Load($checkpointSource)
        $checkpointCore=$checkpointXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
        $checkpointRow=$checkpointCore.SelectSingleNode('inheritanceRationSupplyTransfers/records/li')
        $operationId=$checkpointRow.SelectSingleNode($phase+'Op').InnerText
        $operation=$checkpointCore.SelectSingleNode("inheritanceLedger/operations/li[id='$operationId']")
        if ($operation.SelectSingleNode('stage').InnerText -ne 'Committed') { throw 'Expected committed original transfer proof' }
        $operation.SelectSingleNode('stage').InnerText=if ($inputStage -eq 'Reconcile') { 'Prepared' } else { $inputStage }
        $resultId=$operation.SelectSingleNode('result').InnerText
        $result=$checkpointCore.SelectSingleNode("inheritanceLedger/entities/li[id='$resultId']")
        foreach ($name in @('custody','identity')) { $node=$result.SelectSingleNode($name); if ($node) { $null=$result.RemoveChild($node) } }
        $result.SelectSingleNode('revision').InnerText='0'; $result.SelectSingleNode('status').InnerText='Allocated'
        if ($phase -eq 'arm' -and $inputStage -ne 'Reconcile') {
            $cityId=$checkpointRow.SelectSingleNode('city').InnerText
            $cityRevision=$checkpointCore.SelectSingleNode("inheritanceLedger/entities/li[id='$cityId']/revision")
            $cityRevision.InnerText=([long]$cityRevision.InnerText-1).ToString()
        }
        $checkpointRow.SelectSingleNode('stage').InnerText='Armed'
        $futureSave=Join-Path $savePath 'Saves/INH_SupplyTransferCheckpoint.rws'
        $checkpointXml.Save($futureSave); $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
        [IO.File]::WriteAllLines((Join-Path $savePath 'p04-supply-transfer-checkpoint.txt'),@($phase,$inputStage,$originalName,$checkpointRow.SelectSingleNode('id').InnerText,$operationId))
    }
    if ($Mode -eq 'SupplyTransferFault') {
        $transferFaultSource=Join-Path $savePath 'Saves/INH_SupplyTransferArmed.rws'
        $transferFaultSourceHash=(Get-FileHash -LiteralPath $transferFaultSource -Algorithm SHA256).Hash
        $faultXml=New-Object Xml.XmlDocument
        $faultXml.Load($transferFaultSource)
        $faultCore=$faultXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
        $table=$faultCore.SelectSingleNode('inheritanceRationSupplyTransfers')
        if (!$table -or $faultCore.SelectSingleNode('inheritanceHeader/schemaVersion').InnerText -ne '9') { throw 'Expected actual Core9 Armed save' }
        $row=$table.SelectSingleNode('records/li')
        $transferExpectedReason='Supply transfer fixed manifest is incomplete or invalid'
        switch ($SupplyTransferFault) {
            'MissingJournal' { $null=$faultCore.RemoveChild($table); $transferExpectedReason='Core supply transfer journal is missing' }
            'LegacyMixed' {
                $faultCore.SelectSingleNode('inheritanceHeader/schemaVersion').InnerText='8'
                $faultCore.SelectSingleNode("inheritanceHeader/moduleStamps/li[packageId='inheritance.core']/schemaVersion").InnerText='8'
                $transferExpectedReason='Legacy save unexpectedly contains supply transfer data'
            }
            'UnknownField' { $node=$faultXml.CreateElement('appliedFood'); $node.InnerText='NotSupported'; $null=$row.AppendChild($node) }
            'CountChanged' { $table.SelectSingleNode('recordCount').InnerText='2'; $transferExpectedReason='Mandatory Core9 supply transfer table is missing or invalid' }
            'Orphan' { $null=$table.SelectSingleNode('records').RemoveChild($row); $table.SelectSingleNode('recordCount').InnerText='0'; $transferExpectedReason='Core arm proof is orphaned from its transfer responsibility journal' }
            'ManifestChanged' { $row.SelectSingleNode('count').InnerText=([int]$row.SelectSingleNode('count').InnerText+1).ToString() }
            'Duplicate' { $null=$table.SelectSingleNode('records').AppendChild($row.CloneNode($true)); $table.SelectSingleNode('recordCount').InnerText='2'; $transferExpectedReason='Supply transfer journal duplicates its proof IDs or actor/cause' }
            'FutureStage' { $row.SelectSingleNode('stage').InnerText='2' }
        }
        $futureSave=Join-Path $savePath 'Saves/INH_SupplyTransferReject.rws'
        $faultXml.Save($futureSave)
        $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $transferFaultSource -Algorithm SHA256).Hash -ne $transferFaultSourceHash) { throw 'Original transfer fixture changed' }
    }
}
if ($Mode.StartsWith('SupplyIntent')) {
    if (-not $WithColony) { throw 'Supply intention checks require -WithColony' }
    $intentInput=if ($Mode -eq 'SupplyIntentCityLock') { 'INH_MaintenancePendingAppliedStopped' } else { 'INH_MaintenanceResumed' }
    Copy-Item -LiteralPath (Join-Path $savePath ($intentInput+'-maintenance-ids.txt')) -Destination (Join-Path $savePath 'p04-maintenance-ids.txt') -Force
    if ($Mode -in @('SupplyIntentMissingJournal','SupplyIntentLegacyMixed','SupplyIntentUnknownField','SupplyIntentCountChanged')) {
        $intentFaultSource=Join-Path $savePath 'Saves/INH_SupplyIntentPrepared.rws'
        $intentFaultSourceHash=(Get-FileHash -LiteralPath $intentFaultSource -Algorithm SHA256).Hash
        $intentFaultXml=New-Object Xml.XmlDocument
        $intentFaultXml.Load($intentFaultSource)
        $intentCore=$intentFaultXml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']")
        $intentTable=$intentCore.SelectSingleNode('inheritanceRationSupplyIntents')
        if (!$intentTable -or $intentCore.SelectSingleNode('inheritanceHeader/schemaVersion').InnerText -ne '8') { throw 'Expected actual schema8 prepared save' }
        switch ($Mode) {
            'SupplyIntentMissingJournal' { $null=$intentCore.RemoveChild($intentTable) }
            'SupplyIntentLegacyMixed' {
                $intentCore.SelectSingleNode('inheritanceHeader/schemaVersion').InnerText='7'
                $intentCore.SelectSingleNode("inheritanceHeader/moduleStamps/li[packageId='inheritance.core']/schemaVersion").InnerText='7'
            }
            'SupplyIntentUnknownField' {
                $unknown=$intentFaultXml.CreateElement('appliedFood')
                $unknown.InnerText='NotPermitted'
                $null=$intentTable.SelectSingleNode('records/li').AppendChild($unknown)
            }
            'SupplyIntentCountChanged' {
                $countNode=$intentTable.SelectSingleNode('recordCount')
                $countNode.InnerText=([int]$countNode.InnerText+1).ToString([Globalization.CultureInfo]::InvariantCulture)
            }
        }
        $futureSave=Join-Path $savePath 'Saves/INH_SupplyIntentReject.rws'
        $intentFaultXml.Save($futureSave)
        $futureHash=(Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $intentFaultSource -Algorithm SHA256).Hash -ne $intentFaultSourceHash) { throw 'Original supply fixture changed' }
    }
}
$testFlag = switch ($Mode) {
    'SupplyTransferStaleArmed' { '-inh-supply-transfer -inh-supply-transfer-stale-armed' }
    'SupplyTransferStaleArmedLoad' { '-inh-supply-transfer -inh-supply-transfer-stale-armed-load' }
    'SupplyTransferCheckpoint' { '-inh-supply-transfer -inh-supply-transfer-checkpoint' }
    'SupplyTransferCheckpointReload' { '-inh-supply-transfer -inh-supply-transfer-checkpoint-reload' }
    'SupplyTransferFault' { '-inh-reject-load -inh-supply-transfer-reject' }
    'SupplyTransferArm' { '-inh-supply-transfer -inh-supply-transfer-arm' }
    'SupplyTransferLoad' { '-inh-supply-transfer -inh-supply-transfer-load' }
    'SupplyTransferFinalLoad' { '-inh-supply-transfer -inh-supply-transfer-final-load' }
    'SupplyTransferStale' { '-inh-supply-transfer -inh-supply-transfer-stale' }
    'SupplyTransferAnimal' { '-inh-supply-transfer -inh-supply-transfer-animal -inh-supply-intent' }
    'SupplyIntentPrepare' { '-inh-supply-intent -inh-supply-intent-prepare' }
    'SupplyIntentLoad' { '-inh-supply-intent -inh-supply-intent-load' }
    'SupplyIntentCancelledLoad' { '-inh-supply-intent -inh-supply-intent-cancelled-load' }
    'SupplyIntentStale' { '-inh-supply-intent -inh-supply-intent-stale' }
    'SupplyIntentStaleLoad' { '-inh-supply-intent -inh-supply-intent-stale-load' }
    'SupplyIntentStalePending' { '-inh-supply-intent -inh-supply-intent-stale-pending' }
    'SupplyIntentStalePendingLoad' { '-inh-supply-intent -inh-supply-intent-stale-pending-load' }
    'SupplyIntentCityLock' { '-inh-supply-intent -inh-supply-intent-city-lock' }
    'SupplyIntentMissingJournal' { '-inh-reject-load -inh-supply-intent-reject' }
    'SupplyIntentLegacyMixed' { '-inh-reject-load -inh-supply-intent-reject' }
    'SupplyIntentUnknownField' { '-inh-reject-load -inh-supply-intent-reject' }
    'SupplyIntentCountChanged' { '-inh-reject-load -inh-supply-intent-reject' }
    'SupplyPreview' { '-inh-supply-preview' }
    'SupplyPreviewCityLock' { '-inh-supply-preview-city-lock' }
    'SupplyPreviewRenderEnglish' { '-inh-supply-preview-render' }
    'SupplyPreviewRenderJapanese' { '-inh-supply-preview-render-japanese' }
    'SupplyPreviewCityRenderEnglish' { '-inh-supply-preview-city-lock -inh-supply-preview-render' }
    'SupplyPreviewCityRenderJapanese' { '-inh-supply-preview-city-lock -inh-supply-preview-render-japanese' }
    'DelegatedUiCityRenderEnglish' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-render' }
    'DelegatedUiCityRenderJapanese' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-render-japanese' }
    'DelegatedUiCitySetupPrepared' { '-inh-delegated-ui-probe -inh-delegated-ui-city-setup-prepared' }
    'DelegatedUiCitySetupApplied' { '-inh-delegated-ui-probe -inh-delegated-ui-city-setup-applied' }
    'DelegatedUiCityCleanPrepared' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock' }
    'DelegatedUiCityCleanApplying' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-applying' }
    'DelegatedUiCityPendingPreparedProofPrepared' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-pending-prepared' }
    'DelegatedUiCityPendingPreparedProofApplying' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-pending-prepared -inh-delegated-ui-city-applying' }
    'DelegatedUiCityPendingAppliedProofPrepared' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-pending-applied' }
    'DelegatedUiCityPendingAppliedProofApplying' { '-inh-delegated-ui-probe -inh-delegated-ui-city-lock -inh-delegated-ui-city-pending-applied -inh-delegated-ui-city-applying' }
    'DelegatedUiCityReload' { '-inh-delegated-ui-probe -inh-delegated-ui-city-reload' }
    'DelegatedUiStart' { '-inh-delegated-ui-probe -inh-delegated-ui-start' }
    'DelegatedUiStore' { '-inh-delegated-ui-probe -inh-delegated-ui-store' }
    'DelegatedUiPrepareRelease' { '-inh-delegated-ui-probe -inh-delegated-ui-prepare-release' }
    'DelegatedUiRelease' { '-inh-delegated-ui-probe -inh-delegated-ui-release' }
    'DelegatedUiReload' { '-inh-delegated-ui-probe -inh-delegated-ui-reload' }
    'DelegatedUiRegister' { '-inh-delegated-ui-probe -inh-delegated-ui-register' }
    'DelegatedUiRegisteredReload' { '-inh-delegated-ui-probe -inh-delegated-ui-registered-reload' }
    'DelegatedUiRenderEnglish' { '-inh-delegated-ui-probe -inh-delegated-ui-render' }
    'DelegatedUiRenderJapanese' { '-inh-delegated-ui-probe -inh-delegated-ui-render-japanese' }
    'MaintenanceCitySetup' { '-inh-maintenance-city-setup' }
    'MaintenanceCityLockCleanPrepared' { '-inh-maintenance-city-lock' }
    'MaintenanceCityLockCleanApplying' { '-inh-maintenance-city-lock -inh-maintenance-city-applying' }
    'MaintenanceCityLockOtherPrepared' { '-inh-maintenance-city-lock -inh-maintenance-city-other-prepared' }
    'MaintenanceCityLockOtherApplying' { '-inh-maintenance-city-lock -inh-maintenance-city-other-prepared -inh-maintenance-city-applying' }
    'MaintenanceFreshStart' { '-inh-maintenance-probe -inh-maintenance-fresh-start -inh-delegated-maintenance' }
    'MaintenancePendingStopPrepared' { '-inh-maintenance-probe -inh-maintenance-pending-stop-prepared' }
    'MaintenancePendingStopApplied' { '-inh-maintenance-probe -inh-maintenance-pending-stop-applied' }
    'MaintenancePendingRecoverPrepared' { '-inh-maintenance-probe -inh-maintenance-pending-recover' }
    'MaintenancePendingRecoverApplied' { '-inh-maintenance-probe -inh-maintenance-pending-recover -inh-maintenance-recover-applied' }
    'MaintenanceReentry' { '-inh-maintenance-probe -inh-maintenance-reentry -inh-delegated-maintenance -inh-delegated-custody' }
    'MaintenanceStart' { '-inh-maintenance-probe -inh-maintenance-start -inh-delegated-maintenance' }
    'MaintenanceDay' { '-inh-maintenance-probe -inh-maintenance-day' }
    'MaintenanceShortage' { '-inh-maintenance-probe -inh-maintenance-shortage' }
    'MaintenanceResume' { '-inh-maintenance-probe -inh-maintenance-resume' }
    'MaintenanceStop' { '-inh-maintenance-probe -inh-maintenance-stop' }
    'MaintenanceReload' { '-inh-maintenance-probe -inh-maintenance-reload' }
    'MaintenanceGap' { '-inh-maintenance-probe -inh-maintenance-gap' }
    'MaintenanceGapResume' { '-inh-maintenance-probe -inh-maintenance-gap-resume' }
    'MaintenanceRenderEnglish' { '-inh-maintenance-probe -inh-maintenance-render' }
    'MaintenanceRenderJapanese' { '-inh-maintenance-probe -inh-maintenance-render -inh-maintenance-japanese' }
    'MaintenanceFault' { '-inh-reject-load' }
    'RationPrepare' { '-inh-ration-probe -inh-ration-prepare -inh-delegated-rations -inh-physical-probe' }
    'RationApply' { '-inh-ration-probe -inh-ration-apply -inh-physical-probe' }
    'RationCommit' { '-inh-ration-probe -inh-ration-commit -inh-physical-probe' }
    'RationRelease' { '-inh-ration-probe -inh-ration-release -inh-physical-probe' }
    'RationReload' { '-inh-ration-probe -inh-ration-reload -inh-physical-probe' }
    'RationReceiptLoad' { '-inh-ration-probe -inh-ration-reload -inh-ration-receipt-missing -inh-physical-probe' }
    'RationCheckpoint' { '-inh-ration-probe -inh-ration-checkpoint -inh-ration-checkpoint-'+$RationCheckpoint.ToLower()+' -inh-physical-probe' }
    'RationFault' { '-inh-reject-load' }
    'DelegatedRouteFault' { '-inh-reject-load' }
    'DelegatedRouteUncertifiedSave' { '-inh-delegated-route-probe -inh-delegated-route-uncertifiedsave -inh-physical-probe' }
    'DelegatedRouteCheckpoint' { '-inh-delegated-route-probe -inh-delegated-route-checkpoint -inh-delegated-route-checkpoint-'+$DelegatedRouteCheckpoint.ToLower()+' -inh-physical-probe' }
    'DelegatedRouteReserve' { '-inh-delegated-route-probe -inh-delegated-route-reserve -inh-physical-probe -inh-delegated-custody' }
    'DelegatedRouteTransit' { '-inh-delegated-route-probe -inh-delegated-route-transit -inh-physical-probe' }
    'DelegatedRouteArrival' { '-inh-delegated-route-probe -inh-delegated-route-arrival -inh-physical-probe' }
    'DelegatedRouteStored' { '-inh-delegated-route-probe -inh-delegated-route-stored -inh-physical-probe -inh-delegated-custody' }
    'DelegatedRouteReturn' { '-inh-delegated-route-probe -inh-delegated-route-return -inh-physical-probe' }
    'DelegatedRouteEntering' { '-inh-delegated-route-probe -inh-delegated-route-entering -inh-physical-probe' }
    'DelegatedRouteFinal' { '-inh-delegated-route-probe -inh-delegated-route-final -inh-physical-probe' }
    'DelegatedRouteReloadFinal' { '-inh-delegated-route-probe -inh-delegated-route-reloadfinal -inh-physical-probe' }
    'DelegatedCustodyFault' { '-inh-reject-load' }
    'DelegatedCustodyPrepare' { '-inh-delegated-custody-probe -inh-delegated-custody-prepare -inh-delegated-custody -inh-physical-probe' }
    'DelegatedCustodyApply' { '-inh-delegated-custody-probe -inh-delegated-custody-apply -inh-physical-probe' }
    'DelegatedCustodyCommit' { '-inh-delegated-custody-probe -inh-delegated-custody-commit -inh-delegated-custody -inh-physical-probe' }
    'DelegatedCustodyReleasePrepare' { '-inh-delegated-custody-probe -inh-delegated-custody-release-prepare -inh-physical-probe' }
    'DelegatedCustodyReleaseApply' { '-inh-delegated-custody-probe -inh-delegated-custody-release-apply -inh-physical-probe' }
    'DelegatedCustodyRelease' { '-inh-delegated-custody-probe -inh-delegated-custody-release -inh-physical-probe' }
    'DelegatedCustodyFinal' { '-inh-delegated-custody-probe -inh-delegated-custody-final -inh-physical-probe' }
    'DelegatedCustodyReloadFinal' { '-inh-delegated-custody-probe -inh-delegated-custody-reloadfinal -inh-physical-probe' }
    'CoreCustodyPrepare' { '-quicktest -inh-core-custody -inh-core-custody-prepare' }
    'CoreCustodyApply' { '-inh-core-custody -inh-core-custody-apply' }
    'CoreCustodyCommit' { '-inh-core-custody -inh-core-custody-commit' }
    'CoreCustodyReleasePrepare' { '-inh-core-custody -inh-core-custody-release-prepare' }
    'CoreCustodyRelease' { '-inh-core-custody -inh-core-custody-release' }
    'CoreCustodyFinal' { '-inh-core-custody -inh-core-custody-final' }
    'CoreCustodyReload' { '-inh-core-custody -inh-core-custody-reload' }
    'CoreCustodyFault' { '-inh-reject-load' }
    'SchemaFourPayloadStamp' { '-inh-reject-load -inh-direct-test-load' }
    'SchemaFourPayloadValid' { '-inh-schema-four-payload-valid -inh-direct-test-load' }
    'SchemaFivePayloadStamp' { '-inh-reject-load -inh-direct-test-load' }
    'SchemaFivePayloadValid' { '-inh-schema-four-payload-valid -inh-direct-test-load' }
    'LocalCustodySave' { '-inh-local-custody -inh-local-custody-save -inh-colony-custody -inh-physical-probe' }
    'LocalCustodyLoad' { '-inh-local-custody -inh-local-custody-load -inh-colony-custody -inh-physical-probe' }
    'LocalCustodyFinal' { '-inh-local-custody -inh-local-custody-final -inh-colony-custody -inh-physical-probe' }
    'LocalCustodyRenderEnglish' { '-inh-local-custody -inh-local-custody-render -inh-colony-custody -inh-physical-probe' }
    'LocalCustodyRenderJapanese' { '-inh-local-custody -inh-local-custody-render -inh-local-custody-japanese -inh-colony-custody -inh-physical-probe' }
    'LocalCustodyPendingSave' { '-inh-local-custody -inh-local-custody-pending-save -inh-colony-custody -inh-physical-probe' }
    'LocalCustodyPendingLoad' { '-inh-local-custody -inh-local-custody-pending-load -inh-physical-probe' }
    'LocalCustodyPendingFinal' { '-inh-local-custody -inh-local-custody-pending-final -inh-physical-probe' }
    'Header' { '-inh-smoke' }
    'WorldSave' { '-quicktest -inh-world-save' }
    'WorldLoad' { '-inh-world-load' }
    'CompletedLoad' { '-inh-completed-load' }
    # Only controlled historical/synthetic fixtures bypass the native metadata
    # dialog when their original active packs differ from the test configuration.
    # Core save compatibility and migration validation still run normally.
    'LegacyLoad' { '-inh-legacy-load -inh-direct-test-load' }
    'LegacyReload' { '-inh-legacy-reload' }
    'SchemaTwoLoad' { '-inh-schema-two-load -inh-direct-test-load' }
    'FutureLoad' { '-inh-future-load' }
    'RejectLoad' { '-inh-reject-load' }
    'ReconcileLoad' { '-inh-reconcile-load' }
    'PrototypeSave' { '-quicktest -inh-world-prototype -inh-prototype-save' }
    'PrototypeLoad' { '-inh-world-prototype -inh-prototype-load' }
    'PrototypeCompletedLoad' { '-inh-world-prototype -inh-prototype-completed-load' }
    'PlanResume' { '-inh-world-prototype -inh-plan-resume' }
    'VanillaAttach' { '-inh-world-prototype -inh-vanilla-attach' }
    'WorldFaultLoad' { '-inh-reject-load' }
    'RequiredWorldLoad' { '-inh-required-world-load' }
    'ColonySave' { '-quicktest -inh-colony-save' }
    'ColonyLoad' { '-inh-colony-load' }
    'ColonyCompletedLoad' { '-inh-colony-completed' }
    'ColonyFinalLoad' { '-inh-colony-final' }
    'ColonyResume' { '-inh-colony-resume' }
    'ColonyFaultLoad' { '-inh-reject-load' }
    'RequiredColonyLoad' { '-inh-required-world-load' }
    'PhysicalSave' { '-quicktest -inh-physical-probe -inh-physical-save' }
    'PhysicalLoad' { '-inh-physical-probe -inh-physical-load' }
    'PhysicalFinalLoad' { '-inh-physical-probe -inh-physical-final' }
    'PhysicalReservedLoad' { '-inh-physical-probe -inh-physical-reserved' }
    'PhysicalDepartureIntent' { '-inh-physical-probe -inh-physical-departure-intent' }
    'PhysicalArrivalIntent' { '-inh-physical-probe -inh-physical-arrival-intent' }
    'PhysicalFaultLoad' { '-inh-reject-load' }
    'PhysicalDepartedCheckpoint' { '-inh-physical-probe -inh-physical-checkpoint -inh-physical-load' }
    'PhysicalDeliveredCheckpoint' { '-inh-physical-probe -inh-physical-checkpoint -inh-physical-final' }
    'PhysicalAutoLoad' { '-inh-physical-probe -inh-physical-auto -inh-physical-final' }
    'UiSave' { '-quicktest -inh-ui -inh-ui-save' }
    'UiLoad' { '-inh-ui -inh-ui-load' }
    'UiFinalLoad' { '-inh-ui -inh-ui-final' }
    'UiRenderEnglish' { '-inh-ui -inh-ui-render' }
    'UiRenderJapanese' { '-inh-ui -inh-ui-render -inh-ui-japanese' }
    'UiLossLoad' { '-inh-ui -inh-ui-loss' }
    'UiLossFinalLoad' { '-inh-ui -inh-ui-loss-final' }
    'AnimalSave' { '-inh-animal -inh-animal-save' }
    'JourneySave' { '-inh-journey -inh-journey-save' }
    'JourneyLoad' { '-inh-journey -inh-journey-load' }
    'JourneyFinal' { '-inh-journey -inh-journey-final' }
    'CustodySave' { '-inh-custody -inh-custody-save' }
    'CustodyLoad' { '-inh-custody -inh-custody-load' }
    'CustodyFinal' { '-inh-custody -inh-custody-final' }
    'JourneyDepletedSave' { '-inh-journey -inh-journey-save -inh-journey-deplete' }
    'JourneyDepletedLoad' { '-inh-journey -inh-journey-load -inh-journey-deplete' }
    'AnimalReserved' { '-inh-animal -inh-animal-reserved' }
    'AnimalLoad' { '-inh-animal -inh-animal-load' }
    'AnimalDepartureCheckpoint' { '-inh-animal -inh-animal-checkpoint -inh-animal-load' }
    'AnimalArrivalCheckpoint' { '-inh-animal -inh-animal-checkpoint -inh-animal-final' }
    'AnimalFinal' { '-inh-animal -inh-animal-final' }
    'AnimalRenderEnglish' { '-inh-animal -inh-animal-render' }
    'AnimalRenderJapanese' { '-inh-animal -inh-animal-render -inh-animal-japanese' }
    'LossAccept' { '-inh-loss -inh-loss-accept' }
    'LossLoad' { '-inh-loss -inh-loss-load' }
    'LossFinal' { '-inh-loss -inh-loss-final' }
    'LossCheckpoint' { '-inh-loss -inh-loss-checkpoint' }
    'LossDelivered' { '-inh-loss -inh-loss-delivered' }
    'LossZero' { '-inh-loss -inh-loss-zero' }
    'LossZeroFinal' { '-inh-loss -inh-loss-zero-final' }
    'LossRenderEnglish' { '-inh-loss -inh-loss-render' }
    'LossRenderJapanese' { '-inh-loss -inh-loss-render -inh-loss-japanese' }
}
if ($WithWorld -and $Mode -in @('ColonySave','ColonyLoad','ColonyCompletedLoad','ColonyFinalLoad','ColonyResume')) { $testFlag += ' -inh-world-prototype' }
if ($Mode.StartsWith('DelegatedCustody') -and $Mode -ne 'DelegatedCustodyFault') { $testFlag += ' -inh-colony-custody' }
if ($WithDlc) { $testFlag += ' -inh-four-dlc' }
$graphicsFlag = if ($Mode -in @('UiRenderEnglish','UiRenderJapanese','LossRenderEnglish','LossRenderJapanese','AnimalRenderEnglish','AnimalRenderJapanese','LocalCustodyRenderEnglish','LocalCustodyRenderJapanese','MaintenanceRenderEnglish','MaintenanceRenderJapanese','DelegatedUiRenderEnglish','DelegatedUiRenderJapanese','DelegatedUiCityRenderEnglish','DelegatedUiCityRenderJapanese','SupplyPreviewRenderEnglish','SupplyPreviewRenderJapanese','SupplyPreviewCityRenderEnglish','SupplyPreviewCityRenderJapanese')) { '-screen-fullscreen 0 ' } else { '-batchmode ' }
$arguments = $graphicsFlag + '-screen-width 1280 -screen-height 720 -logFile "' + $logPath + '" "-savedatafolder=' + $savePath + '" ' + $testFlag
$process = Start-Process -FilePath (Join-Path $runtimePath 'RimWorldWin64.exe') -ArgumentList $arguments -WorkingDirectory $runtimePath -WindowStyle Hidden -PassThru
[IO.File]::WriteAllText((Join-Path $runRoot 'process-id.txt'), $process.Id.ToString())
Write-Output "Test PID: $($process.Id)"
Write-Output "Test log: $logPath"
Write-Output 'The harness exits the test process after reporting PASS or FAIL. Existing play sessions are separate.'
$timer = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(1000)) {
    if ($timer.Elapsed.TotalSeconds -gt 180) {
        $process.Kill()
        throw "Smoke test timed out; inspect $logPath"
    }
}
$process.Refresh()
$text = [IO.File]::ReadAllText($logPath)
if ($text -notmatch '\[INH-SMOKE\] ALL PASS') { throw "Smoke test did not pass; inspect $logPath" }
if ($process.ExitCode -ne 0) { throw "Game test exited with code $($process.ExitCode); inspect $logPath" }
$checkedText=$text
if ($Mode -in @('UiRenderJapanese','LossRenderJapanese','AnimalRenderJapanese','LocalCustodyRenderJapanese','MaintenanceRenderJapanese','DelegatedUiRenderJapanese','DelegatedUiCityRenderJapanese','SupplyPreviewRenderJapanese','SupplyPreviewCityRenderJapanese')) {
    # This exact game revision ships Japanese injection errors even with only the official packs.
    # Permit only the captured baseline set and its exact count; new translation errors still fail.
    $expectedTranslation=Get-Content (Join-Path $projectRoot "Tests/ColonySmoke/JapaneseBaseline-$runName.txt") | Sort-Object -CaseSensitive
    $translationReport=if ($Mode.StartsWith('SupplyPreview')) { 'supply-preview-translation-errors-Japanese.txt' } elseif ($Mode -eq 'DelegatedUiRenderJapanese') { 'delegated-ui-translation-errors-Japanese.txt' } elseif ($Mode -eq 'DelegatedUiCityRenderJapanese') { 'delegated-ui-city-translation-errors-Japanese.txt' } else { 'translation-errors-Japanese.txt' }
    $observedTranslation=Get-Content (Join-Path $savePath $translationReport) | Sort-Object -CaseSensitive
    if (@(Compare-Object $expectedTranslation $observedTranslation -CaseSensitive).Count -ne 0) { throw 'Japanese translation errors differ from the official-pack baseline' }
    $expectedWarning='Translation data for language Japanese has '+$expectedTranslation.Count+' errors. Generate translation report for more info.'
    if (-not $checkedText.Contains($expectedWarning)) { throw 'Japanese baseline warning count changed' }
    $checkedText=$checkedText.Replace($expectedWarning,'')
    Write-Output "Japanese official-pack baseline matched: $($expectedTranslation.Count) existing injection errors; no additional load errors."
}
if ($checkedText -match 'Exception:|Root level exception|\[INH-SMOKE\] FAIL|\[INH-WORLD\] FAIL|\[INH-P03\] FAIL|\[INH-P04\] FAIL|\[INH-UI\] FAIL|\[INH-LOSS\] FAIL|\[INH-ANIMAL\] FAIL|\[INH-JOURNEY\] FAIL|\[INH-CUSTODY\] FAIL|\[INH-CORE-CUSTODY\] FAIL|\[INH-LOCAL-CUSTODY\] FAIL|\[INH-DELEGATED-CUSTODY\] FAIL|\[INH-DELEGATED-ROUTE\] FAIL|\[INH-RATIONS?\] FAIL|\[INH-MAINTENANCE\] FAIL|\[INH-DELEGATED-UI\] FAIL|\[INH-SUPPLY-PREVIEW\] FAIL|\[INH-SUPPLY-INTENT\] FAIL|\[INH-SUPPLY-TRANSFER\] FAIL|\[INH-SUPPLY-CHECKPOINT\] FAIL|Error loading|Translation data for language .* has [1-9][0-9]* errors|Could not execute post-long-event|Could not regenerate|Could not resolve cross-reference') {
    throw "Game exception or save error found; inspect $logPath"
}
if ($Mode -in @('SupplyTransferFault','FutureLoad','RejectLoad','ReconcileLoad','WorldFaultLoad','RequiredWorldLoad','ColonyFaultLoad','RequiredColonyLoad','PhysicalFaultLoad','CoreCustodyFault','DelegatedCustodyFault','DelegatedRouteFault','RationFault','MaintenanceFault','SupplyIntentMissingJournal','SupplyIntentLegacyMixed','SupplyIntentUnknownField','SupplyIntentCountChanged','SchemaFourPayloadStamp','SchemaFivePayloadStamp')) {
    if ($Mode -ne 'ReconcileLoad') {
    if ($text -notmatch '\[INH-FUTURE\] MAIN MENU') { throw 'Did not reach main menu after rejecting future save' }
    }
    if ((Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Future save was changed during rejection' }
    Write-Output 'Fixture file remained unchanged; validation/recovery assertions passed.'
}
if ($Mode -eq 'DelegatedRouteCheckpoint' -and (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Checkpoint source fixture changed' }
if ($Mode -in @('SupplyIntentMissingJournal','SupplyIntentLegacyMixed','SupplyIntentUnknownField','SupplyIntentCountChanged') -and
    (Get-FileHash -LiteralPath $intentFaultSource -Algorithm SHA256).Hash -ne $intentFaultSourceHash) { throw 'Original supply fixture changed during rejection' }
if ($Mode -eq 'RationReceiptLoad' -and (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Ration checkpoint source fixture changed' }
if ($Mode -eq 'RationCheckpoint' -and (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Ration pending proof fixture changed' }
if ($Mode -eq 'MaintenanceGap' -and (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Maintenance gap source fixture changed' }
if ($Mode.StartsWith('MaintenanceCityLock') -and (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Maintenance city checkpoint source fixture changed' }
Write-Output 'Smoke assertions passed. Inspect the full log separately for game initialization errors.'

if ($uiCityFixtureHash) {
    if ((Get-FileHash -LiteralPath $uiCityFixture -Algorithm SHA256).Hash -ne $uiCityFixtureHash) { throw 'The original delegated UI city-lock fixture was modified' }
    Write-Output 'Original delegated UI city-lock fixture SHA256 remained unchanged.'
}

if ($Mode -eq 'SupplyTransferFault') {
 if ((Get-FileHash -LiteralPath $transferFaultSource -Algorithm SHA256).Hash -ne $transferFaultSourceHash) { throw 'Original transfer save changed during rejection' }
 if (!$text.Contains($transferExpectedReason)) { throw ('Rejected for a different reason than expected: '+$transferExpectedReason) }
 Write-Output ('Exact supply-transfer refusal reason verified: '+$transferExpectedReason)
}

if ($Mode -eq 'SupplyTransferCheckpoint') {
 if ((Get-FileHash -LiteralPath $checkpointSource -Algorithm SHA256).Hash -ne $checkpointSourceHash -or (Get-FileHash -LiteralPath $futureSave -Algorithm SHA256).Hash -ne $futureHash) { throw 'Checkpoint source or pending fixture changed' }
 Write-Output ('Controlled metadata checkpoint and source preserved: '+$SupplyTransferCheckpoint)
}
