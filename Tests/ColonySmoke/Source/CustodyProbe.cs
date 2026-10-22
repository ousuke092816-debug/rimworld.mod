using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Development proof only. This holder is never distributed with the playable Colony module.
    // Production delegated custody needs a Core-owned save type, transfer journal and simulation authority.
    public sealed class CustodyProbe : GameComponent,IThingHolder
    {
        private ThingOwner<Pawn> stored;
        private bool done;
        public CustodyProbe(Game game) { stored=new ThingOwner<Pawn>(this); }
        public override void FinalizeInit()
        {
            // Pause before the first gameplay frame, so interval-ticked animal aging is
            // not mistaken for a storage/reload mutation by this stationary proof.
            if (GenCommandLine.CommandLineArgPassed("inh-custody")) Find.TickManager.Pause();
        }
        public IThingHolder ParentHolder { get { return null; } }
        public ThingOwner GetDirectlyHeldThings() { return stored; }
        public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children,stored); }
        public override void ExposeData()
        {
            Scribe_Deep.Look(ref stored,"proofStoredPawns",this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && stored == null) stored=new ThingOwner<Pawn>(this);
        }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-custody-"+key+".txt"); }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-CUSTODY] PASS "+text); }
        private static string PawnState(Pawn p)
        {
            return string.Join("|",new[] {p.GetUniqueLoadID(),p.def.defName,p.Name.ToStringFull,p.gender.ToString(),p.Faction.GetUniqueLoadID(),p.ageTracker.AgeBiologicalTicks.ToString(),
                p.skills == null ? "" : string.Join(",",p.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+s.xpSinceLastLevel.ToString("R")).ToArray()),
                string.Join(",",p.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R")+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                p.genes == null ? "" : string.Join(",",p.genes.GenesListForReading.Select(g => g.def.defName+":"+g.Active).ToArray()),
                p.Ideo == null ? "" : p.Ideo.GetUniqueLoadID(),
                p.apparel == null ? "" : string.Join(",",p.apparel.WornApparel.Select(t => t.GetUniqueLoadID()).ToArray()),
                p.equipment == null ? "" : string.Join(",",p.equipment.AllEquipmentListForReading.Select(t => t.GetUniqueLoadID()).ToArray()),
                string.Join(",",p.inventory.innerContainer.Select(t => t.GetUniqueLoadID()+":"+t.def.defName+":"+t.stackCount+":"+t.HitPoints).OrderBy(s => s,StringComparer.Ordinal).ToArray())});
        }
        private static bool SamePawn(Pawn p,string expected)
        {
            var a=PawnState(p).Split('|'); var b=expected.Split('|');
            bool same=a.Length == b.Length && a.Where((s,i) => i != 5).SequenceEqual(b.Where((s,i) => i != 5)) && long.Parse(a[5])-long.Parse(b[5]) >= 0 && long.Parse(a[5])-long.Parse(b[5]) <= 3;
            if (!same) for (int i=0;i<Math.Min(a.Length,b.Length);i++) if (a[i] != b[i]) Log.Message("[INH-CUSTODY] DIFF "+p.GetUniqueLoadID()+" field="+i+" expected="+b[i]+" actual="+a[i]);
            return same;
        }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("inh-custody") || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            done=true;
            try
            {
                Assert(core.IsReady && ColonyMod.Current != null,"Native custody proof preserves Core plus Colony lifecycle"); Find.TickManager.Pause();
                if (GenCommandLine.CommandLineArgPassed("inh-custody-save"))
                {
                    var journey=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-journey-ids.txt"));
                    var pawns=new[] {journey[1],journey[2]}.Select(id => Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Single(p => p.GetUniqueLoadID() == id)).ToArray();
                    Assert(stored.Count == 0 && pawns.All(p => p.Spawned && p.inventory != null),"Original delivered person and animal are available for the storage proof");
                    File.WriteAllLines(PathFor("states"),pawns.Select(PawnState).ToArray());
                    File.WriteAllLines(PathFor("ids"),new[] {core.Header.SaveScopeId,core.Ledger.EntityCount.ToString(),pawns[0].Map.uniqueID.ToString(),pawns[0].GetUniqueLoadID(),pawns[1].GetUniqueLoadID()});
                    File.WriteAllLines(PathFor("bindings"),pawns.Select(p => core.Ledger.Entities.Single(e => e.BoundThingLoadId == p.GetUniqueLoadID()).Id).ToArray());
                    foreach (Pawn pawn in pawns)
                    {
                        pawn.DeSpawn(); if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                        if (!stored.TryAdd(pawn,false)) throw new InvalidOperationException("Proof holder refused the original pawn");
                    }
                    Assert(stored.Count == 2 && pawns.All(p => !p.Spawned && p.holdingOwner == stored && !Find.WorldPawns.Contains(p)),"Original pawns have one deep storage owner and no world-pawn duplicate");
                    Assert(pawns.Select(PawnState).SequenceEqual(File.ReadAllLines(PathFor("states"))) && core.Ledger.EntityCount.ToString() == File.ReadAllLines(PathFor("ids"))[1],"Storage moves existing objects without changing possessions or Core identities");
                    GameDataSaveLoader.SaveGame("INH_CustodyStored");
                }
                else
                {
                    var ids=File.ReadAllLines(PathFor("ids")); var states=File.ReadAllLines(PathFor("states")); var bindings=File.ReadAllLines(PathFor("bindings"));
                    Assert(core.Header.SaveScopeId == ids[0] && core.Ledger.EntityCount == int.Parse(ids[1]) && core.Ledger.GetEntity(bindings[0]).BoundThingLoadId == ids[3] && core.Ledger.GetEntity(bindings[1]).BoundThingLoadId == ids[4],"Core references resolve to the same stored person and animal after process restart");
                    Map map=Find.Maps.Single(m => m.uniqueID.ToString() == ids[2]);
                    if (GenCommandLine.CommandLineArgPassed("inh-custody-load"))
                    {
                        Assert(stored.Count == 2 && stored.Cast<Pawn>().Select(p => p.GetUniqueLoadID()).SequenceEqual(ids.Skip(3)),"Deep storage retains both original native IDs");
                        Assert(stored.Cast<Pawn>().Select((p,i) => SamePawn(p,states[i])).All(v => v),"Stored identity, skills, wounds, genes, ideology, equipment and inventory survive native Scribe");
                        Assert(stored.Cast<Pawn>().All(p => !Find.WorldPawns.Contains(p) && !p.Spawned),"Stored pawns are absent from map and world-pawn simulation lists");
                        foreach (Pawn pawn in stored.Cast<Pawn>().ToArray())
                        {
                            if (!stored.Remove(pawn)) throw new InvalidOperationException("Proof holder refused release");
                            GenSpawn.Spawn(pawn,map.AllCells.First(c => c.Standable(map) && c.GetThingList(map).Count == 0),map);
                        }
                        Assert(stored.Count == 0 && map.mapPawns.AllPawns.Count(p => ids.Skip(3).Contains(p.GetUniqueLoadID())) == 2,"Release returns the exact stored pawns to the map without creating copies");
                        GameDataSaveLoader.SaveGame("INH_CustodyReturned");
                    }
                    else
                    {
                        var pawns=ids.Skip(3).Select(id => map.mapPawns.AllPawns.Single(p => p.GetUniqueLoadID() == id)).ToArray();
                        Assert(stored.Count == 0 && pawns.Select((p,i) => SamePawn(p,states[i])).All(v => v),"Returned reload retains original animals, people and all original possessions");
                        Assert(core.Ledger.EntityCount == int.Parse(ids[1]),"Storage proof creates no new Core person, animal, cargo or receipt");
                    }
                }
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { Log.Error("[INH-CUSTODY] FAIL "+e); Application.Quit(1); }
        }
    }
}
