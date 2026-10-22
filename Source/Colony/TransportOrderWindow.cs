using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    public sealed class TransportOrderWindow : Window
    {
        private readonly ColonyService service;
        public readonly TransportDraft Draft;
        private Vector2 pawnScroll,cargoScroll;
        private readonly Dictionary<string,string> buffers=new Dictionary<string,string>();
        public string CargoSearch = "";
        public bool Rendered { get; private set; }
        public override Vector2 InitialSize { get { return new Vector2(1040,700); } }
        public TransportOrderWindow(ColonyService service)
        { this.service=service; Draft=new TransportDraft(service); doCloseX=true; forcePause=true; absorbInputAroundWindow=true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width-30,32),"INH_ColonyNewPhysical".Translate().ToString());
            Widgets.Label(new Rect(0,36,rect.width,58),"INH_ColonyOrderHelp".Translate().ToString());
            var bases=service.Bases.Where(b => b.Mode == BaseMode.Direct).ToArray();
            float half=(rect.width-16)/2;
            Widgets.Label(new Rect(0,100,half,24),"INH_ColonySource".Translate().ToString());
            Widgets.Label(new Rect(half+16,100,half,24),"INH_ColonyDestination".Translate().ToString());
            string source=bases.Where(b => b.Id == Draft.SourceId).Select(b => b.Name).FirstOrDefault() ?? "INH_ColonyChooseBase".Translate().ToString();
            string destination=bases.Where(b => b.Id == Draft.DestinationId).Select(b => b.Name).FirstOrDefault() ?? "INH_ColonyChooseBase".Translate().ToString();
            if (bases.Length > 0 && Widgets.ButtonText(new Rect(0,126,half,32),source))
                Find.WindowStack.Add(new FloatMenu(bases.Select(b => new FloatMenuOption(b.Name,delegate { Draft.SetSource(b.Id); buffers.Clear(); CargoSearch=""; })).ToList()));
            if (bases.Any(b => b.Id != Draft.SourceId) && Widgets.ButtonText(new Rect(half+16,126,half,32),destination))
                Find.WindowStack.Add(new FloatMenu(bases.Where(b => b.Id != Draft.SourceId).Select(b => new FloatMenuOption(b.Name,delegate { Draft.SetDestination(b.Id); })).ToList()));
            if (bases.Length < 2) Widgets.Label(new Rect(0,164,rect.width,45),"INH_ColonyNeedTwoBases".Translate().ToString());
            else if (Draft.SourceId == null) Widgets.Label(new Rect(0,178,rect.width,45),"INH_ColonyChooseSource".Translate().ToString());
            else
            {
                DrawPeople(new Rect(0,174,half,rect.height-330));
                DrawCargo(new Rect(half+16,174,half,rect.height-330));
            }
            var mass=Draft.Mass;
            Widgets.Label(new Rect(0,rect.height-140,rect.width,30),float.IsNaN(mass.Usage) ? "INH_ColonySelectionChanged".Translate().ToString() :
                "INH_ColonyMass".Translate(mass.Usage.ToString("0.0"),mass.Capacity.ToString("0.0")).ToString() + (mass.Fits ? "" : "  |  " + "INH_ColonyOverweight".Translate().ToString()));
            float days=Draft.FoodDays;
            string food=float.IsNaN(days) ? "INH_ColonyFoodUnknown".Translate().ToString() : days >= DaysWorthOfFoodCalculator.InfiniteDaysWorthOfFood ? "INH_ColonyFoodUnbounded".Translate().ToString() : "INH_ColonyFoodDays".Translate(days.ToString("0.0")).ToString();
            Widgets.Label(new Rect(0,rect.height-108,rect.width,28),food);
            Widgets.Label(new Rect(0,rect.height-78,rect.width,36),"INH_ColonyFoodHint".Translate().ToString());
            Widgets.Label(new Rect(0,rect.height-38,rect.width-200,32),"INH_ColonyAnimalOrderSummary".Translate(Draft.SelectedPeople,Draft.SelectedAnimals,Draft.SelectedItems).ToString());
            bool oldEnabled=GUI.enabled; GUI.enabled=oldEnabled && Draft.CanSubmit;
            if (Widgets.ButtonText(new Rect(rect.width-190,rect.height-38,180,32),"INH_ColonyReserve".Translate().ToString()))
                ColonyOverview.Run(delegate { Draft.Submit(); Close(); });
            GUI.enabled=oldEnabled;
            if (Event.current.type == EventType.Repaint) Rendered=true;
        }
        private void DrawPeople(Rect panel)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_ColonyParty".Translate().ToString());
            var humans=service.AvailableTransportPawns(Draft.SourceId); var animals=service.AvailableTransportAnimals(Draft.SourceId);
            var pawns=humans.Concat(animals).ToArray();
            var area=new Rect(panel.x,panel.y+32,panel.width,panel.height-32);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,pawns.Length*36+40));
            Widgets.BeginScrollView(area,ref pawnScroll,content);
            if (pawns.Length == 0) Widgets.Label(new Rect(0,0,content.width,42),"INH_ColonyNoParty".Translate().ToString());
            float y=0;
            foreach (var pawn in pawns)
            {
                if (animals.Length > 0 && pawn == animals[0]) { Widgets.Label(new Rect(0,y,content.width,28),"INH_ColonyAnimals".Translate().ToString()); y+=32; }
                bool selected=Draft.PawnSelected(pawn);
                Widgets.CheckboxLabeled(new Rect(0,y,content.width,32),pawn.LabelShortCap,ref selected);
                if (selected != Draft.PawnSelected(pawn)) Draft.SelectPawn(pawn,selected); y+=36;
            }
            Widgets.EndScrollView();
        }
        private void DrawCargo(Rect panel)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_ColonyCargo".Translate().ToString());
            Widgets.Label(new Rect(panel.x,panel.y+32,110,28),"INH_ColonySearchCargo".Translate().ToString());
            string search=Widgets.TextField(new Rect(panel.x+114,panel.y+32,panel.width-114,28),CargoSearch);
            if (search != CargoSearch) { CargoSearch=search; cargoScroll=Vector2.zero; }
            var options=service.AvailableTransportCargo(Draft.SourceId).Where(c => c.Thing.LabelCap.ToString().IndexOf(CargoSearch,StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            var area=new Rect(panel.x,panel.y+66,panel.width,panel.height-66);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,options.Length*56+8));
            Widgets.BeginScrollView(area,ref cargoScroll,content);
            if (options.Length == 0) Widgets.Label(new Rect(0,0,content.width,42),"INH_ColonyNoCargo".Translate().ToString());
            float y=0;
            foreach (var option in options)
            {
                var thing=option.Thing; string id=thing.GetUniqueLoadID(); int count=Draft.Quantity(thing);
                string buffer; if (!buffers.TryGetValue(id,out buffer)) buffer=count.ToString();
                Widgets.Label(new Rect(0,y,content.width-82,27),thing.LabelCap.ToString());
                Widgets.TextFieldNumeric<int>(new Rect(content.width-76,y,72,27),ref count,ref buffer,0,option.Available);
                buffers[id]=buffer;
                Widgets.Label(new Rect(0,y+28,content.width-125,24),"INH_ColonyAvailable".Translate(option.Available).ToString());
                bool supply=Draft.Supply(thing);
                if (option.Food) Widgets.CheckboxLabeled(new Rect(content.width-118,y+28,116,24),"INH_ColonyJourneySupply".Translate().ToString(),ref supply);
                if (count != Draft.Quantity(thing) || supply != Draft.Supply(thing)) Draft.SetCargo(thing,count,supply);
                y+=56;
            }
            Widgets.EndScrollView();
        }
    }
}
