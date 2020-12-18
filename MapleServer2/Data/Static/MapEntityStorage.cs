using System.Collections.Generic;
using System.IO;
using Maple2Storage.Types;
using ProtoBuf;

namespace MapleServer2.Data.Static {
    public static class MapEntityStorage
    {
        private static readonly Dictionary<int, List<MapNpc>> npcs = new Dictionary<int, List<MapNpc>>();
        private static readonly Dictionary<int, List<MapPortal>> portals = new Dictionary<int, List<MapPortal>>();

        static MapEntityStorage() {
            using FileStream stream = File.OpenRead("Maple2Storage/Resources/ms2-map-entity-metadata");
            List<MapEntityMetadata> entities = Serializer.Deserialize<List<MapEntityMetadata>>(stream);
            foreach (MapEntityMetadata entity in entities)
            {
                npcs.Add(entity.MapId, entity.Npcs);
                portals.Add(entity.MapId, entity.Portals);
            }
        }

        public static IEnumerable<MapNpc> GetNpcs(int mapId) {
            return npcs.GetValueOrDefault(mapId);
        }

        public static IEnumerable<MapPortal> GetPortals(int mapId) {
            return portals.GetValueOrDefault(mapId);
        }

        public static bool HasPortals(int mapId)
        {
            List<MapPortal> items = portals.GetValueOrDefault(mapId);
            return items?.Count > 0;
        }

        public static MapPortal GetFirstPortal(int mapId)
        {
            List<MapPortal> items = portals.GetValueOrDefault(mapId);
            if (items.Count > 0)
            {
                return items[0];
            }
            return null;
        }
    }
}
