using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    public sealed class LossProbe : GameComponent
    {
        private bool done,captured,ready;
        private int frames;
        private PhysicalLossWindow window;
        public LossProbe(Game game) { }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-LOSS] PASS "+text); }
        private static void Refuses(Action action,string text) { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,text); }
        private static long Steel()
        { return Find.Maps.Sum(m => m.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t => (long)t.stackCount))+Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Where(p => p.inventory != null).Sum(p => p.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => (long)t.stackCount)); }
        private static void Walk(Caravan caravan,Map map)
        { for (int i=0;i<2000 && (caravan.Tile != map.Tile || caravan.pather.Moving);i++) { Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+250); caravan.pather.PatherTickInterval(250); } Assert(caravan.Tile == map.Tile,"Native path reaches the target without changing its tile directly"); }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("inh-loss") || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current;
                if (!ready) { Assert(core.IsReady && service != null && ColonyMod.PhysicalEnabled,"Loss workflow uses ordinary enabled settings"); ready=true; }
                Find.TickManager.Pause(); var ids=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-ui-ids.txt"));
                string cause=ids[9]; Map destination=Find.Maps.Single(m => m.uniqueID.ToString() == ids[8]);
                if (GenCommandLine.CommandLineArgPassed("inh-loss-render")) { Render(service,cause); return; }
                done=true; var controller=new TransportCommandController(service); int before=core.Ledger.EntityCount;
                var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == ids[6]);
                Pawn pawn=caravan == null ? destination.mapPawns.AllPawns.Single(p => p.GetUniqueLoadID() == ids[3]) : caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == ids[3]);
                Thing cargo=pawn.inventory.innerContainer.SingleOrDefault(t => t.GetUniqueLoadID() == ids[4]);
                var snapshot=service.PhysicalTransports.Single(t => t.Cause == cause);
                if (GenCommandLine.CommandLineArgPassed("inh-loss-accept"))
                {
                    Refuses(delegate { controller.Recheck(cause); },"Recheck cannot silently accept missing delivery cargo");
                    var stale=service.PreviewPhysicalLoss(cause); Assert(stale.Lost == 1,"Preview describes loss against the original ordered quantity");
                    cargo.SplitOff(1).Destroy();
                    Refuses(delegate { controller.AcceptLoss(stale); },"A further loss invalidates the open preview before creating a certificate");
                    Assert(core.Ledger.EntityCount == before,"Stale loss acceptance adds no registry records");
                    var def=cargo.def; cargo.def=ThingDefOf.WoodLog;
                    Refuses(delegate { service.PreviewPhysicalLoss(cause); },"A changed native cargo Def cannot be acknowledged as ordinary loss"); cargo.def=def;
                    var faction=pawn.Faction; pawn.SetFaction(Find.FactionManager.AllFactions.First(f => f != faction));
                    Refuses(delegate { service.PreviewPhysicalLoss(cause); },"A captured or defected original pawn requires rescue before loss acceptance"); pawn.SetFaction(faction);
                    var preview=service.PreviewPhysicalLoss(cause); string first=controller.AcceptLoss(preview);
                    Assert(controller.AcceptLoss(preview) == first && core.Ledger.EntityCount == before+2 && cargo.stackCount == 28,"Repeated acceptance creates one operation and one certificate and retains the original cargo");
                    cargo.SplitOff(1).Destroy(); service.Tick();
                    Assert(service.PhysicalTransports.Single(t => t.Cause == cause).Stage == PhysicalStage.NeedsAttention,"A later delivery loss puts the certified transport on hold again");
                    var second=service.PreviewPhysicalLoss(cause); Assert(second.Lost == 1 && second.Items.Single().Expected == 28,"A second preview starts from the last agreed quantity");
                    controller.AcceptLoss(second); cargo.stackCount=28;
                    Refuses(delegate { service.PreviewPhysicalLoss(cause); },"Restoring surplus above the certified remainder is refused"); cargo.stackCount=27;
                    var state=(ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core)));
                    var record=state.physical.Single(r => r.cause == cause);
                    Assert(record.cargo.Single(c => !c.journeySupply).count == 30 && record.lossCount == 2 && record.losses[1].previousReceipt == first,"Original manifest is unchanged and two monotonic certificates form a chain");
                    Assert(pawn.GetUniqueLoadID() == ids[3] && cargo.GetUniqueLoadID() == ids[4] && Steel() == long.Parse(ids[12])-3,"Loss acceptance never recreates original pawns or lost material");
                    File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-loss-ids.txt"),new[] {core.Ledger.EntityCount.ToString(),record.losses.Last().receipt});
                    GameDataSaveLoader.SaveGame("INH_LossAccepted");
                }
                else if (GenCommandLine.CommandLineArgPassed("inh-loss-checkpoint"))
                {
                    var saved=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-loss-ids.txt"));
                    var state=(ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core)));
                    Assert(state.physical.Single(r => r.cause == cause).losses.Last().receipt == saved[1] && core.Ledger.EntityCount.ToString() == saved[0],"Committed loss with missing local reference restores the same certificate without added records");
                    Assert(snapshot.Stage == PhysicalStage.InTransit && cargo.stackCount == 27 && Steel() == long.Parse(ids[12])-3,"Checkpoint repair observes the same caravan and certified cargo without moving or replacing it");
                }
                else if (GenCommandLine.CommandLineArgPassed("inh-loss-delivered"))
                {
                    Walk(caravan,destination); PhysicalCaravanAdapter.EnterDestination(core,caravan,destination);
                    var preview=service.PreviewPhysicalLoss(cause); controller.AcceptLoss(preview); controller.AcceptLoss(preview); controller.Deliver(cause);
                    Assert(core.Ledger.EntityCount == before+4 && pawn.Map == destination && cargo.stackCount == 29 && Steel() == long.Parse(ids[12])-1,"Already entered native cargo is certified and delivered once without a second transfer");
                    GameDataSaveLoader.SaveGame("INH_LossManualDelivered");
                }
                else if (GenCommandLine.CommandLineArgPassed("inh-loss-zero") || GenCommandLine.CommandLineArgPassed("inh-loss-zero-final"))
                {
                    if (caravan != null)
                    {
                        cargo.Destroy(); var preview=service.PreviewPhysicalLoss(cause);
                        Assert(preview.Items.Single().Remaining == 0 && preview.Lost == 30,"Completely missing delivery cargo can be explicitly acknowledged as zero");
                        controller.AcceptLoss(preview); controller.Route(cause); Walk(caravan,destination); service.Tick(); controller.Deliver(cause);
                        GameDataSaveLoader.SaveGame("INH_LossZeroDelivered");
                    }
                    Assert(pawn.Spawned && pawn.Map == destination && !pawn.inventory.innerContainer.Any(t => t.GetUniqueLoadID() == ids[4]) && Steel() == long.Parse(ids[12])-30,"Zero-remainder arrival and reload do not replace missing cargo");
                    Assert(service.PhysicalTransports.Single(t => t.Cause == cause).Lost == 30,"Zero delivery retains the full acknowledged loss history");
                }
                else
                {
                    if (caravan != null) { controller.Route(cause); Walk(caravan,destination); service.Tick(); controller.Deliver(cause); GameDataSaveLoader.SaveGame("INH_LossDelivered"); }
                    Assert(pawn.Spawned && pawn.Map == destination && cargo.stackCount == 27 && cargo.GetUniqueLoadID() == ids[4] && Steel() == long.Parse(ids[12])-3,"Certified remainder arrives and survives another process with the original physical IDs");
                    Assert(service.PhysicalTransports.Single(t => t.Cause == cause).Stage == PhysicalStage.Arrived && service.PhysicalTransports.Single(t => t.Cause == cause).Lost == 3,"Arrival retains the certified cumulative loss and original order");
                    int arrived=core.Ledger.EntityCount; controller.Deliver(cause); controller.Recheck(cause); Assert(core.Ledger.EntityCount == arrived,"Repeated final actions create no extra arrival or loss certificates");
                }
                Assert(core.Header.SaveScopeId == ids[0] && core.Ledger.GetEntity(ids[11]).BoundThingLoadId == ids[3],"Permanent person ID and world scope remain unchanged");
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { done=true; Log.Error("[INH-LOSS] FAIL "+e); Application.Quit(1); }
        }
        private void Render(ColonyService service,string cause)
        {
            string language=GenCommandLine.CommandLineArgPassed("inh-loss-japanese") ? "Japanese" : "English";
            string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"TransportLoss-"+language+".png");
            if (window == null)
            {
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"Loss window starts in its requested language");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"translation-errors-Japanese.txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                window=new PhysicalLossWindow(service,cause); if (File.Exists(path)) File.Delete(path); Find.WindowStack.Add(window); return;
            }
            frames++; if (!captured && window.Rendered && frames > 30) { ScreenCapture.CaptureScreenshot(path); captured=true; }
            if (captured && File.Exists(path) && new FileInfo(path).Length > 1000) { Assert(window.Rendered,"Native loss preview repaints and captures "+language); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        }
    }
}
