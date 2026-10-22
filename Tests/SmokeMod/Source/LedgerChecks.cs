using System;
using System.IO;
using System.Linq;
using System.Threading;
using Inheritance.Core;
using Verse;

namespace Inheritance.Smoke
{
    internal static class LedgerChecks
    {
        private const string Actor = "inheritance.core", Item = "ludeon.rimworld:Steel";
        internal static void Require(bool okay, string message)
        { if (!okay) throw new InvalidOperationException(message); Log.Message("[INH-LEDGER] PASS " + message); }
        private static void Refuses(Action action, string message)
        { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Require(refused,message); }
        private static string Create(CoreLedger l, string cause, EntitySpec spec)
        { var op = l.PrepareCreate(l.NewOperationId(Actor),cause,spec); return l.Commit(op.Id).ResultId; }
        private static EntitySnapshot Named(CoreLedger l, string name)
        { return l.Entities.Single(x => x.Status == EntityStatus.Active && x.Name == name && x.LineageId == null); }
        private static string Meta(string name) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"expected-" + name + ".txt"); }
        internal static void Populate(CoreWorldComponent c)
        {
            var l = c.Ledger;
            string home = Create(l,"home",new EntitySpec("polity","Home"));
            string rival = Create(l,"rival",new EntitySpec("polity","Rival"));
            string community = Create(l,"community",new EntitySpec("community","Settlers",affiliationId:home));
            string origin = Create(l,"origin",new EntitySpec("city","Origin",cultureKey:"star-tomb",controllerId:home,ownerId:home));
            string destination = Create(l,"destination",new EntitySpec("city","Destination",controllerId:home,ownerId:home));
            string convoy = Create(l,"convoy",new EntitySpec("shipment","Convoy",ownerId:home));
            string lot = Create(l,"stock",new EntitySpec("lot","Stock",ownerId:home,custodyId:origin,quantity:100,itemKey:Item));
            string person = Create(l,"delegate",new EntitySpec("person","Delegate",communityId:community,affiliationId:home,custodyId:origin,identityKey:"delegate-one"));
            var pawn = Find.CurrentMap.mapPawns.FreeColonists.First();
            string physical = Create(l,"resident",new EntitySpec("person","Resident",custodyId:origin,authority:SimulationAuthority.Physical,binding:pawn));
            File.WriteAllText(Meta("pawn"),pawn.GetUniqueLoadID());
            Refuses(delegate { l.PrepareMove(l.NewOperationId(Actor),"physical-move",physical,destination,l.GetEntity(physical).Revision); },"Physical asset transfer requires an adapter");
            string released = l.PrepareReservation(l.NewOperationId(Actor),"reserve-release",lot,10,l.GetEntity(lot).Revision).Id;
            string releaseId = l.Commit(released).ResultId;
            var release = l.PrepareRelease(l.NewOperationId(Actor),"release",releaseId); l.Commit(release.Id); l.Commit(release.Id);
            Require(l.Available(lot) == 100,"Release is idempotent and restores availability");
            var occupation = l.PrepareControllerChange(l.NewOperationId(Actor),"occupation",origin,rival,l.GetEntity(origin).Revision,100);
            l.Commit(occupation.Id); l.Commit(occupation.Id);
            Require(l.GetEntity(origin).ControllerId == rival && l.GetEntity(origin).OwnerId == home && l.GetEntity(origin).CultureKey == "star-tomb" && l.GetEntity(origin).ControllerChangeCount == 1,"Controller changes once while culture and ownership persist");
            var held = l.GetEntity(lot);
            var reserve = l.PrepareReservation(l.NewOperationId(Actor),"reserve-30",lot,30,held.Revision);
            Require(l.PrepareReservation(reserve.Id,"reserve-30",lot,30,held.Revision).ResultId == reserve.ResultId,"Identical preparation reuses one result ID");
            Refuses(delegate { l.PrepareReservation(reserve.Id,"reserve-30",lot,31,held.Revision); },"Changed payload cannot reuse operation ID");
            Refuses(delegate { l.PrepareReservation(l.NewOperationId(Actor),"reserve-30",lot,30,held.Revision); },"Business cause cannot execute under another operation ID");
            l.Commit(reserve.Id); l.Commit(reserve.Id);
            Require(l.Available(lot) == 70 && l.Reserved(lot) == 30 && held.Revision < l.GetEntity(lot).Revision && held.Quantity == 100,"Reservation is within stock and old snapshot stays detached");
            Refuses(delegate { l.PrepareReservation(l.NewOperationId(Actor),"overdraw",lot,80,l.GetEntity(lot).Revision); },"Over-reservation is refused");
            Refuses(delegate { l.PrepareMove(l.NewOperationId(Actor),"reserved-move",lot,destination,l.GetEntity(lot).Revision); },"Reserved lot cannot be moved as free stock");
            var abort = l.PrepareCreate(l.NewOperationId(Actor),"abort",new EntitySpec("unit","NeverCreated")); l.Abort(abort.Id);
            Refuses(delegate { l.Commit(abort.Id); },"Aborted operation cannot commit or reuse allocated ID");
            Require(l.GetEntity(abort.ResultId).Status == EntityStatus.Allocated,"Aborted result ID remains registered and unused");
            var preparedPerson = l.PrepareMove(l.NewOperationId(Actor),"person-move",person,destination,l.GetEntity(person).Revision);
            File.WriteAllText(Meta("person-op"),preparedPerson.Id);
            var depart = l.PrepareDeparture(l.NewOperationId(Actor),"departure",reserve.ResultId,convoy); l.BeginApplying(depart.Id);
            File.WriteAllText(Meta("departure"),depart.Id);
            Refuses(delegate { l.PrepareRelease(l.NewOperationId(Actor),"conflict",reserve.ResultId); },"Pending departure locks stock and reservation");
            Exception threadError = null;
            var thread = new Thread(delegate() { try { l.NewOperationId(Actor); } catch (Exception e) { threadError = e; } }); thread.Start(); thread.Join();
            Require(threadError is InvalidOperationException,"Background-thread writes are rejected");
            Require(l.Inventory(origin,Item) == 100 && l.Inventory(convoy,Item) == 0 && l.TotalAbstractQuantity(Item) == 100,"Applying save still contains only the before state");
        }
        internal static void Resume(CoreWorldComponent c)
        {
            var l = c.Ledger; string dep = File.ReadAllText(Meta("departure"));
            Require(l.GetOperation(dep).Stage == OperationStage.Prepared && l.GetOperation(dep).RecoveryCount == 1,"Applying journal recovers without guessing or duplication");
            Require(l.GetOperation(File.ReadAllText(Meta("person-op"))).Stage == OperationStage.Prepared,"Prepared operation survives a process restart");
            CheckBinding(l);
            string child = l.Commit(dep).ResultId; l.Commit(dep);
            Require(l.GetEntity(child).Quantity == 30 && l.GetEntity(child).CustodyId == Named(l,"Convoy").Id && Named(l,"Stock").Quantity == 70,"Departure splits stock once into convoy custody");
            var move = l.GetOperation(File.ReadAllText(Meta("person-op"))); l.Commit(move.Id); l.Commit(move.Id);
            Require(Named(l,"Delegate").CustodyId == Named(l,"Destination").Id && Named(l,"Delegate").AffiliationId == Named(l,"Home").Id,"Person movement preserves identity and affiliation");
            var arrival = l.PrepareMove(l.NewOperationId(Actor),"arrival",child,Named(l,"Destination").Id,l.GetEntity(child).Revision); l.Commit(arrival.Id); l.Commit(arrival.Id);
            File.WriteAllText(Meta("arrival"),arrival.Id); File.WriteAllText(Meta("child"),child);
            Require(l.TotalAbstractQuantity(Item) == 100 && l.Inventory(Named(l,"Origin").Id,Item) == 70 && l.Inventory(Named(l,"Destination").Id,Item) == 30 && l.Inventory(Named(l,"Convoy").Id,Item) == 0,"Arrival preserves exactly 100 total stock");
            string id = l.NewOperationId(Actor); File.WriteAllText(Meta("last-id"),id);
        }
        private static void CheckBinding(CoreLedger l)
        {
            string expected = File.ReadAllText(Meta("pawn")); var e = Named(l,"Resident");
            Require(e.Authority == SimulationAuthority.Physical && e.BoundThingLoadId == expected &&
                Find.CurrentMap.mapPawns.AllPawns.Count(x => x.GetUniqueLoadID() == expected) == 1,"One original pawn reference survives Scribe without cloning");
        }
        internal static void VerifyCompleted(CoreWorldComponent c)
        {
            var l = c.Ledger; CheckBinding(l);
            string child = File.ReadAllText(Meta("child")); var arrival = l.GetOperation(File.ReadAllText(Meta("arrival")));
            Require(arrival.Stage == OperationStage.Committed,"Committed journal survives a second process restart");
            l.Commit(arrival.Id); l.Commit(File.ReadAllText(Meta("departure")));
            Require(l.GetEntity(child).CustodyId == Named(l,"Destination").Id && l.TotalAbstractQuantity(Item) == 100 && Named(l,"Stock").Quantity == 70,"Replayed completed transfers preserve IDs and quantity");
            string old = File.ReadAllText(Meta("last-id")), next = l.NewOperationId(Actor);
            Require(long.Parse(next.Split(':').Last()) > long.Parse(old.Split(':').Last()),"ID counter never rewinds across two saves");
            Require(Named(l,"Origin").ControllerId == Named(l,"Rival").Id && Named(l,"Origin").ControllerChangeCount == 1,"Controller history persists through two saves");
        }
        internal static void VerifyReconciliation(CoreWorldComponent c)
        {
            var l = c.Ledger; string dep = File.ReadAllText(Meta("departure"));
            Require(l.GetOperation(dep).Stage == OperationStage.NeedsReconciliation && Named(l,"Stock").Quantity == 99 && l.Inventory(Named(l,"Convoy").Id,Item) == 0,"Divergent before state is held for reconciliation without inventing cargo");
            Refuses(delegate { l.Commit(dep); },"Unreconciled operation cannot commit");
            Refuses(delegate { l.PrepareMove(l.NewOperationId(Actor),"reconcile-lock",Named(l,"Stock").Id,Named(l,"Convoy").Id,Named(l,"Stock").Revision); },"Reconciliation retains affected asset locks");
        }
    }
}
