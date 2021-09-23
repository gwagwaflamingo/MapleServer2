﻿using System.Text.RegularExpressions;
using System.Xml;
using GameDataParser.Files;
using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.Parser.Flat;
using Maple2.File.Parser.MapXBlock;
using Maple2Storage.Enums;
using Maple2Storage.Types;
using Maple2Storage.Types.Metadata;

namespace GameDataParser.Parsers
{
    public class MapEntityParser : Exporter<List<MapEntityMetadata>>
    {
        private List<MapEntityMetadata> Entities;
        private Dictionary<string, Dictionary<int, SpawnMetadata>> SpawnTagMap;
        private Dictionary<string, string> Maps;
        private Dictionary<int, InteractObjectType> InteractTypes;

        public MapEntityParser(MetadataResources resources) : base(resources, "map-entity") { }

        protected override List<MapEntityMetadata> Parse()
        {
            // Get InteractObject Types
            InteractTypes = new Dictionary<int, InteractObjectType>();
            foreach (PackFileEntry entry in Resources.XmlReader.Files)
            {
                if (!entry.Name.StartsWith("table/interactobject"))
                {
                    continue;
                }

                XmlDocument document = Resources.XmlReader.GetXmlDocument(entry);
                XmlNodeList interactNodes = document.GetElementsByTagName("interact");
                foreach (XmlNode node in interactNodes)
                {
                    string locale = string.IsNullOrEmpty(node.Attributes["locale"]?.Value) ? "" : node.Attributes["locale"].Value;
                    if (locale != "NA" && locale != "")
                    {
                        continue;
                    }

                    int interactId = int.Parse(node.Attributes["id"].Value);
                    _ = Enum.TryParse(node.Attributes["type"].Value, out InteractObjectType objectType);

                    InteractTypes[interactId] = objectType;
                }
            }

            Entities = new List<MapEntityMetadata>();

            // Get mob spawn ID and mob spawn information from xml (can be expanded to parse other xml info)
            SpawnTagMap = new Dictionary<string, Dictionary<int, SpawnMetadata>>();
            foreach (PackFileEntry entry in Resources.XmlReader.Files
                .Where(entry => Regex.Match(entry.Name, "table/mapspawntag").Success))
            {
                XmlDocument document = Resources.XmlReader.GetXmlDocument(entry);
                XmlNodeList regionNodes = document.SelectNodes("/ms2/region");

                foreach (XmlNode node in regionNodes)
                {
                    string mapID = node.Attributes["mapCode"].Value;
                    int spawnPointID = int.Parse(node.Attributes["spawnPointID"].Value);

                    int difficulty = int.Parse(node.Attributes["difficulty"].Value);
                    int minDifficulty = int.Parse(node.Attributes["difficultyMin"].Value);
                    string[] spawnTags = node.Attributes["tag"].Value.Split(",").Select(p => p.Trim()).ToArray();
                    if (!int.TryParse(node.Attributes["coolTime"].Value, out int spawnTime))
                    {
                        spawnTime = 0;
                    }

                    if (!int.TryParse(node.Attributes["population"].Value, out int population))
                    {
                        population = 0;
                    }

                    bool isPetSpawn = node.Attributes["petPopulation"] != null &&
                                      int.Parse(node.Attributes["petPopulation"].Value) > 0;

                    SpawnMetadata spawnData = new SpawnMetadata(spawnTags, population, spawnTime, difficulty,
                        minDifficulty, isPetSpawn);
                    if (!SpawnTagMap.ContainsKey(mapID))
                    {
                        SpawnTagMap[mapID] = new Dictionary<int, SpawnMetadata>();
                    }

                    SpawnTagMap[mapID][spawnPointID] = spawnData;
                }
            }

            // Iterate over map xblocks
            Maps = new Dictionary<string, string>();
            XBlockParser parser = new XBlockParser(Resources.ExportedReader, new FlatTypeIndex(Resources.ExportedReader));
            parser.Parse(BuildMetadata);

            // Since parsing is done in parallel, sort at the end for deterministic order.
            Entities.Sort((metadata1, metadata2) => metadata1.MapId.CompareTo(metadata2.MapId));
            return Entities;
        }

