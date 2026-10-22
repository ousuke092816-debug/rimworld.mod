using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Inheritance.Core
{
    // Only this Core-owned class appears in Scribe. Optional module DTOs remain strings.
    internal sealed class ModulePayload : IExposable
    {
        internal string owner, data;
        internal int version;
        public ModulePayload() { }
        public void ExposeData()
        { Scribe_Values.Look(ref owner,"owner"); Scribe_Values.Look(ref version,"version",-1,true); Scribe_Values.Look(ref data,"data"); }
    }
    internal sealed class ModuleLifecycle
    {
        internal string owner;
        internal int version;
        internal Action<CoreWorldComponent,CoreLedger,string> restore;
        internal Func<CoreWorldComponent,string> save;
        internal Action<CoreWorldComponent> update;
    }
    public sealed partial class CoreWorldComponent
    {
        private static readonly List<ModuleLifecycle> lifecycles = new List<ModuleLifecycle>();
        private List<ModulePayload> modulePayloads = new List<ModulePayload>();
        private int modulePayloadCount;
        private bool modulesValidated;
        private string bootstrapActor;
        public static void RegisterModuleLifecycle(string owner, int version,
            Action<CoreWorldComponent,CoreLedger,string> restore, Func<CoreWorldComponent,string> save, Action<CoreWorldComponent> update = null)
        {
            if (string.IsNullOrEmpty(owner) || owner == "inheritance.core" || owner == "inheritance.world" || version < 1 ||
                restore == null || save == null || lifecycles.Any(x => x.owner == owner)) throw new InvalidOperationException("Invalid module lifecycle registration");
            lifecycles.Add(new ModuleLifecycle { owner = owner, version = version, restore = restore, save = save, update = update });
        }
        private void ValidateModulePayloads(ModuleCatalogue catalogue)
        {
            if (modulePayloads == null || loaded && header.SchemaVersion >= 4 && modulePayloadCount != modulePayloads.Count)
                throw new InvalidOperationException("Module payload table is missing or incomplete");
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in modulePayloads)
            {
                if (p == null || !owners.Add(p.owner) || string.IsNullOrEmpty(p.data)) throw new InvalidOperationException("Invalid module payload record");
                catalogue.Require(p.owner);
                var callback = lifecycles.SingleOrDefault(x => x.owner == p.owner);
                if (callback == null || callback.version != p.version || !header.ModuleStamps.Any(x => x.PackageId == p.owner && x.SchemaVersion == p.version))
                    throw new InvalidOperationException("Required module save adapter or schema is missing: " + p.owner);
            }
            foreach (var callback in lifecycles)
            {
                catalogue.Require(callback.owner);
                if (!catalogue.Stamps().Any(x => x.PackageId == callback.owner && x.SchemaVersion == callback.version))
                    throw new InvalidOperationException("Module lifecycle version disagrees with its declaration");
            }
        }
        private void RestoreModules()
        {
            foreach (var callback in lifecycles.OrderBy(x => x.owner,StringComparer.Ordinal))
            {
                var payload = modulePayloads.SingleOrDefault(x => x.owner == callback.owner);
                bootstrapActor = callback.owner; bootstrapping = true;
                try { callback.restore(this,ledger,payload == null ? null : payload.data); }
                finally { bootstrapping = false; bootstrapActor = null; }
            }
            modulesValidated = true;
        }
        private void SaveModules()
        {
            var snapshots = new List<ModulePayload>();
            foreach (var callback in lifecycles.OrderBy(x => x.owner,StringComparer.Ordinal))
            {
                string data = callback.save(this);
                if (!string.IsNullOrEmpty(data)) snapshots.Add(new ModulePayload { owner = callback.owner, version = callback.version, data = data });
            }
            modulePayloads = snapshots;
        }
        private void UpdateModules()
        { foreach (var callback in lifecycles) if (callback.update != null) callback.update(this); }
        private void ExposeModulePayloads()
        {
            if (header != null && header.SchemaVersion >= 4)
            {
                if (Scribe.mode == LoadSaveMode.Saving) modulePayloadCount = modulePayloads.Count;
                Scribe_Values.Look(ref modulePayloadCount,"inheritanceModulePayloadCount",-1,true);
                Scribe_Collections.Look(ref modulePayloads,"inheritanceModulePayloads",LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && (modulePayloads == null || modulePayloadCount != modulePayloads.Count))
                    blockReason = "Module payload record count mismatch";
            }
        }
    }
}
