using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Inheritance.Core
{
    public sealed class ModuleStamp : IExposable
    {
        internal string packageId;
        internal int schemaVersion;
        public ModuleStamp() { }
        internal ModuleStamp(string id, int version) { packageId = id; schemaVersion = version; }
        internal ModuleStamp Copy() { return new ModuleStamp(packageId, schemaVersion); }
        public string PackageId { get { return packageId; } }
        public int SchemaVersion { get { return schemaVersion; } }
        public void ExposeData()
        {
            Scribe_Values.Look(ref packageId, "packageId"); Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0, true);
        }
    }

    internal sealed class ModuleCatalogue
    {
        internal const int Protocol = 2;
        private readonly Dictionary<string, int> schemas = new Dictionary<string, int>(StringComparer.Ordinal);
        internal static ModuleCatalogue Current()
        {
            var result = new ModuleCatalogue();
            foreach (var def in DefDatabase<ModuleDef>.AllDefs)
            {
                if (string.IsNullOrEmpty(def.packageId) || def.packageId != def.packageId.ToLowerInvariant() ||
                    def.packageId.Any(c => !(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '_'))
                    throw new InvalidOperationException("Invalid module package ID: " + def.packageId);
                if (def.protocolVersion != Protocol || def.moduleSchemaVersion < 1 || !ModsConfig.IsActive(def.packageId))
                    throw new InvalidOperationException("Unsupported or inactive module: " + def.packageId);
                if (def.modContentPack == null || def.modContentPack.PackageId.ToLowerInvariant() != def.packageId)
                    throw new InvalidOperationException("Module declaration belongs to a different package: " + def.packageId);
                if (result.schemas.ContainsKey(def.packageId)) throw new InvalidOperationException("Duplicate module declaration: " + def.packageId);
                result.schemas.Add(def.packageId, def.moduleSchemaVersion);
            }
            if (!result.schemas.ContainsKey("inheritance.core") || result.schemas["inheritance.core"] != SaveHeader.CurrentSchemaVersion)
                throw new InvalidOperationException("Core module schema declaration is missing or incompatible");
            return result;
        }
        internal void Require(string id)
        { if (id == null || !schemas.ContainsKey(id)) throw new InvalidOperationException("Unregistered module: " + id); }
        internal List<ModuleStamp> Stamps()
        { return schemas.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new ModuleStamp(x.Key, x.Value)).ToList(); }
        internal void ValidateSaved(List<ModuleStamp> saved, IEnumerable<string> owners, int savedCoreSchema)
        {
            var used = new HashSet<string>(owners, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in saved)
            {
                if (s == null || !seen.Add(s.packageId)) throw new InvalidOperationException("Duplicate or null saved module stamp");
                int supported;
                if (!schemas.TryGetValue(s.packageId, out supported))
                { if (used.Contains(s.packageId)) throw new InvalidOperationException("Missing saved data owner: " + s.packageId); }
                else if (s.schemaVersion != (s.packageId == "inheritance.core" ? savedCoreSchema : supported)) throw new InvalidOperationException("Unsupported saved module schema: " + s.packageId);
            }
            foreach (string owner in used)
                if (!seen.Contains(owner)) throw new InvalidOperationException("Missing module version for saved data: " + owner);
            if (!seen.Contains("inheritance.core")) throw new InvalidOperationException("Missing Core schema stamp");
        }
    }
}
