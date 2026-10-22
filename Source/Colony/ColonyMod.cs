using System;
using System.Linq;
using Inheritance.Core;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    public sealed class ColonySettings : ModSettings
    {
        public bool physicalEnabled;
        public bool custodyEnabled;
        public bool maintenanceEnabled;
        public bool delegatedEnabled;
        public override void ExposeData()
        { Scribe_Values.Look(ref physicalEnabled,"physicalEnabled",false); Scribe_Values.Look(ref custodyEnabled,"custodyEnabled",false); Scribe_Values.Look(ref maintenanceEnabled,"maintenanceEnabled",false); Scribe_Values.Look(ref delegatedEnabled,"delegatedEnabled",false); }
    }
    public sealed class ColonyMod : Mod
    {
        private static ColonySettings settings;
        public static bool PhysicalEnabled { get { return settings != null && settings.physicalEnabled; } }
        public static bool PhysicalAllowed { get { return PhysicalEnabled || GenCommandLine.CommandLineArgPassed("inh-physical-probe"); } }
        public static bool CustodyEnabled { get { return settings != null && settings.custodyEnabled; } }
        public static bool CustodyAllowed { get { return CustodyEnabled || GenCommandLine.CommandLineArgPassed("inh-colony-custody"); } }
        public static bool MaintenanceEnabled { get { return settings != null && settings.maintenanceEnabled; } }
        public static bool MaintenanceAllowed { get { return MaintenanceEnabled || GenCommandLine.CommandLineArgPassed("inh-delegated-maintenance"); } }
        public static bool DelegatedEnabled { get { return settings != null && settings.delegatedEnabled; } }
        public static bool DelegatedAllowed { get { return DelegatedEnabled || GenCommandLine.CommandLineArgPassed("inh-delegated-custody"); } }
        private static ColonyService current;
        private static CoreWorldComponent currentCore;
        public static ColonyService Current
        { get { if (currentCore == null || Find.World == null || Find.World.GetComponent<CoreWorldComponent>() != currentCore) return null; return current; } }
        public ColonyMod(ModContentPack content) : base(content)
        {
            settings=GetSettings<ColonySettings>();
            CoreWorldComponent.RegisterModuleLifecycle(ColonyService.Actor,1,
                delegate(CoreWorldComponent core, CoreLedger ledger, string payload) { currentCore = core; current = new ColonyService(core,ledger,payload); },
                delegate(CoreWorldComponent core) { if (core != currentCore || current == null) throw new InvalidOperationException("Colony runtime is missing"); return current.SaveSnapshot(core); },
                delegate(CoreWorldComponent core) { if (core == currentCore && current != null) current.Tick(); });
        }
        public override string SettingsCategory() { return "INH_ColonyTitle".Translate().ToString(); }
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard(); listing.Begin(rect);
            listing.Label("INH_ColonyPrototype".Translate().ToString());
            bool wasEnabled=settings.physicalEnabled;
            listing.CheckboxLabeled("INH_ColonyEnablePhysical".Translate().ToString(),ref settings.physicalEnabled,"INH_ColonyEnablePhysicalHint".Translate().ToString());
            bool wasCustodyEnabled=settings.custodyEnabled;
            listing.CheckboxLabeled("INH_ColonyEnableCustody".Translate().ToString(),ref settings.custodyEnabled,"INH_ColonyCustodyFrozenWarning".Translate().ToString());
            listing.Label("INH_ColonyCustodyFrozenWarning".Translate().ToString());
            bool wasMaintenanceEnabled=settings.maintenanceEnabled;
            listing.CheckboxLabeled("INH_MaintenanceEnable".Translate().ToString(),ref settings.maintenanceEnabled,"INH_MaintenanceEnableHint".Translate().ToString());
            bool wasDelegatedEnabled=settings.delegatedEnabled;
            listing.CheckboxLabeled("INH_DelegatedEnable".Translate().ToString(),ref settings.delegatedEnabled,"INH_DelegatedEnableHint".Translate().ToString());
            if (wasEnabled != settings.physicalEnabled || wasCustodyEnabled != settings.custodyEnabled || wasMaintenanceEnabled != settings.maintenanceEnabled || wasDelegatedEnabled != settings.delegatedEnabled) WriteSettings();
            var service = Current;
            if (service != null && currentCore.IsReady)
            {
                if (listing.ButtonText("INH_ColonyRegisterMaps".Translate().ToString()))
                    foreach (var map in Find.Maps.Where(m => m.ParentFaction == RimWorld.Faction.OfPlayer)) service.RegisterDirectMap(map);
                if (listing.ButtonText("INH_ColonyOverview".Translate().ToString())) Find.WindowStack.Add(new ColonyOverview(service));
                if (settings.physicalEnabled && listing.ButtonText("INH_ColonyNewPhysical".Translate().ToString())) Find.WindowStack.Add(new TransportOrderWindow(service));
                if ((settings.custodyEnabled || service.HasLocalCustody) && listing.ButtonText("INH_ColonyCustody".Translate().ToString())) Find.WindowStack.Add(new CustodyWindow(service));
                if ((settings.maintenanceEnabled || service.HasDelegatedMaintenance) && listing.ButtonText("INH_MaintenanceTitle".Translate().ToString())) Find.WindowStack.Add(new DelegatedMaintenanceWindow(service));
                if ((settings.delegatedEnabled || service.HasDelegatedCustody) && listing.ButtonText("INH_DelegatedCustodyTitle".Translate().ToString())) Find.WindowStack.Add(new DelegatedCustodyWindow(service));
            }
            listing.End();
        }
    }
    public sealed class ColonyOverview : Window
    {
        private readonly ColonyService service;
        private Vector2 scroll;
        public override Vector2 InitialSize { get { return new Vector2(860,620); } }
        public ColonyOverview(ColonyService service) { this.service = service; doCloseX = true; forcePause = true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width,32),"INH_ColonyOverview".Translate().ToString());
            float actionX=0;
            if (ColonyMod.PhysicalEnabled)
            {
                if (Widgets.ButtonText(new Rect(actionX,36,190,30),"INH_ColonyNewPhysical".Translate().ToString())) Find.WindowStack.Add(new TransportOrderWindow(service));
                actionX+=200;
            }
            bool showCustody=ColonyMod.CustodyEnabled || service.HasLocalCustody;
            if (showCustody && Widgets.ButtonText(new Rect(actionX,36,190,30),"INH_ColonyCustody".Translate().ToString())) Find.WindowStack.Add(new CustodyWindow(service));
            if (showCustody) actionX+=200;
            bool showMaintenance=ColonyMod.MaintenanceEnabled || service.HasDelegatedMaintenance;
            if (showMaintenance && Widgets.ButtonText(new Rect(actionX,36,190,30),"INH_MaintenanceTitle".Translate().ToString())) Find.WindowStack.Add(new DelegatedMaintenanceWindow(service));
            if (showMaintenance) actionX+=200;
            bool showDelegated=ColonyMod.DelegatedEnabled || service.HasDelegatedCustody;
            if (showDelegated && Widgets.ButtonText(new Rect(actionX,36,190,30),"INH_DelegatedCustodyTitle".Translate().ToString())) Find.WindowStack.Add(new DelegatedCustodyWindow(service));
            var bases = service.Bases.ToArray(); var jobs = service.Transports.ToArray(); var physical = service.PhysicalTransports.ToArray();
            float listTop=ColonyMod.PhysicalEnabled || showCustody || showMaintenance || showDelegated ? 76 : 40;
            Rect area = new Rect(0,listTop,rect.width,rect.height-listTop), content = new Rect(0,0,rect.width-24,Math.Max(rect.height-listTop,bases.Length*30+jobs.Length*30+physical.Length*152+60));
            Widgets.BeginScrollView(area,ref scroll,content); float y = 0;
            foreach (var b in bases)
            {
                string detail = b.Mode == BaseMode.Direct ? "  |  " + "INH_ColonyMapStock".Translate(b.Colonists,b.PhysicalSteel).ToString() : "";
                Widgets.Label(new Rect(0,y,content.width,30),b.Name + "  |  " + (b.Mode == BaseMode.Direct ? "INH_ColonyDirect" : "INH_ColonyDelegated").Translate().ToString() + detail); y += 30;
            }
            y += 15;
            if (jobs.Length == 0 && physical.Length == 0) Widgets.Label(new Rect(0,y,content.width,30),"INH_ColonyNoTransport".Translate().ToString());
            foreach (var job in jobs)
            {
                string from = bases.Single(b => b.Id == job.SourceId).Name, to = bases.Single(b => b.Id == job.DestinationId).Name;
                Widgets.Label(new Rect(0,y,content.width,30),from + " → " + to + "  |  " + job.Amount + "  |  " + ("INH_Transport_" + job.Stage).Translate().ToString()); y += 30;
            }
            foreach (var job in physical)
            {
                string from=bases.Single(b => b.Id == job.SourceId).Name, to=bases.Single(b => b.Id == job.DestinationId).Name;
                Widgets.Label(new Rect(0,y,content.width,42),"INH_ColonyPhysicalRow".Translate(from,to,job.People,job.Cargo,("INH_Physical_" + job.Stage).Translate().ToString()).ToString()); y+=42;
                if (job.Animals > 0) { Widgets.Label(new Rect(0,y,content.width,24),"INH_ColonyAnimalParty".Translate(job.Animals).ToString()); y+=24; }
                if (job.Lost > 0) { Widgets.Label(new Rect(0,y,content.width,24),"INH_ColonyLossAccepted".Translate(job.Lost).ToString()); y+=24; }
                if (job.Problem != null) { Widgets.Label(new Rect(0,y,content.width,30),"INH_ColonyPhysicalAttention".Translate().ToString()); y+=30; }
                DrawPhysicalActions(job,content.width,ref y);
            }
            Widgets.EndScrollView();
        }
        private void DrawPhysicalActions(PhysicalTransportSnapshot job,float width,ref float y)
        {
            if (!ColonyMod.PhysicalEnabled) return;
            var controller=new TransportCommandController(service); float x=0;
            if (job.Stage == PhysicalStage.Reserved || job.Stage == PhysicalStage.NeedsAttention && job.CaravanId == null)
            {
                if (Widgets.ButtonText(new Rect(x,y,140,28),"INH_ColonyDispatch".Translate().ToString())) Run(delegate { controller.Dispatch(job.Cause); }); x+=150;
                if (Widgets.ButtonText(new Rect(x,y,140,28),"INH_ColonyCancel".Translate().ToString())) Run(delegate { controller.Cancel(job.Cause); });
            }
            else if (job.Stage == PhysicalStage.InTransit)
            {
                if (Widgets.ButtonText(new Rect(x,y,140,28),"INH_ColonyResumeRoute".Translate().ToString())) Run(delegate { controller.Route(job.Cause); }); x+=150;
                if (Widgets.ButtonText(new Rect(x,y,140,28),"INH_ColonyEnter".Translate().ToString())) Run(delegate { controller.Deliver(job.Cause); });
            }
            else if (job.Stage == PhysicalStage.NeedsAttention && job.CaravanId != null)
            {
                if (Widgets.ButtonText(new Rect(x,y,140,28),"INH_ColonyRecheck".Translate().ToString())) Run(delegate { controller.Recheck(job.Cause); }); x+=150;
                if (Widgets.ButtonText(new Rect(x,y,180,28),"INH_ColonyInspectLoss".Translate().ToString())) Run(delegate { Find.WindowStack.Add(new PhysicalLossWindow(service,job.Cause)); });
            }
            y+=32;
        }
        internal static void Run(Action action)
        {
            try { action(); }
            catch (InvalidOperationException e)
            { Log.Message("[Inheritance-Colony] Command refused: " + e.Message); Find.WindowStack.Add(new Dialog_MessageBox("INH_ColonyCommandRefused".Translate().ToString())); }
        }
    }
}
