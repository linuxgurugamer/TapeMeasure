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
        public float SnapPixelRadius = 34f;
        public float VesselGridSize = 0.10f;

        public bool SymmetryAwareMeasurements = false;
        public MeasurementLockMode DefaultLockMode = MeasurementLockMode.PartRelative;
        public DistanceUnitMode DistanceUnits = DistanceUnitMode.Auto;

        // Interface layout/skin preferences. The legacy "Show...Pane" field
        // names are retained for Settings.cfg compatibility; starting in 0.8.7
        // they represent each pane's expanded/collapsed state. The pane header
        // remains visible even when the content is collapsed.
        public bool UseAlternateSkin = false;
        public bool ShowSnappingPane = false;
        public bool ShowVesselDimensionsPane = true;
        public bool ShowMeasurementListPane = true;
        public bool SelectedMeasurementPaneExpanded = true;

        // Window/UI state. Window visibility itself is deliberately not saved;
        // entering the editor should always leave the tool discoverable.
        public bool HasWindowPosition = false;
        public float WindowX = 260f;
        public float WindowY = 90f;
        public bool HasSettingsWindowPosition = false;
        public float SettingsWindowX = 1120f;
        public float SettingsWindowY = 90f;
        // Retains the original config key for backward compatibility. In 0.8.0
        // this controls whether the separate Settings window is open.
        public bool ShowSettingsPanel = false;

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
                node.AddValue("snapPixelRadius", F(SnapPixelRadius));
                node.AddValue("vesselGridSize", F(VesselGridSize));

                // Legacy 0.8.3 key retained so rolling back does not silently
                // lose the user's attachment-node preference.
                node.AddValue("snapToAttachNodes", SnappingEnabled && SnapAttachmentNodes);
                node.AddValue("symmetryAwareMeasurements", SymmetryAwareMeasurements);
                node.AddValue("defaultLockMode", DefaultLockMode.ToString());
                node.AddValue("distanceUnits", DistanceUnits.ToString());
                node.AddValue("useAlternateSkin", UseAlternateSkin);
                node.AddValue("showSnappingPane", ShowSnappingPane);
                node.AddValue("showVesselDimensionsPane", ShowVesselDimensionsPane);
                node.AddValue("showMeasurementListPane", ShowMeasurementListPane);
                node.AddValue("selectedMeasurementPaneExpanded", SelectedMeasurementPaneExpanded);
                node.AddValue("hasWindowPosition", HasWindowPosition);
                node.AddValue("windowX", F(WindowX));
                node.AddValue("windowY", F(WindowY));
                node.AddValue("hasSettingsWindowPosition", HasSettingsWindowPosition);
                node.AddValue("settingsWindowX", F(SettingsWindowX));
                node.AddValue("settingsWindowY", F(SettingsWindowY));
                node.AddValue("showSettingsPanel", ShowSettingsPanel);

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
                SnapPixelRadius = GetFloat(node, "snapPixelRadius", SnapPixelRadius);
                VesselGridSize = GetFloat(node, "vesselGridSize", VesselGridSize);

                SymmetryAwareMeasurements = GetBool(node, "symmetryAwareMeasurements", SymmetryAwareMeasurements);
                UseAlternateSkin = GetBool(node, "useAlternateSkin", UseAlternateSkin);
                ShowSnappingPane = GetBool(node, "showSnappingPane", ShowSnappingPane);
                ShowVesselDimensionsPane = GetBool(node, "showVesselDimensionsPane", ShowVesselDimensionsPane);
                ShowMeasurementListPane = GetBool(node, "showMeasurementListPane", ShowMeasurementListPane);
                SelectedMeasurementPaneExpanded = GetBool(node, "selectedMeasurementPaneExpanded", SelectedMeasurementPaneExpanded);
                HasWindowPosition = GetBool(node, "hasWindowPosition", HasWindowPosition);
                WindowX = GetFloat(node, "windowX", WindowX);
                WindowY = GetFloat(node, "windowY", WindowY);
                HasSettingsWindowPosition = GetBool(node, "hasSettingsWindowPosition", HasSettingsWindowPosition);
                SettingsWindowX = GetFloat(node, "settingsWindowX", SettingsWindowX);
                SettingsWindowY = GetFloat(node, "settingsWindowY", SettingsWindowY);
                ShowSettingsPanel = GetBool(node, "showSettingsPanel", ShowSettingsPanel);

                MeasurementLockMode mode;
                if (Enum.TryParse(GetString(node, "defaultLockMode", DefaultLockMode.ToString()), true, out mode))
                    DefaultLockMode = mode;

                DistanceUnitMode units;
                if (Enum.TryParse(GetString(node, "distanceUnits", DistanceUnits.ToString()), true, out units))
                    DistanceUnits = units;

                Clamp();
            }
            catch (Exception ex)
            {
                Debug.LogError("[TapeMeasure] Failed to load settings: " + ex);
            }
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
