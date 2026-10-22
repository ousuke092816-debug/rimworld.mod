using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace Inheritance.Core
{
    public sealed class ModuleDef : Def
    {
        public string packageId;
        public int protocolVersion = 2;
        public int moduleSchemaVersion = 1;
    }

    public sealed class CoreSettings : ModSettings
    {
        public bool verboseDiagnostics;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref verboseDiagnostics, "verboseDiagnostics", false);
        }
    }

    public sealed class CoreMod : Mod
    {
        internal static int MainThreadId;
        private readonly CoreSettings settings;
        public CoreMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<CoreSettings>();
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Log.Message("[Inheritance] Core 0.4.16 loaded; protocol=2; modules=" +
                    DefDatabase<ModuleDef>.AllDefs.Count());
                if (settings.verboseDiagnostics)
                    Log.Message("[Inheritance] DLC active: Royalty=" + ModsConfig.RoyaltyActive +
                        ", Ideology=" + ModsConfig.IdeologyActive + ", Biotech=" + ModsConfig.BiotechActive +
                        ", Odyssey=" + ModsConfig.OdysseyActive);
            });
        }
        public override string SettingsCategory() { return "INH_CoreTitle".Translate(); }
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("INH_Prototype".Translate());
            listing.Gap();
            listing.Label("INH_Modules".Translate(DefDatabase<ModuleDef>.AllDefs.Count()));
            listing.CheckboxLabeled("INH_Diagnostics".Translate(), ref settings.verboseDiagnostics);
            var world = Find.World;
            var component = world == null ? null : world.GetComponent<CoreWorldComponent>();
            listing.Label(component == null ? "INH_NoWorld".Translate() :
                "INH_SaveScope".Translate(component.Header.SaveScopeId));
            listing.End();
        }
    }
}
