using System;
using System.Linq;
using Inheritance.Core;
using UnityEngine;
using Verse;

namespace Inheritance.World
{
    public sealed class WorldSettings : ModSettings
    {
        public bool enableForNewWorld;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref enableForNewWorld,"enableForNewWorld",false); }
    }
    public sealed class WorldMod : Mod
    {
        private readonly WorldSettings settings;
        private static WorldStateComponent currentWorld;
        public static WorldStateComponent CurrentWorld
        { get { if (currentWorld != null && currentWorld.World != Find.World) currentWorld = null; return currentWorld; } }
        public WorldMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WorldSettings>();
            CoreWorldComponent.RegisterWorldBootstrap(delegate { return settings.enableForNewWorld || GenCommandLine.CommandLineArgPassed("inh-world-prototype") ? "six-nations" : "vanilla"; },
                delegate(CoreWorldComponent core, CoreLedger ledger) { currentWorld = new WorldStateComponent(Find.World); currentWorld.Bootstrap(core,ledger); },
                delegate(CoreWorldComponent core) { var current = CurrentWorld; if (current == null) throw new InvalidOperationException("World runtime is missing"); return current.SaveSnapshot(core); },
                delegate(CoreWorldComponent core) { if (core.Header.WorldMode != "six-nations") currentWorld = null; else if (CurrentWorld != null) CurrentWorld.Tick(); });
        }
        public override string SettingsCategory() { return "INH_WorldTitle".Translate().ToString(); }
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard(); listing.Begin(rect);
            listing.CheckboxLabeled("INH_WorldNewWorld".Translate().ToString(),ref settings.enableForNewWorld);
            listing.Label("INH_WorldScope".Translate().ToString()); listing.Gap();
            var component = CurrentWorld;
            if (component != null && component.IsReady && listing.ButtonText("INH_WorldOverview".Translate().ToString())) Find.WindowStack.Add(new OverviewWindow(component));
            listing.End();
        }
    }
    public sealed class OverviewWindow : Window
    {
        private readonly WorldStateComponent component;
        private readonly Texture2D map;
        private Vector2 scroll;
        private static readonly Color[] Colors = { new Color(.6f,.5f,.75f), new Color(.25f,.65f,.35f), new Color(.8f,.38f,.18f), new Color(.7f,.75f,.85f), new Color(.15f,.55f,.8f), new Color(.85f,.7f,.25f) };
        public override Vector2 InitialSize { get { return new Vector2(1000,740); } }
        public OverviewWindow(WorldStateComponent component)
        {
            this.component = component; doCloseX = true; forcePause = true; absorbInputAroundWindow = true;
            map = new Texture2D(720,300,TextureFormat.RGBA32,false);
            var background = Enumerable.Repeat(new Color(.1f,.13f,.19f),720*300).ToArray(); map.SetPixels(background);
            foreach (var r in component.Regions)
            {
                int x = Math.Max(0,Math.Min(719,(int)((Math.Atan2(r.Z,r.X)+Math.PI)/(2*Math.PI)*719)));
                int y = Math.Max(0,Math.Min(299,(int)((Math.Asin(Math.Max(-1,Math.Min(1,r.Y)))+Math.PI/2)/Math.PI*299)));
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (x+dx>=0 && x+dx<720 && y+dy>=0 && y+dy<300) map.SetPixel(x+dx,y+dy,Colors[r.NationIndex]);
            }
            map.Apply();
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width,30),"INH_WorldOverview".Translate().ToString());
            Widgets.DrawTextureFitted(new Rect(0,35,720,260),map,1f);
            Widgets.Label(new Rect(735,35,220,240),"INH_WorldMapNote".Translate().ToString());
            var nations = component.Nations.ToArray(); var cities = component.Cities.ToArray();
            Rect view = new Rect(0,305,rect.width,rect.height-305), content = new Rect(0,0,rect.width-24,Math.Max(rect.height-305,cities.Length*25+nations.Length*42));
            Widgets.BeginScrollView(view,ref scroll,content);
            float y = 0;
            for (int i = 0; i < nations.Length; i++)
            {
                var n = nations[i]; Widgets.DrawBoxSolid(new Rect(0,y+6,14,14),Colors[i]);
                var capital = cities.FirstOrDefault(c => c.Id == n.CapitalCityId);
                Widgets.Label(new Rect(22,y,content.width-22,28),n.Name + "  |  " + "INH_WorldPeace".Translate().ToString() + "  |  " + "INH_WorldCapital".Translate().ToString() + ": " + (capital == null ? "—" : capital.Name) + (n.TemporaryCapital ? " (" + "INH_WorldTemporary".Translate().ToString() + ")" : "") + "  |  " + "INH_WorldIncome".Translate().ToString() + ": " + (n.AbstractIncomeTicks/60000d).ToString("0.##")); y += 30;
                foreach (var c in cities.Where(c => c.ControllerId == n.Id).OrderByDescending(c => c.Level))
                { Widgets.Label(new Rect(22,y,content.width-22,25),"L" + c.Level + "  " + c.Name + "  |  " + "INH_WorldLocation".Translate(c.Tile).ToString() + "  |  " + ("INH_Culture_" + c.CultureKey).Translate().ToString()); y += 25; }
                y += 12;
            }
            Widgets.EndScrollView();
        }
        public override void PostClose() { base.PostClose(); UnityEngine.Object.Destroy(map); }
    }
}