        private void BuildMetadata(string xblock, IEnumerable<IMapEntity> mapEntities)
        {
            if (xblock.EndsWith("_cn") || xblock.EndsWith("_jp") || xblock.EndsWith("_kr"))
            {
                return;
            }

            Match isParsableField = Regex.Match(xblock, @"^(\d{8})");
            if (!isParsableField.Success)
            {
                // TODO: Handle these later, if we need them. They're xblock files with some other names like
                //  character_test.xblock, login.xblock,
                return;
            }

            string mapId = isParsableField.Groups[1].Value;
            if (Maps.ContainsKey(mapId))
            {
                return;
            }

            Maps.Add(mapId, xblock); // Only used to check if we've visited this node before.

            // TODO: metadata should be keyed on xblock name, not mapId
            MapEntityMetadata metadata = new MapEntityMetadata(int.Parse(mapId));

            foreach (IMapEntity entity in mapEntities)
            {
                switch (entity)
                {
                    case IMS2Bounding bounding:
                        if (bounding.EntityName.EndsWith("0") && metadata.BoundingBox0.Equals(CoordS.From(0, 0, 0)))
                        {
                            metadata.BoundingBox0 = CoordS.FromVector3(bounding.Position);
                        }
                        else if (bounding.EntityName.EndsWith("1") && metadata.BoundingBox1.Equals(CoordS.From(0, 0, 0)))
                        {
                            metadata.BoundingBox1 = CoordS.FromVector3(bounding.Position);
                        }
                        break;
                    case IMS2PatrolData patrolData:
                        string patrolDataName = patrolData.EntityName.Replace("-", string.Empty);
                        List<string> wayPointIds = new List<string>();
                        foreach (KeyValuePair<string, string> entry in patrolData.WayPoints)
                        {
                            string wayPointId = entry.Value.Replace("-", string.Empty);
                            wayPointIds.Add(wayPointId);
                        }

                        List<string> arriveAnimations = new List<string>();
                        foreach (KeyValuePair<string, string> entry in patrolData.ArriveAnims)
                        {
                            arriveAnimations.Add(entry.Value);
                        }

                        List<string> approachAnimations = new List<string>();
                        foreach (KeyValuePair<string, string> entry in patrolData.ApproachAnims)
                        {
                            approachAnimations.Add(entry.Value);
                        }

                        List<int> arriveAnimationTimes = new List<int>();
                        foreach (KeyValuePair<string, uint> entry in patrolData.ArriveAnimsTime)
                        {
                            arriveAnimationTimes.Add((int) entry.Value);
                        }
                        metadata.PatrolDatas.Add(new PatrolData(patrolDataName, wayPointIds, (int) patrolData.PatrolSpeed, patrolData.IsLoop, patrolData.IsAirWayPoint, arriveAnimations, approachAnimations, arriveAnimationTimes));
                        break;
                    case IMS2WayPoint wayPoint:
                        metadata.WayPoints.Add(new WayPoint(wayPoint.EntityId, wayPoint.IsVisible, CoordS.FromVector3(wayPoint.Position), CoordS.FromVector3(wayPoint.Rotation)));
                        break;
                    // TODO: This can probably be more generally handled as IMS2RegionSkill
                    case IMS2HealingRegionSkillSound healingRegion:
                        if (healingRegion.Position == default)
                        {
                            continue;
                        }

                        metadata.HealingSpot.Add(CoordS.FromVector3(healingRegion.Position));
                        break;
                    case IMS2InteractObject interact:
                        InteractObjectType type = GetInteractObjectType(interact.interactID);
                        if (type == InteractObjectType.None)
                        {
                            continue;
                        }
                        switch (interact)
                        {
                            case IMS2SimpleUiObject uiObject:
                                metadata.InteractObjects.Add(new MapInteractObject(uiObject.EntityId, uiObject.interactID, uiObject.Enabled, type));
                                break;
                            case IMS2InteractMesh interactMesh:
                                metadata.InteractObjects.Add(new MapInteractObject(interactMesh.EntityId, interactMesh.interactID, interactMesh.IsVisible, type));
                                break;
                            case IMS2Telescope telescope:
                                metadata.InteractObjects.Add(new MapInteractObject(telescope.EntityId, telescope.interactID, telescope.Enabled, type));
                                break;
                            case IMS2InteractActor interactActor:
                                metadata.InteractObjects.Add(new MapInteractObject(interactActor.EntityId, interactActor.interactID, interactActor.IsVisible, type));
                                break;
                            case IMS2InteractDisplay interactDisplay:
                                metadata.InteractObjects.Add(new MapInteractObject(interactDisplay.EntityId, interactDisplay.interactID, interactDisplay.IsVisible, type));
                                break;
                        }
                        break;
                    case ISpawnPoint spawn:
                        switch (spawn)
                        {
                            // TODO: Parse "value" from NPCList.
                            case IEventSpawnPointNPC eventSpawnNpc: // trigger mob/npc spawns
                                List<string> npcIds = new List<string>();
                                npcIds.AddRange(eventSpawnNpc.NpcList.Keys);

                                metadata.EventNpcSpawnPoints.Add(new MapEventNpcSpawnPoint(eventSpawnNpc.SpawnPointID, eventSpawnNpc.NpcCount, npcIds, eventSpawnNpc.SpawnAnimation, eventSpawnNpc.SpawnRadius,
                                    CoordF.FromVector3(eventSpawnNpc.Position), CoordF.FromVector3(eventSpawnNpc.Rotation)));
                                break;
                            case ISpawnPointPC pcSpawn:
                                metadata.PlayerSpawns.Add(
                                    new MapPlayerSpawn(CoordS.FromVector3(pcSpawn.Position), CoordS.FromVector3(pcSpawn.Rotation)));
                                break;
                            case ISpawnPointNPC npcSpawn:
                                // These tend to be vendors, shops, etc.
                                // If the name tag begins with SpawnPointNPC, I think these are mob spawn locations. Skipping these.
                                string npcIdStr = npcSpawn.NpcList.FirstOrDefault().Key ?? "0";
                                if (npcIdStr == "0" || !int.TryParse(npcIdStr, out int npcId))
                                {
                                    continue;
                                }

                                MapNpc npc = new MapNpc(npcId, npcSpawn.ModelName, npcSpawn.EntityName, CoordS.FromVector3(npcSpawn.Position),
                                    CoordS.FromVector3(npcSpawn.Rotation), npcSpawn.IsSpawnOnFieldCreate, npcSpawn.dayDie, npcSpawn.nightDie);
                                // Parse some additional flat supplemented data about this NPC.

                                metadata.Npcs.Add(npc);
                                break;
                        }
                        break;
                    case IMS2RegionSpawnBase spawnBase:
                        switch (spawnBase)
                        {
                            case IMS2RegionBoxSpawn boxSpawn:
                                SpawnMetadata mobSpawnDataBox =
                                    (SpawnTagMap.ContainsKey(mapId) && SpawnTagMap[mapId].ContainsKey(boxSpawn.SpawnPointID))
                                        ? SpawnTagMap[mapId][boxSpawn.SpawnPointID]
                                        : null;

                                int mobNpcCountBox = mobSpawnDataBox?.Population ?? 6;
                                int mobSpawnRadiusBox = 150;
                                // TODO: This previously relied in "NpcList" to be set. NpcList is impossible to be set on
                                // MS2RegionSpawn, it's only set for SpawnPointNPC.
                                List<int> mobNpcListBox = new List<int>
                                {
                                    21000025 // Placeholder
                                };
                                metadata.MobSpawns.Add(new MapMobSpawn(boxSpawn.SpawnPointID, CoordS.FromVector3(boxSpawn.Position),
                                    mobNpcCountBox, mobNpcListBox, mobSpawnRadiusBox, mobSpawnDataBox));
                                // "QR_10000264_" is Quest Reward Chest? This is tied to a MS2TriggerAgent making this object appear.
                                break;
                            case IMS2RegionSpawn regionSpawn:
                                SpawnMetadata mobSpawnData =
                                    (SpawnTagMap.ContainsKey(mapId) && SpawnTagMap[mapId].ContainsKey(regionSpawn.SpawnPointID))
                                        ? SpawnTagMap[mapId][regionSpawn.SpawnPointID]
                                        : null;

                                int mobNpcCount = mobSpawnData?.Population ?? 6;

                                // TODO: This previously relied in "NpcList" to be set. NpcList is impossible to be set on
                                // MS2RegionSpawn, it's only set for SpawnPointNPC.
                                List<int> mobNpcList = new List<int>
                                {
                                    21000025 // Placeholder
                                };
                                metadata.MobSpawns.Add(new MapMobSpawn(regionSpawn.SpawnPointID, CoordS.FromVector3(regionSpawn.Position),
                                    mobNpcCount, mobNpcList, (int) regionSpawn.SpawnRadius, mobSpawnData));
                                break;
                        }
                        break;
                    case IPortal portal:
                        metadata.Portals.Add(new MapPortal(portal.PortalID, portal.ModelName, portal.PortalEnable, portal.IsVisible, portal.MinimapIconVisible, portal.TargetFieldSN,
                            CoordS.FromVector3(portal.Position), CoordS.FromVector3(portal.Rotation), portal.TargetPortalID, (byte) portal.PortalType));
                        break;
                    case IMS2Breakable breakable:
                        switch (breakable)
                        {
                            case IMS2BreakableActor actor:
                                metadata.BreakableActors.Add(new MapBreakableActorObject(actor.EntityId, actor.Enabled, actor.hideTimer, actor.resetTimer));
                                break;
                            case IMS2BreakableNIF nif:
                                metadata.BreakableNifs.Add(new MapBreakableNifObject(nif.EntityId, nif.Enabled, (int) nif.TriggerBreakableID, nif.hideTimer, nif.resetTimer));
                                break;
                        }
                        break;
                    case IMS2TriggerObject triggerObject:
                        switch (triggerObject)
                        {
                            case IMS2TriggerMesh triggerMesh:
                                metadata.TriggerMeshes.Add(new MapTriggerMesh(triggerMesh.TriggerObjectID, triggerMesh.IsVisible));
                                break;
                            case IMS2TriggerEffect triggerEffect:
                                metadata.TriggerEffects.Add(new MapTriggerEffect(triggerEffect.TriggerObjectID, triggerEffect.IsVisible));
                                break;
                            case IMS2TriggerCamera triggerCamera:
                                metadata.TriggerCameras.Add(new MapTriggerCamera(triggerCamera.TriggerObjectID, triggerCamera.Enabled));
                                break;
                            case IMS2TriggerBox triggerBox:
                                metadata.TriggerBoxes.Add(new MapTriggerBox(triggerBox.TriggerObjectID, CoordF.FromVector3(triggerBox.Position), CoordF.FromVector3(triggerBox.ShapeDimensions)));
                                break;
                            case IMS2TriggerLadder triggerLadder: // TODO: Find which parameters correspond to animationeffect (bool) and animation delay (int?)
                                metadata.TriggerLadders.Add(new MapTriggerLadder(triggerLadder.TriggerObjectID, triggerLadder.IsVisible));
                                break;
                            case IMS2TriggerRope triggerRope: // TODO: Find which parameters correspond to animationeffect (bool) and animation delay (int?)
                                metadata.TriggerRopes.Add(new MapTriggerRope(triggerRope.TriggerObjectID, triggerRope.IsVisible));
                                break;
                            case IMS2TriggerPortal triggerPortal:
                                metadata.Portals.Add(new MapPortal(triggerPortal.PortalID, triggerPortal.ModelName, triggerPortal.PortalEnable, triggerPortal.IsVisible, triggerPortal.MinimapIconVisible,
                                    triggerPortal.TargetFieldSN, CoordS.FromVector3(triggerPortal.Position), CoordS.FromVector3(triggerPortal.Rotation), triggerPortal.TargetPortalID, (byte) triggerPortal.PortalType, triggerPortal.TriggerObjectID));
                                break;
                            case IMS2TriggerActor triggerActor:
                                metadata.TriggerActors.Add(new MapTriggerActor(triggerActor.TriggerObjectID, triggerActor.IsVisible, triggerActor.InitialSequence));
                                break;
                            case IMS2TriggerCube triggerCube:
                                metadata.TriggerCubes.Add(new MapTriggerCube(triggerCube.TriggerObjectID, triggerCube.IsVisible));
                                break;
                            case IMS2TriggerSound triggerSound:
                                metadata.TriggerSounds.Add(new MapTriggerSound(triggerSound.TriggerObjectID, triggerSound.Enabled));
                                break;
                        }
                        break;
                    case IMS2Vibrate vibrate:
                        metadata.VibrateObjects.Add(new MapVibrateObject(vibrate.EntityId));
                        break;
                    case IPlaceable placeable: // TODO: placeable might be too generic
                        // These are objects which you can place in the world
                        string nameCoord = placeable.EntityName.ToLower();
                        Match coordMatch = Regex.Match(nameCoord, @"-?\d+, -?\d+, -?\d+");
                        if (!coordMatch.Success)
                        {
                            continue;
                        }

                        // Only MS2MapProperties has ObjectWeaponItemCode
                        if (entity is not IMS2MapProperties mapProperties)
                        {
                            continue;
                        }

                        try
                        //TODO: The parser will output errors here, which are non-critical, yet need resolving later.
                        {
                            CoordB coord = CoordB.Parse(coordMatch.Value, ", ");
                            metadata.Objects.Add(new MapObject(coord, int.Parse(mapProperties.ObjectWeaponItemCode)));
                        }
                        catch (FormatException)
                        {
                            // ignored
                            //byte[] bytes = System.Text.Encoding.UTF8.GetBytes(mapProperties.ObjectWeaponItemCode);
                            //Console.WriteLine($"String in bytes: {Convert.ToHexString(bytes)}");
                        }
                        catch (OverflowException)
                        {
                            //byte[] bytes = System.Text.Encoding.UTF8.GetBytes(mapProperties.ObjectWeaponItemCode);
                            //Console.WriteLine($"String in bytes: {Convert.ToHexString(bytes)}");
                        }
                        break;
                }

                /* NPC Objects have a modelName of 8 digits followed by an underscore and a name that's the same thing,
                     *  but with a number (for each instance on that map) after it
                     *
                     * IM_ Prefixed items are Interactable Meshes supplemented by data in "xml/table/interactobject.xml"
                     * IA_ prefixed items are Interactable Actors (Doors, etc). Have an interactID, which is an event on interact.
                     * "mixinMS2MapProperties" is generic field items
                     *  "mixinMS2SalePost" - is for sale signs. Does a packet need to respond to this item?
                     */
                /*

                // Unhandled Items:
                // "mixinEventSpawnPointNPC",
                // "mixinMS2Actor" as in "fa_fi_funct_irondoor_A01_"
                // MS2RegionSkill as in "SkillObj_co_Crepper_C03_" (Only on 8xxxx and 9xxxxx maps)
                // "mixinMS2FunctionCubeKFM" as in "ry_functobj_lamp_B01_", "ke_functobj_bath_B01_"
                // "mixinMS2FunctionCubeNIF"
                // "MS2MapProperties"->"MS2PhysXProp" that's not a weapon. Standard

                /*
                 * if (Regex.Match(modelName, @"^\d{8}_").Success && Regex.Match(name, @"\d{1,3}").Success)
                 * {
                    // Parse non-permanent NPCs. These have no .flat files to supplement them.
                    string npcListIndex = node.SelectSingleNode("property[@name='NpcList']")?.FirstChild.Attributes["index"].Value ?? "-1";
                    if (npcListIndex == "-1")
                    {
                        continue;
                    }
                    string npcPositionValue = node.SelectSingleNode("property[@name='Position']")?.FirstChild.Attributes["value"].Value ?? "0, 0, 0";
                    string npcRotationValue = node.SelectSingleNode("property[@name='Rotation']")?.FirstChild.Attributes["value"].Value ?? "0, 0, 0";
                    // metadata.Npcs.Add(new MapNpc(int.Parse(npcListIndex), modelName, name, ParseCoord(npcPositionValue), ParseCoord(npcRotationValue)));
                }
                */
            }

            // No data on this map
            if (metadata.Npcs.Count == 0 && metadata.Portals.Count == 0 && metadata.PlayerSpawns.Count == 0)
            {
                return;
            }

            Entities.Add(metadata);
        }

        private InteractObjectType GetInteractObjectType(int interactId)
        {
            if (InteractTypes.ContainsKey(interactId))
            {
                return InteractTypes[interactId];
            }
            return InteractObjectType.None;
        }
    }
}
