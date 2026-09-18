using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TapeMeasure
{
    internal sealed class MeasurementPersistence
    {
        private const string RootNodeName = "TAPE_MEASURE_DATABASE";
        private const string CraftNodeName = "CRAFT";
        private const string MeasurementNodeName = "MEASUREMENT";
        private const string DatabaseVersion = "7";

        private readonly string _path;
        private readonly string _legacyPath;
        private readonly List<StoredCraft> _crafts = new List<StoredCraft>();

        public MeasurementPersistence()
        {
            string saveFolder = string.IsNullOrEmpty(HighLogic.SaveFolder) ? "default" : HighLogic.SaveFolder;
            _path = Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder, "TapeMeasure", "Measurements.cfg");
            _legacyPath = Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder, "VesselMeasureTool", "Measurements.cfg");
            bool importedLegacy = !File.Exists(_path) && File.Exists(_legacyPath);
            LoadDatabase(importedLegacy ? _legacyPath : _path);
            if (importedLegacy)
            {
                Debug.Log("[TapeMeasure] Imported VesselMeasureTool measurements.");
                SaveDatabase();
            }
        }

        public List<MeasurementRecord> LoadForShip(ShipConstruct ship, string editorKey, out bool usedRootFallback)
        {
            usedRootFallback = false;
            List<MeasurementRecord> result = new List<MeasurementRecord>();
            if (ship == null || ship.parts == null || ship.parts.Count == 0) return result;

            string shipName = SafeShipName(ship.shipName);
            uint rootCraftId = GetRootCraftId(ship);
            StoredCraft stored = FindExact(editorKey, shipName);
            if (stored == null && rootCraftId != 0u)
            {
                stored = FindByRoot(editorKey, rootCraftId);
                usedRootFallback = stored != null;
            }
            if (stored == null) return result;

            for (int i = 0; i < stored.Measurements.Count; ++i)
            {
                StoredMeasurement item = stored.Measurements[i];
                MeasurementPoint a = ResolvePoint(ship, item.A, item.LockMode);
                MeasurementPoint b = ResolvePoint(ship, item.B, item.LockMode);
                if (a == null || b == null) continue;

                MeasurementPoint c = null;
                if (item.Kind == MeasurementKind.Angle)
                {
                    c = ResolvePoint(ship, item.C, item.LockMode);
                    if (c == null) continue;
                }

                result.Add(new MeasurementRecord(
                    item.Id, item.Name, item.Kind, item.LockMode, a, b, c,
                    item.Visible, item.Group, item.DisplayColor, item.Notes));
            }
            return result;
        }

        public void SaveForShip(ShipConstruct ship, string editorKey, IList<MeasurementRecord> measurements)
        {
            if (ship == null || ship.parts == null || ship.parts.Count == 0) return;
            string shipName = SafeShipName(ship.shipName);
            StoredCraft stored = FindExact(editorKey, shipName);
            if (stored == null)
            {
                stored = new StoredCraft { EditorKey = editorKey, ShipName = shipName };
                _crafts.Add(stored);
            }

            stored.RootCraftId = GetRootCraftId(ship);
            stored.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            stored.Measurements.Clear();

            if (measurements != null)
            {
                for (int i = 0; i < measurements.Count; ++i)
                {
                    MeasurementRecord m = measurements[i];
                    if (m == null || !m.IsComplete) continue;
                    StoredMeasurement item = new StoredMeasurement();
                    item.Id = m.Id;
                    item.Name = m.Name;
                    item.Kind = m.Kind;
                    item.LockMode = m.LockMode;
                    item.Visible = m.Visible;
                    item.Group = m.Group;
                    item.DisplayColor = m.DisplayColor;
                    item.Notes = m.Notes ?? string.Empty;
                    item.A = CopyPoint(m.PointA);
                    item.B = CopyPoint(m.PointB);
                    if (m.Kind == MeasurementKind.Angle) item.C = CopyPoint(m.PointC);
                    stored.Measurements.Add(item);
                }
            }
            SaveDatabase();
        }

        private static StoredPoint CopyPoint(MeasurementPoint point)
        {
            StoredPoint p = new StoredPoint();
            if (point == null) return p;
            p.CraftId = point.CraftId;
            p.PartLocal = point.PartLocalPosition;
            p.NodeId = point.SnapNodeId;
            p.VesselReferenceCraftId = point.VesselReferenceCraftId;
            p.VesselLocal = point.VesselLocalPosition;
            p.HasVesselLocal = point.HasVesselLocalPosition;
            return p;
        }

        private static MeasurementPoint ResolvePoint(ShipConstruct ship, StoredPoint p, MeasurementLockMode mode)
        {
            return MeasurementPoint.Resolve(
                ship, p.CraftId, p.PartLocal, p.NodeId,
                p.VesselReferenceCraftId, p.VesselLocal, p.HasVesselLocal, mode);
        }

        private void LoadDatabase(string path)
        {
            _crafts.Clear();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                ConfigNode root = ConfigNode.Load(path);
                if (root == null) return;
                ConfigNode[] craftNodes = root.GetNodes(CraftNodeName);
                for (int i = 0; i < craftNodes.Length; ++i)
                {
                    ConfigNode n = craftNodes[i];
                    StoredCraft craft = new StoredCraft();
                    craft.EditorKey = GetString(n, "editor", "VAB");
                    craft.ShipName = GetString(n, "shipName", "Untitled Space Craft");
                    craft.RootCraftId = GetUInt(n, "rootCraftId", 0u);
                    craft.UpdatedUtc = GetString(n, "updatedUtc", string.Empty);
                    ConfigNode[] measurementNodes = n.GetNodes(MeasurementNodeName);
                    for (int j = 0; j < measurementNodes.Length; ++j)
                    {
                        StoredMeasurement item = ParseMeasurement(measurementNodes[j]);
                        if (item != null) craft.Measurements.Add(item);
                    }
                    _crafts.Add(craft);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[TapeMeasure] Failed to load persistence database: " + ex);
            }
        }

        private void SaveDatabase()
        {
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                ConfigNode root = new ConfigNode(RootNodeName);
                root.AddValue("version", DatabaseVersion);

                for (int i = 0; i < _crafts.Count; ++i)
                {
                    StoredCraft craft = _crafts[i];
                    ConfigNode cn = root.AddNode(CraftNodeName);
                    cn.AddValue("editor", craft.EditorKey);
                    cn.AddValue("shipName", craft.ShipName);
                    cn.AddValue("rootCraftId", craft.RootCraftId.ToString(CultureInfo.InvariantCulture));
                    cn.AddValue("updatedUtc", craft.UpdatedUtc ?? string.Empty);

                    for (int j = 0; j < craft.Measurements.Count; ++j)
                    {
                        StoredMeasurement item = craft.Measurements[j];
                        ConfigNode mn = cn.AddNode(MeasurementNodeName);
                        mn.AddValue("id", item.Id ?? string.Empty);
                        mn.AddValue("name", item.Name ?? "Measurement");
                        mn.AddValue("kind", item.Kind.ToString());
                        mn.AddValue("lockMode", item.LockMode.ToString());
                        mn.AddValue("visible", item.Visible);
                        mn.AddValue("group", item.Group ?? string.Empty);
                        mn.AddValue("color", ColorToHex(item.DisplayColor));
                        mn.AddValue("notes", item.Notes ?? string.Empty);
                        AddStoredPoint(mn, "a", item.A);
                        AddStoredPoint(mn, "b", item.B);
                        if (item.Kind == MeasurementKind.Angle) AddStoredPoint(mn, "c", item.C);
                    }
                }

                string temp = _path + ".tmp";
                root.Save(temp);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);
            }
            catch (Exception ex)
            {
                Debug.LogError("[TapeMeasure] Failed to save persistence database: " + ex);
            }
        }

        private static void AddStoredPoint(ConfigNode node, string prefix, StoredPoint p)
        {
            node.AddValue(prefix + "CraftId", p.CraftId.ToString(CultureInfo.InvariantCulture));
            AddVector(node, prefix, p.PartLocal);
            if (!string.IsNullOrEmpty(p.NodeId)) node.AddValue(prefix + "NodeId", p.NodeId);
            node.AddValue(prefix + "VesselRefCraftId", p.VesselReferenceCraftId.ToString(CultureInfo.InvariantCulture));
            if (p.HasVesselLocal)
            {
                node.AddValue(prefix + "HasVesselLocal", true);
                AddVector(node, prefix + "V", p.VesselLocal);
            }
        }

        private StoredCraft FindExact(string editorKey, string shipName)
        {
            for (int i = 0; i < _crafts.Count; ++i)
            {
                StoredCraft c = _crafts[i];
                if (string.Equals(c.EditorKey, editorKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.ShipName, shipName, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        private StoredCraft FindByRoot(string editorKey, uint rootCraftId)
        {
            StoredCraft newest = null;
            DateTime newestTime = DateTime.MinValue;
            for (int i = 0; i < _crafts.Count; ++i)
            {
                StoredCraft c = _crafts[i];
                if (!string.Equals(c.EditorKey, editorKey, StringComparison.OrdinalIgnoreCase) || c.RootCraftId != rootCraftId) continue;
                DateTime parsed;
                if (!DateTime.TryParse(c.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)) parsed = DateTime.MinValue;
                if (newest == null || parsed >= newestTime) { newest = c; newestTime = parsed; }
            }
            return newest;
        }

        private static StoredMeasurement ParseMeasurement(ConfigNode node)
        {
            StoredMeasurement item = new StoredMeasurement();
            item.Id = GetString(node, "id", Guid.NewGuid().ToString("N"));
            item.Name = GetString(node, "name", "Measurement");
            item.Kind = ParseKind(GetString(node, "kind", "Distance"));
            item.LockMode = ParseLockMode(GetString(node, "lockMode", "PartRelative"));
            item.Visible = GetBool(node, "visible", true);
            item.Group = GetString(node, "group", string.Empty);
            item.DisplayColor = ParseColor(GetString(node, "color", string.Empty), MeasurementRecord.DefaultDisplayColor);
            item.Notes = GetString(node, "notes", string.Empty);
            item.A = ParsePoint(node, "a");
            item.B = ParsePoint(node, "b");
            if (item.A.CraftId == 0u || item.B.CraftId == 0u) return null;
            if (item.Kind == MeasurementKind.Angle)
            {
                item.C = ParsePoint(node, "c");
                if (item.C.CraftId == 0u) return null;
            }
            return item;
        }

        private static StoredPoint ParsePoint(ConfigNode node, string prefix)
        {
            StoredPoint p = new StoredPoint();
            p.CraftId = GetUInt(node, prefix + "CraftId", 0u);
            p.PartLocal = GetVector(node, prefix);
            p.NodeId = GetOptionalString(node, prefix + "NodeId");
            p.VesselReferenceCraftId = GetUInt(node, prefix + "VesselRefCraftId", 0u);
            p.HasVesselLocal = GetBool(node, prefix + "HasVesselLocal", false) || node.HasValue(prefix + "VX");
            p.VesselLocal = GetVector(node, prefix + "V");
            return p;
        }

        private static MeasurementKind ParseKind(string value)
        {
            MeasurementKind parsed;
            return Enum.TryParse(value, true, out parsed) ? parsed : MeasurementKind.Distance;
        }

        private static MeasurementLockMode ParseLockMode(string value)
        {
            MeasurementLockMode parsed;
            return Enum.TryParse(value, true, out parsed) ? parsed : MeasurementLockMode.PartRelative;
        }

        private static void AddVector(ConfigNode node, string prefix, Vector3 value)
        {
            node.AddValue(prefix + "X", value.x.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(prefix + "Y", value.y.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(prefix + "Z", value.z.ToString("R", CultureInfo.InvariantCulture));
        }

        private static Vector3 GetVector(ConfigNode node, string prefix)
        {
            return new Vector3(GetFloat(node, prefix + "X", 0f), GetFloat(node, prefix + "Y", 0f), GetFloat(node, prefix + "Z", 0f));
        }

        private static string GetString(ConfigNode node, string key, string fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            string value = node.GetValue(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string GetOptionalString(ConfigNode node, string key)
        {
            if (node == null || !node.HasValue(key)) return null;
            string value = node.GetValue(key);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static uint GetUInt(ConfigNode node, string key, uint fallback)
        {
            uint value; string text = node != null ? node.GetValue(key) : null;
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static float GetFloat(ConfigNode node, string key, float fallback)
        {
            float value; string text = node != null ? node.GetValue(key) : null;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool GetBool(ConfigNode node, string key, bool fallback)
        {
            bool value; string text = node != null ? node.GetValue(key) : null;
            return bool.TryParse(text, out value) ? value : fallback;
        }


        private static string ColorToHex(Color color)
        {
            Color32 c = color;
            return c.r.ToString("X2", CultureInfo.InvariantCulture) +
                   c.g.ToString("X2", CultureInfo.InvariantCulture) +
                   c.b.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            string hex = value.Trim().TrimStart('#');
            if (hex.Length != 6) return fallback;
            byte r, g, b;
            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                return fallback;
            return new Color32(r, g, b, 255);
        }

        private static uint GetRootCraftId(ShipConstruct ship)
        {
            return ship == null || ship.parts == null || ship.parts.Count == 0 || ship.parts[0] == null ? 0u : ship.parts[0].craftID;
        }

        private static string SafeShipName(string name) { return string.IsNullOrEmpty(name) ? "Untitled Space Craft" : name; }

        private sealed class StoredCraft
        {
            public string EditorKey;
            public string ShipName;
            public uint RootCraftId;
            public string UpdatedUtc;
            public readonly List<StoredMeasurement> Measurements = new List<StoredMeasurement>();
        }

        private sealed class StoredMeasurement
        {
            public string Id;
            public string Name;
            public MeasurementKind Kind;
            public MeasurementLockMode LockMode;
            public bool Visible = true;
            public string Group = string.Empty;
            public Color DisplayColor = MeasurementRecord.DefaultDisplayColor;
            public string Notes = string.Empty;
            public StoredPoint A;
            public StoredPoint B;
            public StoredPoint C;
        }

        private struct StoredPoint
        {
            public uint CraftId;
            public Vector3 PartLocal;
            public string NodeId;
            public uint VesselReferenceCraftId;
            public Vector3 VesselLocal;
            public bool HasVesselLocal;
        }
    }
}
