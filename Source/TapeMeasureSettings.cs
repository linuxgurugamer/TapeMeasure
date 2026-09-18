using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TapeMeasure
{
    internal enum DistanceUnitMode
    {
        Auto = 0,
        Meters = 1,
        Centimeters = 2,
        Millimeters = 3,
        FeetInches = 4
    }


    internal enum DisplayPrecisionMode
    {
        Automatic = 0,
        One = 1,
        Two = 2,
        Three = 3,
        Four = 4
    }

    internal enum MeasurementSortMode
    {
        Creation = 0,
        Name = 1,
        Type = 2,
        Value = 3
    }

    /// <summary>
    /// Per-save UI/render preferences. These are intentionally separate from
    /// craft measurements so appearance and UI changes do not dirty
    /// Measurements.cfg.
    /// </summary>
    internal sealed class TapeMeasureSettings
    {
        private const string NodeName = "TAPE_MEASURE_SETTINGS";
        private readonly string _path;

        public float MarkerSizeMultiplier = 1.0f;
        public float LineWidthMultiplier = 1.0f;
        public bool ShowMeasurementLines = true;
        public bool ShowWorldLabels = true;
        public bool ShowWorldLabelValues = true;
        public bool ShowMeasurementGuides = false;
        public bool ShowGuideLabels = false;
        public bool ShowAngleArcs = true;
        public bool ShowDimensionEndCaps = true;
        public bool ShowBoundingBox = false;
        public bool EmphasizeSelected = true;
        public float SelectedMarkerMultiplier = 1.35f;
        public float SelectedLineMultiplier = 1.50f;
        public float InactiveOpacity = 0.55f;

        // Snapping. SnappingEnabled is the master switch. Holding Shift in
        // the editor temporarily enables the configured target set without
        // changing this saved preference.
        public bool SnappingEnabled = false;
        public bool SnapPartOrigin = false;
        public bool SnapAttachmentNodes = true;
        public bool SnapSurfaceAttachmentPoint = false;
        public bool SnapPartCenter = false;
        public bool SnapVesselRoot = false;
        public bool SnapCenterOfMass = false;
        public bool SnapCenterOfLift = false;
        public bool SnapCenterOfThrust = false;
        public bool SnapVesselAxisGrid = false;
        public bool SnapMeasurementPoints = false;
        public float SnapPixelRadius = 34f;
        public float VesselGridSize = 0.10f;

        public bool SymmetryAwareMeasurements = false;
        public MeasurementLockMode DefaultLockMode = MeasurementLockMode.PartRelative;
        public DistanceUnitMode DistanceUnits = DistanceUnitMode.Auto;
        public DisplayPrecisionMode DisplayPrecision = DisplayPrecisionMode.Automatic;

        // Interface layout/skin preferences. The legacy "Show...Pane" field
        // names are retained for Settings.cfg compatibility; starting in 0.8.7
        // they represent each pane's expanded/collapsed state. The pane header
        // remains visible even when the content is collapsed.
        public bool UseAlternateSkin = false;
        public bool ShowSnappingPane = false;
        public bool ShowVesselDimensionsPane = true;
        public bool ShowMeasurementListPane = true;
        public bool SelectedMeasurementPaneExpanded = true;
        public bool HideWindowWhileMeasuring = false;
        // When true, stopping and later restarting measurement mode resumes an
        // unfinished distance/angle from its existing A/B points. When false,
        // Start Measuring abandons those unfinished points and begins fresh.
        public bool RememberIncompleteMeasurementOnRestart = true;
        public MeasurementSortMode MeasurementListSort = MeasurementSortMode.Creation;
        public bool MeasurementListSortAscending = true;

        // Keyboard shortcuts. These are per-save preferences and may be rebound
        // in the Settings window. Axis bindings are held while placing/editing.
        public ShortcutBinding ShortcutToggleMeasurement = new ShortcutBinding(KeyCode.M);
        public ShortcutBinding ShortcutCancelMode = new ShortcutBinding(KeyCode.Escape);
        public ShortcutBinding ShortcutNewDistance = new ShortcutBinding(KeyCode.None);
        public ShortcutBinding ShortcutNewAngle = new ShortcutBinding(KeyCode.None);
        public ShortcutBinding ShortcutEditEndpoints = new ShortcutBinding(KeyCode.None);
        public ShortcutBinding ShortcutToggleLabels = new ShortcutBinding(KeyCode.None);
        public ShortcutBinding ShortcutDeleteSelected = new ShortcutBinding(KeyCode.Delete);
        public ShortcutBinding ShortcutCopySelected = new ShortcutBinding(KeyCode.C, true);
        public ShortcutBinding ShortcutUndo = new ShortcutBinding(KeyCode.Z, true);
        public ShortcutBinding ShortcutRedo = new ShortcutBinding(KeyCode.Y, true);
        public ShortcutBinding ShortcutRedoAlternate = new ShortcutBinding(KeyCode.Z, true, true);
        public ShortcutBinding ShortcutSnapModifier = new ShortcutBinding(KeyCode.LeftShift);
        public ShortcutBinding ShortcutAxisX = new ShortcutBinding(KeyCode.X, false, false, true);
        public ShortcutBinding ShortcutAxisY = new ShortcutBinding(KeyCode.Y, false, false, true);
        public ShortcutBinding ShortcutAxisZ = new ShortcutBinding(KeyCode.Z, false, false, true);

        // Window/UI state. Window visibility itself is deliberately not saved;
        // entering the editor should always leave the tool discoverable.
        public bool HasWindowPosition = false;
        public float WindowX = 260f;
        public float WindowY = 90f;
        public float WindowWidth = 600f;
        public bool HasSettingsWindowPosition = false;
        public float SettingsWindowX = 1120f;
        public float SettingsWindowY = 90f;

        public TapeMeasureSettings()
        {
            string saveFolder = string.IsNullOrEmpty(HighLogic.SaveFolder)
                ? "default"
                : HighLogic.SaveFolder;

            _path = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "saves",
                saveFolder,
                "TapeMeasure",
                "Settings.cfg");

            Load();
        }

        public void Clamp()
        {
            MarkerSizeMultiplier = Mathf.Clamp(MarkerSizeMultiplier, 0.25f, 4f);
            LineWidthMultiplier = Mathf.Clamp(LineWidthMultiplier, 0.25f, 5f);
            SelectedMarkerMultiplier = Mathf.Clamp(SelectedMarkerMultiplier, 1f, 3f);
            SelectedLineMultiplier = Mathf.Clamp(SelectedLineMultiplier, 1f, 4f);
            InactiveOpacity = Mathf.Clamp(InactiveOpacity, 0.10f, 1f);
            SnapPixelRadius = Mathf.Clamp(SnapPixelRadius, 8f, 100f);
            VesselGridSize = Mathf.Clamp(VesselGridSize, 0.001f, 10f);
            WindowWidth = Mathf.Clamp(WindowWidth, 320f, 2400f);
        }

        public void Save()
        {
            Clamp();

            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                ConfigNode node = new ConfigNode(NodeName);
                node.AddValue("markerSizeMultiplier", F(MarkerSizeMultiplier));
                node.AddValue("lineWidthMultiplier", F(LineWidthMultiplier));
                node.AddValue("showMeasurementLines", ShowMeasurementLines);
                node.AddValue("showWorldLabels", ShowWorldLabels);
                node.AddValue("showWorldLabelValues", ShowWorldLabelValues);
                node.AddValue("showMeasurementGuides", ShowMeasurementGuides);
                node.AddValue("showGuideLabels", ShowGuideLabels);
                node.AddValue("showAngleArcs", ShowAngleArcs);
                node.AddValue("showDimensionEndCaps", ShowDimensionEndCaps);
                node.AddValue("showBoundingBox", ShowBoundingBox);
                node.AddValue("emphasizeSelected", EmphasizeSelected);
                node.AddValue("selectedMarkerMultiplier", F(SelectedMarkerMultiplier));
                node.AddValue("selectedLineMultiplier", F(SelectedLineMultiplier));
                node.AddValue("inactiveOpacity", F(InactiveOpacity));

                node.AddValue("snappingEnabled", SnappingEnabled);
                node.AddValue("snapPartOrigin", SnapPartOrigin);
                node.AddValue("snapAttachmentNodes", SnapAttachmentNodes);
                node.AddValue("snapSurfaceAttachmentPoint", SnapSurfaceAttachmentPoint);
                node.AddValue("snapPartCenter", SnapPartCenter);
                node.AddValue("snapVesselRoot", SnapVesselRoot);
                node.AddValue("snapCenterOfMass", SnapCenterOfMass);
                node.AddValue("snapCenterOfLift", SnapCenterOfLift);
                node.AddValue("snapCenterOfThrust", SnapCenterOfThrust);
                node.AddValue("snapVesselAxisGrid", SnapVesselAxisGrid);
                node.AddValue("snapMeasurementPoints", SnapMeasurementPoints);
                node.AddValue("snapPixelRadius", F(SnapPixelRadius));
                node.AddValue("vesselGridSize", F(VesselGridSize));

                // Legacy 0.8.3 key retained so rolling back does not silently
                // lose the user's attachment-node preference.
                node.AddValue("snapToAttachNodes", SnappingEnabled && SnapAttachmentNodes);
                node.AddValue("symmetryAwareMeasurements", SymmetryAwareMeasurements);
                node.AddValue("defaultLockMode", DefaultLockMode.ToString());
                node.AddValue("distanceUnits", DistanceUnits.ToString());
                node.AddValue("displayPrecision", DisplayPrecision.ToString());
                node.AddValue("useAlternateSkin", UseAlternateSkin);
                node.AddValue("showSnappingPane", ShowSnappingPane);
                node.AddValue("showVesselDimensionsPane", ShowVesselDimensionsPane);
                node.AddValue("showMeasurementListPane", ShowMeasurementListPane);
                node.AddValue("selectedMeasurementPaneExpanded", SelectedMeasurementPaneExpanded);
                node.AddValue("hideWindowWhileMeasuring", HideWindowWhileMeasuring);
                node.AddValue("rememberIncompleteMeasurementOnRestart", RememberIncompleteMeasurementOnRestart);
                node.AddValue("measurementListSort", MeasurementListSort.ToString());
                node.AddValue("measurementListSortAscending", MeasurementListSortAscending);
                SaveShortcut(node, "shortcutToggleMeasurement", ShortcutToggleMeasurement);
                SaveShortcut(node, "shortcutCancelMode", ShortcutCancelMode);
                SaveShortcut(node, "shortcutNewDistance", ShortcutNewDistance);
                SaveShortcut(node, "shortcutNewAngle", ShortcutNewAngle);
                SaveShortcut(node, "shortcutEditEndpoints", ShortcutEditEndpoints);
                SaveShortcut(node, "shortcutToggleLabels", ShortcutToggleLabels);
                SaveShortcut(node, "shortcutDeleteSelected", ShortcutDeleteSelected);
                SaveShortcut(node, "shortcutCopySelected", ShortcutCopySelected);
                SaveShortcut(node, "shortcutUndo", ShortcutUndo);
                SaveShortcut(node, "shortcutRedo", ShortcutRedo);
                SaveShortcut(node, "shortcutRedoAlternate", ShortcutRedoAlternate);
                SaveShortcut(node, "shortcutSnapModifier", ShortcutSnapModifier);
                SaveShortcut(node, "shortcutAxisX", ShortcutAxisX);
                SaveShortcut(node, "shortcutAxisY", ShortcutAxisY);
                SaveShortcut(node, "shortcutAxisZ", ShortcutAxisZ);
                node.AddValue("hasWindowPosition", HasWindowPosition);
                node.AddValue("windowX", F(WindowX));
                node.AddValue("windowY", F(WindowY));
                node.AddValue("windowWidth", F(WindowWidth));
                node.AddValue("hasSettingsWindowPosition", HasSettingsWindowPosition);
                node.AddValue("settingsWindowX", F(SettingsWindowX));
                node.AddValue("settingsWindowY", F(SettingsWindowY));

                string temp = _path + ".tmp";
                node.Save(temp);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);
            }
            catch (Exception ex)
            {
                Debug.LogError("[TapeMeasure] Failed to save settings: " + ex);
            }
        }

        private void Load()
        {
            if (!File.Exists(_path))
                return;

            try
            {
                ConfigNode node = ConfigNode.Load(_path);
                if (node == null)
                    return;

                MarkerSizeMultiplier = GetFloat(node, "markerSizeMultiplier", MarkerSizeMultiplier);
                LineWidthMultiplier = GetFloat(node, "lineWidthMultiplier", LineWidthMultiplier);
                ShowMeasurementLines = GetBool(node, "showMeasurementLines", ShowMeasurementLines);
                ShowWorldLabels = GetBool(node, "showWorldLabels", ShowWorldLabels);
                ShowWorldLabelValues = GetBool(node, "showWorldLabelValues", ShowWorldLabelValues);
                ShowMeasurementGuides = GetBool(node, "showMeasurementGuides", ShowMeasurementGuides);
                ShowGuideLabels = GetBool(node, "showGuideLabels", ShowGuideLabels);
                ShowAngleArcs = GetBool(node, "showAngleArcs", ShowAngleArcs);
                ShowDimensionEndCaps = GetBool(node, "showDimensionEndCaps", ShowDimensionEndCaps);
                ShowBoundingBox = GetBool(node, "showBoundingBox", ShowBoundingBox);
                EmphasizeSelected = GetBool(node, "emphasizeSelected", EmphasizeSelected);
                SelectedMarkerMultiplier = GetFloat(node, "selectedMarkerMultiplier", SelectedMarkerMultiplier);
                SelectedLineMultiplier = GetFloat(node, "selectedLineMultiplier", SelectedLineMultiplier);
                InactiveOpacity = GetFloat(node, "inactiveOpacity", InactiveOpacity);

                // Upgrade the old single attachment-node snapping flag to the
                // new master switch the first time a 0.8.3 Settings.cfg is read.
                bool legacyAttachSnap = GetBool(node, "snapToAttachNodes", false);
                SnappingEnabled = node.HasValue("snappingEnabled")
                    ? GetBool(node, "snappingEnabled", SnappingEnabled)
                    : legacyAttachSnap;
                SnapPartOrigin = GetBool(node, "snapPartOrigin", SnapPartOrigin);
                SnapAttachmentNodes = node.HasValue("snapAttachmentNodes")
                    ? GetBool(node, "snapAttachmentNodes", SnapAttachmentNodes)
                    : true;
                SnapSurfaceAttachmentPoint = GetBool(node, "snapSurfaceAttachmentPoint", SnapSurfaceAttachmentPoint);
                SnapPartCenter = GetBool(node, "snapPartCenter", SnapPartCenter);
                SnapVesselRoot = GetBool(node, "snapVesselRoot", SnapVesselRoot);
                SnapCenterOfMass = GetBool(node, "snapCenterOfMass", SnapCenterOfMass);
                SnapCenterOfLift = GetBool(node, "snapCenterOfLift", SnapCenterOfLift);
                SnapCenterOfThrust = GetBool(node, "snapCenterOfThrust", SnapCenterOfThrust);
                SnapVesselAxisGrid = GetBool(node, "snapVesselAxisGrid", SnapVesselAxisGrid);
                SnapMeasurementPoints = GetBool(node, "snapMeasurementPoints", SnapMeasurementPoints);
                SnapPixelRadius = GetFloat(node, "snapPixelRadius", SnapPixelRadius);
                VesselGridSize = GetFloat(node, "vesselGridSize", VesselGridSize);

                SymmetryAwareMeasurements = GetBool(node, "symmetryAwareMeasurements", SymmetryAwareMeasurements);
                UseAlternateSkin = GetBool(node, "useAlternateSkin", UseAlternateSkin);
                ShowSnappingPane = GetBool(node, "showSnappingPane", ShowSnappingPane);
                ShowVesselDimensionsPane = GetBool(node, "showVesselDimensionsPane", ShowVesselDimensionsPane);
                ShowMeasurementListPane = GetBool(node, "showMeasurementListPane", ShowMeasurementListPane);
                SelectedMeasurementPaneExpanded = GetBool(node, "selectedMeasurementPaneExpanded", SelectedMeasurementPaneExpanded);
                HideWindowWhileMeasuring = GetBool(node, "hideWindowWhileMeasuring", HideWindowWhileMeasuring);
                RememberIncompleteMeasurementOnRestart = GetBool(node,
                    "rememberIncompleteMeasurementOnRestart",
                    RememberIncompleteMeasurementOnRestart);

                MeasurementSortMode sortMode;
                if (Enum.TryParse(GetString(node, "measurementListSort", MeasurementListSort.ToString()), true, out sortMode))
                    MeasurementListSort = sortMode;
                MeasurementListSortAscending = GetBool(node, "measurementListSortAscending", MeasurementListSortAscending);

                ShortcutToggleMeasurement = LoadShortcut(node, "shortcutToggleMeasurement", ShortcutToggleMeasurement);
                ShortcutCancelMode = LoadShortcut(node, "shortcutCancelMode", ShortcutCancelMode);
                ShortcutNewDistance = LoadShortcut(node, "shortcutNewDistance", ShortcutNewDistance);
                ShortcutNewAngle = LoadShortcut(node, "shortcutNewAngle", ShortcutNewAngle);
                ShortcutEditEndpoints = LoadShortcut(node, "shortcutEditEndpoints", ShortcutEditEndpoints);
                ShortcutToggleLabels = LoadShortcut(node, "shortcutToggleLabels", ShortcutToggleLabels);
                ShortcutDeleteSelected = LoadShortcut(node, "shortcutDeleteSelected", ShortcutDeleteSelected);
                ShortcutCopySelected = LoadShortcut(node, "shortcutCopySelected", ShortcutCopySelected);
                ShortcutUndo = LoadShortcut(node, "shortcutUndo", ShortcutUndo);
                ShortcutRedo = LoadShortcut(node, "shortcutRedo", ShortcutRedo);
                ShortcutRedoAlternate = LoadShortcut(node, "shortcutRedoAlternate", ShortcutRedoAlternate);
                ShortcutSnapModifier = LoadShortcut(node, "shortcutSnapModifier", ShortcutSnapModifier);
                ShortcutAxisX = LoadShortcut(node, "shortcutAxisX", ShortcutAxisX);
                ShortcutAxisY = LoadShortcut(node, "shortcutAxisY", ShortcutAxisY);
                ShortcutAxisZ = LoadShortcut(node, "shortcutAxisZ", ShortcutAxisZ);

                HasWindowPosition = GetBool(node, "hasWindowPosition", HasWindowPosition);
                WindowX = GetFloat(node, "windowX", WindowX);
                WindowY = GetFloat(node, "windowY", WindowY);
                WindowWidth = GetFloat(node, "windowWidth", WindowWidth);
                HasSettingsWindowPosition = GetBool(node, "hasSettingsWindowPosition", HasSettingsWindowPosition);
                SettingsWindowX = GetFloat(node, "settingsWindowX", SettingsWindowX);
                SettingsWindowY = GetFloat(node, "settingsWindowY", SettingsWindowY);

                MeasurementLockMode mode;
                if (Enum.TryParse(GetString(node, "defaultLockMode", DefaultLockMode.ToString()), true, out mode))
                    DefaultLockMode = mode;

                DistanceUnitMode units;
                if (Enum.TryParse(GetString(node, "distanceUnits", DistanceUnits.ToString()), true, out units))
                    DistanceUnits = units;

                DisplayPrecisionMode precision;
                if (Enum.TryParse(GetString(node, "displayPrecision", DisplayPrecision.ToString()), true, out precision))
                    DisplayPrecision = precision;

                Clamp();
            }
            catch (Exception ex)
            {
                Debug.LogError("[TapeMeasure] Failed to load settings: " + ex);
            }
        }

        public void ResetKeyboardShortcuts()
        {
            ShortcutToggleMeasurement = new ShortcutBinding(KeyCode.M);
            ShortcutCancelMode = new ShortcutBinding(KeyCode.Escape);
            ShortcutNewDistance = new ShortcutBinding(KeyCode.None);
            ShortcutNewAngle = new ShortcutBinding(KeyCode.None);
            ShortcutEditEndpoints = new ShortcutBinding(KeyCode.None);
            ShortcutToggleLabels = new ShortcutBinding(KeyCode.None);
            ShortcutDeleteSelected = new ShortcutBinding(KeyCode.Delete);
            ShortcutCopySelected = new ShortcutBinding(KeyCode.C, true);
            ShortcutUndo = new ShortcutBinding(KeyCode.Z, true);
            ShortcutRedo = new ShortcutBinding(KeyCode.Y, true);
            ShortcutRedoAlternate = new ShortcutBinding(KeyCode.Z, true, true);
            ShortcutSnapModifier = new ShortcutBinding(KeyCode.LeftShift);
            ShortcutAxisX = new ShortcutBinding(KeyCode.X, false, false, true);
            ShortcutAxisY = new ShortcutBinding(KeyCode.Y, false, false, true);
            ShortcutAxisZ = new ShortcutBinding(KeyCode.Z, false, false, true);
        }

        private static void SaveShortcut(ConfigNode node, string key, ShortcutBinding binding)
        {
            node.AddValue(key, binding != null ? binding.Serialize() : "None");
        }

        private static ShortcutBinding LoadShortcut(ConfigNode node, string key, ShortcutBinding fallback)
        {
            return ShortcutBinding.Parse(GetString(node, key, null), fallback);
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static float GetFloat(ConfigNode node, string key, float fallback)
        {
            float value;
            string text = node.GetValue(key);
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value : fallback;
        }

        private static bool GetBool(ConfigNode node, string key, bool fallback)
        {
            bool value;
            string text = node.GetValue(key);
            return bool.TryParse(text, out value) ? value : fallback;
        }

        private static string GetString(ConfigNode node, string key, string fallback)
        {
            if (!node.HasValue(key)) return fallback;
            string value = node.GetValue(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
