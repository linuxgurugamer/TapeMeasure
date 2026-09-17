using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using KSP.UI.Screens;
using ClickThroughFix;
using ToolbarControl_NS;

namespace TapeMeasure
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public sealed class TapeMeasure : MonoBehaviour
    {
        internal const string ModId = "TapeMeasure_NS";
        internal const string ModName = "TapeMeasure";
        private const string LogPrefix = "[TapeMeasure] ";
        private const string ToolbarButtonId = "TapeMeasureButton";
        private const string IconLargeNormal = "TapeMeasure/Textures/icon_38";
        private const string IconSmallNormal = "TapeMeasure/Textures/icon_24";
        private const string IconLargeMeasuring = "TapeMeasure/Textures/icon_measure_38";
        private const string IconSmallMeasuring = "TapeMeasure/Textures/icon_measure_24";
        private const string CursorMeasure = "TapeMeasure/Textures/cursor_measure";
        private const float EndpointGrabPixels = 24f;
        private const string EditorInputLockId = "TapeMeasure.EditorInputLock";
        private const string MeasurementNameControlName = "TapeMeasure.MeasurementName";

        private readonly List<MeasurementRecord> _measurements = new List<MeasurementRecord>();
        private readonly MeasurementUndoStack _measurementUndo = new MeasurementUndoStack();
        private readonly SnapResolver _snapResolver = new SnapResolver();
        private MeasurementRenderer _renderer;
        private MeasurementPersistence _persistence;
        private TapeMeasureSettings _settings;
        private ToolbarControl _toolbarControl;
        private MeasurementRecord _selectedMeasurement;
        // The measurement currently receiving newly placed points. This is
        // deliberately separate from list/UI selection so creating a
        // measurement never auto-opens the Selected Measurement pane.
        private MeasurementRecord _activeMeasurement;
        private ShipConstruct _currentShip;
        private VesselDimensions _vesselDimensions;

        private bool _windowVisible = false;
        private bool _measurementMode;
        private bool _endpointEditMode;
        private bool _ownsSoftLock;
        private bool _persistenceDirty;
        private bool _settingsDirty;
        private bool _showSettings;
        private bool _measurementNameFieldFocused;

        private MeasurementEndpoint _dragEndpoint = MeasurementEndpoint.None;
        private string _dragSnapNodeId;
        private string _dragSnapDescription;
        private string _activeSnapDescription;
        private string _dragPartTitle;
        private bool _dragMoved;
        private MeasurementUndoState _dragUndoState;

        private MeasurementUndoState _nameEditUndoState;
        private string _nameEditMeasurementId;

        private string _currentShipName = string.Empty;
        private string _statusMessage = "Click a measurement value to copy it to the clipboard.";
        private float _saveAfterRealtime;
        private float _settingsSaveAfterRealtime;
        private float _statusUntilRealtime;

        private Rect _windowRect = new Rect(260f, 90f, 600f, 0f);
        private Rect _settingsWindowRect = new Rect(1120f, 90f, 560f, 0f);
        private Vector2 _measurementScroll;
        private Vector2 _settingsScroll;
        private GUIStyle _statusStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _selectedNameStyle;
        private GUIStyle _worldLabelStyle;
        private GUIStyle _valueButtonStyle;
        private GUIStyle _deleteButtonStyle;
        private GUISkin _styleSkin;
        private Texture2D _measurementCursorTexture;
        private bool _measurementCursorVisible;

        private void Awake()
        {
            Debug.Log(LogPrefix + "Awake");
        }

        private void Start()
        {
            _settings = new TapeMeasureSettings();
            _showSettings = _settings.ShowSettingsPanel;

            if (_settings.HasWindowPosition)
            {
                _windowRect.x = _settings.WindowX;
                _windowRect.y = _settings.WindowY;
            }
            if (_settings.HasSettingsWindowPosition)
            {
                _settingsWindowRect.x = _settings.SettingsWindowX;
                _settingsWindowRect.y = _settings.SettingsWindowY;
            }
            ClampWindowsToScreen();

            _renderer = new MeasurementRenderer();
            _persistence = new MeasurementPersistence();
            LoadMeasurementCursor();
            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            CreateToolbarButton();
        }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null) return;
            EnsureShipContext();
            RemoveInvalidMeasurements();

            HandleKeyboardShortcuts();

            // Cursor visibility depends on both measurement mode and whether
            // the pointer is over one of TapeMeasure's own windows. Re-evaluate
            // every frame so the normal cursor comes back immediately over UI.
            UpdateMeasurementCursor();

            if (_endpointEditMode)
            {
                if (Input.GetMouseButtonDown(0)) BeginEndpointDrag();
                if (_dragEndpoint != MeasurementEndpoint.None && Input.GetMouseButton(0)) UpdateEndpointDrag();
                if (_dragEndpoint != MeasurementEndpoint.None && Input.GetMouseButtonUp(0)) EndEndpointDrag();
            }
            else if (_measurementMode && Input.GetMouseButtonDown(0))
            {
                TrySelectPoint();
            }

            if (_persistenceDirty && Time.realtimeSinceStartup >= _saveAfterRealtime) SaveMeasurements();
            if (_settingsDirty && Time.realtimeSinceStartup >= _settingsSaveAfterRealtime) SaveSettings();

            Camera camera = GetEditorCamera();
            if (_renderer != null)
            {
                _renderer.Update(_measurements, camera, _windowVisible,
                    _selectedMeasurement != null ? _selectedMeasurement.Id : null,
                    _settings);
                UpdateMeasurementPreview(camera);
            }
        }

        private void OnGUI()
        {
            if (!_windowVisible || !HighLogic.LoadedSceneIsEditor) return;

            GUISkin requestedSkin = (_settings != null && _settings.UseAlternateSkin && GUI.skin != null)
                ? GUI.skin
                : HighLogic.Skin;
            if (requestedSkin != null)
                GUI.skin = requestedSkin;
            EnsureStyles();

            _windowRect = ClickThruBlocker.GUILayoutWindow(
                GetInstanceID(), _windowRect, DrawWindow, "Tape Measure", GUILayout.Width(600f));
            TrackWindowPosition();

            if (_showSettings)
            {
                _settingsWindowRect = ClickThruBlocker.GUILayoutWindow(
                    GetInstanceID() + 1, _settingsWindowRect, DrawSettingsWindow,
                    "TapeMeasure Settings", GUILayout.Width(560f));
                TrackSettingsWindowPosition();
            }

            if (_settings != null && _settings.ShowWorldLabels) DrawWorldLabels();
            DrawMeasurementCursor();
        }

        private void OnDestroy()
        {
            ReleaseMeasurementCursor();
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            SetMeasurementMode(false);
            SetEndpointEditMode(false);
            if (_persistenceDirty) SaveMeasurements();
            if (_settings != null)
            {
                CaptureWindowPreferences();
                _settings.Save();
                _settingsDirty = false;
            }
            if (_toolbarControl != null)
            {
                Destroy(_toolbarControl);
                _toolbarControl = null;
            }
            if (_renderer != null) { _renderer.Destroy(); _renderer = null; }
            Debug.Log(LogPrefix + "Destroyed");
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();
            DrawMeasurementControls();

            GUILayout.Space(6f);
            DrawSnappingPanel();

            GUILayout.Space(6f);
            DrawDimensionsPanel();

            GUILayout.Space(7f);
            DrawMeasurementListPanel();

            // Selection is explicit. The Selected Measurement pane is not
            // shown at all until the user clicks a measurement row.
            if (_selectedMeasurement != null)
            {
                GUILayout.Space(8f);
                DrawSelectedMeasurementDetails();
            }
            else
            {
                _measurementNameFieldFocused = false;
                CommitPendingNameUndo();
            }

            GUILayout.Space(8f);
            DrawCollectionButtons();
            GUILayout.Space(5f);
            DrawStatusLine();
            GUILayout.EndVertical();
            // Controls consume their own mouse events first; any remaining area
            // of the window can be used to drag it, not just the title bar.
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 10000f));
        }

        private void DrawSettingsWindow(int windowId)
        {
            GUILayout.BeginVertical();
            float settingsHeight = Mathf.Clamp(Screen.height - 180f, 320f, 700f);
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, GUILayout.Height(settingsHeight));
            DrawSettingsPanel();
            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            if (GUILayout.Button("Close", GUILayout.Height(28f)))
            {
                _showSettings = false;
                _settings.ShowSettingsPanel = false;
                MarkSettingsDirty(true);
            }
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 10000f));
        }

        private void DrawMeasurementControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_measurementMode ? "Stop Measuring" : "Start Measuring", GUILayout.Height(30f)))
                SetMeasurementMode(!_measurementMode);

            if (GUILayout.Button("New Distance", GUILayout.Height(30f), GUILayout.Width(125f)))
            {
                _activeMeasurement = CreateMeasurement(MeasurementKind.Distance);
                SetMeasurementMode(true);
                SetStatus("New distance measurement ready. Click point A. It is not selected until you click its row.", 5f);
            }

            if (GUILayout.Button("New Angle", GUILayout.Height(30f), GUILayout.Width(110f)))
            {
                _activeMeasurement = CreateMeasurement(MeasurementKind.Angle);
                SetMeasurementMode(true);
                SetStatus("New angle measurement ready. Click point A, then vertex B, then point C. It is not selected automatically.", 6f);
            }

            bool showSettings = GUILayout.Toggle(_showSettings, "Settings", GUI.skin.button, GUILayout.Height(30f), GUILayout.Width(110f));
            if (showSettings != _showSettings)
            {
                _showSettings = showSettings;
                _settings.ShowSettingsPanel = showSettings;
                MarkSettingsDirty();
            }
            GUILayout.EndHorizontal();

            string instruction = (_measurementMode || _endpointEditMode)
                ? GetMeasurementInstruction()
                : "Measurement mode is off.";
            if (_measurementMode)
                instruction += " Hold Shift to temporarily enable the configured snap targets.";
            GUILayout.Label(instruction, _statusStyle);

            bool labels = GUILayout.Toggle(_settings.ShowWorldLabels,
                "Show measurement labels in the editor view");
            if (labels != _settings.ShowWorldLabels)
            {
                _settings.ShowWorldLabels = labels;
                MarkSettingsDirty();
            }
        }

        private void DrawSettingsPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            bool changed = false;
            GUILayout.Label("Measurement behavior", _statusStyle);

            GUILayout.Label("Snapping", _statusStyle);
            changed |= DrawSnappingOptions();

            bool sym = GUILayout.Toggle(_settings.SymmetryAwareMeasurements,
                "Create symmetry counterpart measurements");
            if (sym != _settings.SymmetryAwareMeasurements)
            {
                _settings.SymmetryAwareMeasurements = sym;
                changed = true;
                SetStatus(sym ? "Symmetry-aware measurements enabled." : "Symmetry-aware measurements disabled.", 4f);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("New measurement lock:", GUILayout.Width(150f));
            int mode = GUILayout.Toolbar((int)_settings.DefaultLockMode,
                new string[] { "Part-relative", "Vessel-relative" }, GUILayout.Width(310f));
            GUILayout.EndHorizontal();
            if (mode != (int)_settings.DefaultLockMode)
            {
                _settings.DefaultLockMode = (MeasurementLockMode)mode;
                changed = true;
            }

            GUILayout.Space(7f);
            GUILayout.Label("Interface", _statusStyle);

            bool alternateSkin = GUILayout.Toggle(_settings.UseAlternateSkin,
                "Use alternate KSP skin");
            if (alternateSkin != _settings.UseAlternateSkin)
            {
                _settings.UseAlternateSkin = alternateSkin;
                changed = true;
                // Force all cached styles to be rebuilt from the newly
                // selected GUISkin on the next OnGUI pass.
                _styleSkin = null;
                _statusStyle = null;
                _tableHeaderStyle = null;
                _selectedNameStyle = null;
                _worldLabelStyle = null;
                _valueButtonStyle = null;
                _deleteButtonStyle = null;
            }

            bool snappingPane = GUILayout.Toggle(_settings.ShowSnappingPane,
                "Expand snapping pane");
            if (snappingPane != _settings.ShowSnappingPane)
            {
                _settings.ShowSnappingPane = snappingPane;
                RequestMainWindowResize();
                changed = true;
            }

            bool dimensionsPane = GUILayout.Toggle(_settings.ShowVesselDimensionsPane,
                "Expand automatic vessel dimensions pane");
            if (dimensionsPane != _settings.ShowVesselDimensionsPane)
            {
                _settings.ShowVesselDimensionsPane = dimensionsPane;
                RequestMainWindowResize();
                changed = true;
            }

            bool listPane = GUILayout.Toggle(_settings.ShowMeasurementListPane,
                "Expand measurement list pane");
            if (listPane != _settings.ShowMeasurementListPane)
            {
                _settings.ShowMeasurementListPane = listPane;
                RequestMainWindowResize();
                changed = true;
            }

            bool selectedPane = GUILayout.Toggle(_settings.SelectedMeasurementPaneExpanded,
                "Expand Selected Measurement pane when a measurement is selected");
            if (selectedPane != _settings.SelectedMeasurementPaneExpanded)
            {
                _settings.SelectedMeasurementPaneExpanded = selectedPane;
                RequestMainWindowResize();
                changed = true;
            }

            GUILayout.Label("Pane headings remain visible when collapsed. The Selected Measurement pane itself appears only after an explicit row selection.");

            GUILayout.Space(7f);
            GUILayout.Label("Display", _statusStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Units:", GUILayout.Width(130f));
            int units = GUILayout.Toolbar(
                (int)_settings.DistanceUnits,
                new string[] { "Auto", "m", "cm", "mm", "ft/in" },
                GUILayout.Width(390f));
            GUILayout.EndHorizontal();
            if (units != (int)_settings.DistanceUnits)
            {
                _settings.DistanceUnits = (DistanceUnitMode)units;
                changed = true;
            }

            bool lines = GUILayout.Toggle(_settings.ShowMeasurementLines,
                "Show measurement lines");
            if (lines != _settings.ShowMeasurementLines)
            {
                _settings.ShowMeasurementLines = lines;
                changed = true;
            }

            GUI.enabled = _settings.ShowWorldLabels;
            bool labelValues = GUILayout.Toggle(_settings.ShowWorldLabelValues,
                "Show measurement dimension values in editor labels");
            GUI.enabled = true;
            if (labelValues != _settings.ShowWorldLabelValues)
            {
                _settings.ShowWorldLabelValues = labelValues;
                changed = true;
            }

            bool guides = GUILayout.Toggle(_settings.ShowMeasurementGuides,
                "Show X/Y/Z projection guides for the selected distance measurement");
            if (guides != _settings.ShowMeasurementGuides)
            {
                _settings.ShowMeasurementGuides = guides;
                changed = true;
            }
            if (_settings.ShowMeasurementGuides)
                GUILayout.Label("Guide colors: X = red, Y = green, Z = blue. Guides project from both endpoints.");

            GUILayout.Space(5f);
            GUILayout.Label("Marker appearance", _statusStyle);

            changed |= DrawSlider("Marker size", ref _settings.MarkerSizeMultiplier, 0.25f, 4f, "F2", "x");
            changed |= DrawSlider("Line width", ref _settings.LineWidthMultiplier, 0.25f, 5f, "F2", "x");

            bool emphasize = GUILayout.Toggle(_settings.EmphasizeSelected,
                "Emphasize selected measurement (inactive measurements are thinner/faded)");
            if (emphasize != _settings.EmphasizeSelected)
            {
                _settings.EmphasizeSelected = emphasize;
                changed = true;
            }

            if (_settings.EmphasizeSelected)
            {
                changed |= DrawSlider("Selected marker", ref _settings.SelectedMarkerMultiplier, 1f, 3f, "F2", "x");
                changed |= DrawSlider("Selected line", ref _settings.SelectedLineMultiplier, 1f, 4f, "F2", "x");
                changed |= DrawSlider("Inactive opacity", ref _settings.InactiveOpacity, 0.10f, 1f, "F2", string.Empty);
            }

            GUILayout.Space(7f);
            GUILayout.Label("Keyboard shortcuts", _statusStyle);
            GUILayout.Label("M - toggle measurement mode");
            GUILayout.Label("Esc - exit measurement or endpoint-edit mode");
            GUILayout.Label("Delete - delete the selected measurement");
            GUILayout.Label("Ctrl+C - copy the selected measurement value");
            GUILayout.Label("Ctrl+Z - undo the last TapeMeasure measurement change");
            GUILayout.Label("Shift - temporarily enable the configured snap targets while held");

            if (changed) MarkSettingsDirty();
            GUILayout.EndVertical();
        }

        private static bool DrawSlider(string label, ref float value, float min, float max, string format, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(130f));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220f));
            GUILayout.Label(next.ToString(format) + suffix, GUILayout.Width(55f));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(next - value) < 0.0001f) return false;
            value = next;
            return true;
        }

        private static bool DrawToggle(ref bool value, string label, params GUILayoutOption[] options)
        {
            bool next = GUILayout.Toggle(value, label, options);
            if (next == value) return false;
            value = next;
            return true;
        }

        private string GetMeasurementInstruction()
        {
            if (_endpointEditMode)
            {
                if (_dragEndpoint != MeasurementEndpoint.None)
                    return "Dragging " + GetEndpointLabel(_dragEndpoint, _selectedMeasurement) +
                        ". Release the mouse button to place it. Esc exits endpoint editing.";
                return "Endpoint editing: drag the visible A/B" +
                    (_selectedMeasurement != null && _selectedMeasurement.Kind == MeasurementKind.Angle ? "/C" : string.Empty) +
                    " marker. Esc exits endpoint editing.";
            }

            MeasurementRecord measurement = _activeMeasurement;
            if (measurement == null)
                return "Click point A to start a distance measurement. Esc exits measurement mode.";
            if (measurement.IsComplete)
                return measurement.Kind == MeasurementKind.Angle
                    ? "Click point A to start another angle measurement. Esc exits measurement mode."
                    : "Click point A to start another distance measurement. Esc exits measurement mode.";
            if (measurement.Kind == MeasurementKind.Angle)
            {
                if (!measurement.HasPointA) return "Angle: click point A. B will be the vertex. Esc exits measurement mode.";
                if (!measurement.HasPointB) return "Angle: click vertex B. Esc exits measurement mode.";
                return "Angle: click point C. The angle is A-B-C. Esc exits measurement mode.";
            }
            if (!measurement.HasPointA) return "Distance: click point A. Esc exits measurement mode.";
            return "Distance: click point B. Esc exits measurement mode.";
        }

        private bool DrawPaneHeader(string title, ref bool expanded, string suffix)
        {
            string text = (expanded ? "\u25BC " : "\u25B6 ") + title;
            if (!string.IsNullOrEmpty(suffix))
                text += "    " + suffix;

            // Use label styling for collapsible pane headings so they read as
            // section labels rather than normal command buttons.
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.alignment = TextAnchor.MiddleLeft;
            headerStyle.fontStyle = FontStyle.Bold;

            if (GUILayout.Button(text, headerStyle, GUILayout.Height(25f), GUILayout.ExpandWidth(true)))
            {
                expanded = !expanded;
                RequestMainWindowResize();
                MarkSettingsDirty();
            }

            return expanded;
        }

        private void RequestMainWindowResize()
        {
            // GUILayout.Window otherwise tends to retain the previous Rect
            // height after a pane is collapsed. Resetting height allows the
            // next layout pass to fit the currently visible content.
            _windowRect.height = 0f;
        }

        private void DrawSnappingPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (!DrawPaneHeader("Snapping", ref _settings.ShowSnappingPane,
                _settings.SnappingEnabled ? "On" : "Off"))
            {
                GUILayout.EndVertical();
                return;
            }

            bool changed = DrawSnappingOptions();
            if (changed)
                MarkSettingsDirty();

            GUILayout.EndVertical();
        }

        private bool DrawSnappingOptions()
        {
            bool changed = false;

            bool snapping = GUILayout.Toggle(_settings.SnappingEnabled,
                "Enable snapping (Shift temporarily enables the configured targets)");
            if (snapping != _settings.SnappingEnabled)
            {
                _settings.SnappingEnabled = snapping;
                changed = true;
                SetStatus(snapping ? "Snapping enabled." :
                    "Snapping disabled. Hold Shift for temporary snapping.", 4f);
            }

            GUILayout.Label("Snap targets (nearest enabled target within the snap radius wins):");
            GUILayout.BeginHorizontal();
            changed |= DrawToggle(ref _settings.SnapPartOrigin, "Part origin", GUILayout.Width(260f));
            changed |= DrawToggle(ref _settings.SnapAttachmentNodes, "Attachment node", GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            changed |= DrawToggle(ref _settings.SnapSurfaceAttachmentPoint, "Surface attachment point", GUILayout.Width(260f));
            changed |= DrawToggle(ref _settings.SnapPartCenter, "Part center", GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            changed |= DrawToggle(ref _settings.SnapVesselRoot, "Vessel root", GUILayout.Width(260f));
            changed |= DrawToggle(ref _settings.SnapCenterOfMass, "Center of Mass", GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            changed |= DrawToggle(ref _settings.SnapCenterOfLift, "Center of Lift", GUILayout.Width(260f));
            changed |= DrawToggle(ref _settings.SnapCenterOfThrust, "Center of Thrust", GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            changed |= DrawToggle(ref _settings.SnapVesselAxisGrid, "Vessel Axis / Grid snapping");

            changed |= DrawSlider("Snap radius", ref _settings.SnapPixelRadius, 8f, 100f, "F0", " px");
            if (_settings.SnapVesselAxisGrid)
            {
                changed |= DrawSlider("Grid spacing", ref _settings.VesselGridSize, 0.01f, 2f, "F2", " m");
                GUILayout.Label("Axis snapping uses the vessel X/Y/Z axes through the previous endpoint; grid snapping uses vessel-local coordinates.");
            }

            return changed;
        }

        private void DrawDimensionsPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (!DrawPaneHeader("Automatic vessel dimensions", ref _settings.ShowVesselDimensionsPane, null))
            {
                GUILayout.EndVertical();
                return;
            }

            if (!_vesselDimensions.IsValid)
            {
                GUILayout.Label("No visible vessel geometry is available yet.");
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Length (Z)", GUILayout.Width(72f));
            DrawDistanceCopyButton(_vesselDimensions.Length, "Length", 96f);
            GUILayout.Label("Width (X)", GUILayout.Width(68f));
            DrawDistanceCopyButton(_vesselDimensions.Width, "Width", 96f);
            GUILayout.Label("Height (Y)", GUILayout.Width(70f));
            DrawDistanceCopyButton(_vesselDimensions.Height, "Height", 96f);
            GUILayout.EndHorizontal();

            string boxText = FormatBoundingBox(_vesselDimensions.Size);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bounding box (X x Y x Z):", GUILayout.Width(170f));
            if (GUILayout.Button(boxText, _valueButtonStyle, GUILayout.Width(260f)))
                CopyToClipboard(boxText, "Bounding box");
            GUILayout.EndHorizontal();
            GUILayout.Label("Based on visible active-part geometry.");
            GUILayout.EndVertical();
        }

        private void OnEditorShipModified(ShipConstruct ship)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            _snapResolver.Invalidate();
            RefreshVesselDimensions(ship);
        }

        private void RefreshVesselDimensions(ShipConstruct ship = null)
        {
            ShipConstruct targetShip = ship ?? _currentShip;
            if (targetShip == null || EditorLogic.fetch == null)
            {
                _vesselDimensions = new VesselDimensions();
                return;
            }

            _vesselDimensions = VesselDimensionsCalculator.Calculate(
                targetShip,
                EditorLogic.VesselRotation);
        }

        private string FormatBoundingBox(Vector3 size)
        {
            return FormatDistance(size.x) + " x " +
                   FormatDistance(size.y) + " x " +
                   FormatDistance(size.z);
        }

        private void DrawMeasurementListPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            string countText = _measurements.Count + (_measurements.Count == 1 ? " measurement" : " measurements");
            if (!DrawPaneHeader("Measurement list", ref _settings.ShowMeasurementListPane, countText))
            {
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Type", _tableHeaderStyle, GUILayout.Width(64f));
            GUILayout.Label("Name", _tableHeaderStyle, GUILayout.Width(130f));
            GUILayout.Space(4f);
            GUILayout.Label("Lock", _tableHeaderStyle, GUILayout.Width(60f));
            GUILayout.Label("Value", _tableHeaderStyle, GUILayout.Width(68f));
            GUILayout.Label("X", _tableHeaderStyle, GUILayout.Width(54f));
            GUILayout.Label("Y", _tableHeaderStyle, GUILayout.Width(54f));
            GUILayout.Label("Z", _tableHeaderStyle, GUILayout.Width(54f));
            GUILayout.Label(string.Empty, GUILayout.Width(24f));
            GUILayout.EndHorizontal();

            float height = Mathf.Clamp(_measurements.Count * 28f + 4f, 62f, 185f);
            _measurementScroll = GUILayout.BeginScrollView(_measurementScroll, GUILayout.Height(height));
            if (_measurements.Count == 0)
                GUILayout.Label("No measurements. Click New Distance or New Angle.");
            else
            {
                for (int i = 0; i < _measurements.Count; ++i)
                    if (DrawMeasurementRow(_measurements[i])) break;
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Click anywhere in a row to select and highlight that measurement in the editor. Values are clickable and copy to the clipboard.");
            GUILayout.EndVertical();
        }

        private bool DrawMeasurementRow(MeasurementRecord m)
        {
            const float typeWidth = 64f;
            const float nameWidth = 130f;
            const float lockWidth = 60f;
            const float valueWidth = 68f;
            const float axisWidth = 54f;
            const float deleteWidth = 24f;
            const float gap = 2f;
            const float nameGap = 6f;

            Rect row = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));

            // Calculate all cells before handling row selection so the delete X
            // can be excluded from the row-select hit area.
            float x = row.x;
            Rect typeRect = new Rect(x, row.y, typeWidth, row.height);
            x += typeWidth + gap;
            Rect nameRect = new Rect(x, row.y, nameWidth, row.height);
            x += nameWidth + nameGap;
            Rect lockRect = new Rect(x, row.y, lockWidth, row.height);
            x += lockWidth + gap;
            Rect valueRect = new Rect(x, row.y, valueWidth, row.height);
            x += valueWidth + gap;
            Rect xRect = new Rect(x, row.y, axisWidth, row.height);
            x += axisWidth + gap;
            Rect yRect = new Rect(x, row.y, axisWidth, row.height);
            x += axisWidth + gap;
            Rect zRect = new Rect(x, row.y, axisWidth, row.height);
            x += axisWidth + gap;
            Rect deleteRect = new Rect(x, row.y, deleteWidth, row.height);

            // Selecting on mouse-down makes the 3D highlight react immediately.
            // The delete X is deliberately excluded so deleting an unselected
            // row never selects/highlights it first.
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                row.Contains(Event.current.mousePosition) &&
                !deleteRect.Contains(Event.current.mousePosition))
            {
                SelectMeasurement(m);
            }

            GUI.Label(typeRect, m.Kind == MeasurementKind.Angle ? "Angle" : "Distance");

            GUIStyle nameStyle = m == _selectedMeasurement ? _selectedNameStyle : GUI.skin.button;
            if (GUI.Button(nameRect, m.Name, nameStyle))
                SelectMeasurement(m);

            GUI.Label(lockRect, m.LockMode == MeasurementLockMode.PartRelative ? "Part" : "Vessel");

            if (m.IsComplete)
            {
                if (m.Kind == MeasurementKind.Angle)
                {
                    string angleText = FormatAngle(m.AngleDegrees);
                    if (GUI.Button(valueRect, angleText, _valueButtonStyle))
                        CopyToClipboard(angleText, "Angle");
                    GUI.Label(xRect, "--");
                    GUI.Label(yRect, "--");
                    GUI.Label(zRect, "--");
                }
                else
                {
                    Vector3 axis = m.AxisDelta(EditorLogic.VesselRotation);
                    DrawDistanceCopyButton(valueRect, m.Distance, "Distance");
                    DrawDistanceCopyButton(xRect, Mathf.Abs(axis.x), "X");
                    DrawDistanceCopyButton(yRect, Mathf.Abs(axis.y), "Y");
                    DrawDistanceCopyButton(zRect, Mathf.Abs(axis.z), "Z");
                }
            }
            else
            {
                GUI.Label(valueRect, "--");
                GUI.Label(xRect, "--");
                GUI.Label(yRect, "--");
                GUI.Label(zRect, "--");
            }

            bool delete = GUI.Button(deleteRect, "X", _deleteButtonStyle);
            if (!delete) return false;
            DeleteMeasurement(m);
            return true;
        }

        private void DrawSelectedMeasurementDetails()
        {
            _measurementNameFieldFocused = false;

            if (_nameEditUndoState != null &&
                (_selectedMeasurement == null || _selectedMeasurement.Id != _nameEditMeasurementId))
                CommitPendingNameUndo();

            if (_selectedMeasurement == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            string suffix = _selectedMeasurement.Name;

            // Selected Measurement gets its own header row so Clear Selection
            // lives at the top-right of the pane rather than with collection
            // commands below the measurement list.
            GUILayout.BeginHorizontal();
            bool expanded = _settings.SelectedMeasurementPaneExpanded;
            string headerText = (expanded ? "\u25BC " : "\u25B6 ") + "Selected measurement";
            if (!string.IsNullOrEmpty(suffix))
                headerText += "    " + suffix;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.alignment = TextAnchor.MiddleLeft;
            headerStyle.fontStyle = FontStyle.Bold;

            if (GUILayout.Button(headerText, headerStyle, GUILayout.Height(25f), GUILayout.ExpandWidth(true)))
            {
                _settings.SelectedMeasurementPaneExpanded = !_settings.SelectedMeasurementPaneExpanded;
                RequestMainWindowResize();
                MarkSettingsDirty();
            }

            if (GUILayout.Button("Clear Selection", GUILayout.Width(110f), GUILayout.Height(25f)))
            {
                ClearSelection();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            if (!_settings.SelectedMeasurementPaneExpanded)
            {
                CommitPendingNameUndo();
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Type:", GUILayout.Width(70f));
            GUILayout.Label(_selectedMeasurement.Kind == MeasurementKind.Angle ? "Angle (A-B-C, B is vertex)" : "Distance");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(70f));
            GUI.SetNextControlName(MeasurementNameControlName);
            string newName = GUILayout.TextField(_selectedMeasurement.Name, GUILayout.Width(300f));
            _measurementNameFieldFocused = GUI.GetNameOfFocusedControl() == MeasurementNameControlName;
            if (newName != _selectedMeasurement.Name)
            {
                if (_nameEditUndoState == null || _nameEditMeasurementId != _selectedMeasurement.Id)
                {
                    CommitPendingNameUndo();
                    _nameEditUndoState = CaptureMeasurementUndoState();
                    _nameEditMeasurementId = _selectedMeasurement.Id;
                }

                _selectedMeasurement.Name = NormalizeMeasurementName(newName);
                MarkPersistenceDirty();
            }
            GUILayout.EndHorizontal();

            if (!_measurementNameFieldFocused)
                CommitPendingNameUndo();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Lock:", GUILayout.Width(70f));
            int lockMode = GUILayout.Toolbar((int)_selectedMeasurement.LockMode,
                new string[] { "Part-relative", "Vessel-relative" }, GUILayout.Width(260f));
            if (lockMode != (int)_selectedMeasurement.LockMode)
            {
                CommitPendingNameUndo();
                PushMeasurementUndo("Change lock mode");
                _selectedMeasurement.SetLockMode((MeasurementLockMode)lockMode, _currentShip);
                MarkPersistenceDirty(true);
                SetStatus(_selectedMeasurement.Name + " changed to " +
                    (_selectedMeasurement.LockMode == MeasurementLockMode.PartRelative ? "Part-relative" : "Vessel-relative") + " locking.", 5f);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = _selectedMeasurement.IsComplete;
            if (GUILayout.Button(_endpointEditMode ? "Stop Editing Endpoints" : "Edit Endpoints",
                GUILayout.Width(185f)))
            {
                SetEndpointEditMode(!_endpointEditMode);
            }
            GUI.enabled = true;
            GUILayout.Label(_selectedMeasurement.Kind == MeasurementKind.Angle
                ? "Drag A, B, or C in the editor view."
                : "Drag A or B in the editor view.");
            GUILayout.EndHorizontal();

            DrawPointRow("Point A", _selectedMeasurement.PointA);
            DrawPointRow(_selectedMeasurement.Kind == MeasurementKind.Angle ? "Vertex B" : "Point B", _selectedMeasurement.PointB);
            if (_selectedMeasurement.Kind == MeasurementKind.Angle) DrawPointRow("Point C", _selectedMeasurement.PointC);

            if (_selectedMeasurement.IsComplete)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Click any displayed value to copy it to the clipboard.");
                if (_selectedMeasurement.Kind == MeasurementKind.Angle)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Angle", GUILayout.Width(70f));
                    DrawAngleCopyButton(_selectedMeasurement.AngleDegrees, "Angle", 110f);
                    GUILayout.Label("Angle at B between BA and BC.");
                    GUILayout.EndHorizontal();
                }
                else
                {
                    Vector3 axis = _selectedMeasurement.AxisDelta(EditorLogic.VesselRotation);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Distance", GUILayout.Width(70f));
                    DrawDistanceCopyButton(_selectedMeasurement.Distance, "Distance", 100f);
                    GUILayout.Label("X", GUILayout.Width(18f));
                    DrawDistanceCopyButton(Mathf.Abs(axis.x), "X", 95f);
                    GUILayout.Label("Y", GUILayout.Width(18f));
                    DrawDistanceCopyButton(Mathf.Abs(axis.y), "Y", 95f);
                    GUILayout.Label("Z", GUILayout.Width(18f));
                    DrawDistanceCopyButton(Mathf.Abs(axis.z), "Z", 95f);
                    GUILayout.EndHorizontal();
                    GUILayout.Label("X/Y/Z are absolute distances along the vessel's editor coordinate axes.");
                }
            }
            else GUILayout.Label(GetIncompleteMeasurementText(_selectedMeasurement));

            GUILayout.EndVertical();
        }

        private static string GetIncompleteMeasurementText(MeasurementRecord m)
        {
            if (m == null) return "No measurement selected.";
            if (m.Kind == MeasurementKind.Angle)
            {
                if (!m.HasPointA) return "Select point A.";
                if (!m.HasPointB) return "Select vertex B.";
                return "Select point C to complete the angle.";
            }
            if (!m.HasPointA) return "Select point A.";
            return "Select point B to complete the distance.";
        }

        private void DrawCollectionButtons()
        {
            GUILayout.BeginHorizontal();

            GUI.enabled = _measurementUndo.CanUndo;
            string undoLabel = _measurementUndo.CanUndo
                ? "Undo (" + _measurementUndo.Count + ")"
                : "Undo";
            if (GUILayout.Button(undoLabel, GUILayout.Width(95f)))
                UndoLastMeasurementChange();

            GUI.enabled = _selectedMeasurement != null;
            if (GUILayout.Button("Delete Selected")) DeleteMeasurement(_selectedMeasurement);

            GUI.enabled = _measurements.Count > 0;
            if (GUILayout.Button("Copy CSV")) CopyMeasurementsCsvToClipboard();
            if (GUILayout.Button("Export CSV")) ExportMeasurementsCsv();
            if (GUILayout.Button("Clear All"))
            {
                CommitPendingNameUndo();
                PushMeasurementUndo("Clear all measurements");
                SetEndpointEditMode(false);
                _measurements.Clear();
                _selectedMeasurement = null;
                _activeMeasurement = null;
                MarkPersistenceDirty(true);
                SetStatus("All measurements cleared. Use Undo to restore them.", 5f);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawStatusLine()
        {
            string text = _statusMessage;
            if (_statusUntilRealtime > 0f && Time.realtimeSinceStartup > _statusUntilRealtime)
            {
                _statusUntilRealtime = 0f;
                text = "Click a measurement value to copy it to the clipboard.";
                _statusMessage = text;
            }
            GUILayout.Box(text, GUILayout.ExpandWidth(true));
        }

        private void DrawPointRow(string label, MeasurementPoint point)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(70f));
            GUILayout.Label(point != null ? point.PartTitle : "(not set)");
            GUILayout.EndHorizontal();
        }

        private void DrawDistanceCopyButton(float meters, string label, float width)
        {
            string value = FormatDistance(meters);
            if (GUILayout.Button(value, _valueButtonStyle, GUILayout.Width(width))) CopyToClipboard(value, label);
        }

        private void DrawDistanceCopyButton(Rect rect, float meters, string label)
        {
            string value = FormatDistance(meters);
            if (GUI.Button(rect, value, _valueButtonStyle)) CopyToClipboard(value, label);
        }

        private void DrawAngleCopyButton(float degrees, string label, float width)
        {
            string value = FormatAngle(degrees);
            if (GUILayout.Button(value, _valueButtonStyle, GUILayout.Width(width))) CopyToClipboard(value, label);
        }

        private void DrawWorldLabels()
        {
            Camera camera = GetEditorCamera();
            if (camera == null) return;
            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null || !m.IsComplete) continue;
                Vector3 screen = camera.WorldToScreenPoint(m.LabelPosition);
                if (screen.z <= 0f) continue;
                string text = _settings.ShowWorldLabelValues
                    ? m.Name + ": " + FormatMeasurementValue(m)
                    : m.Name;
                Vector2 size = _worldLabelStyle.CalcSize(new GUIContent(text));
                float x = screen.x - size.x * 0.5f;
                float y = Screen.height - screen.y - size.y * 0.5f;
                GUI.Box(new Rect(x - 8f, y - 4f, size.x + 16f, size.y + 8f), text, _worldLabelStyle);
            }
        }

        private void BeginEndpointDrag()
        {
            if (_selectedMeasurement == null || !_selectedMeasurement.IsComplete)
            {
                SetEndpointEditMode(false);
                return;
            }

            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (IsPointerOverGui(guiMouse)) return;

            Camera camera = GetEditorCamera();
            if (camera == null) return;

            MeasurementEndpoint endpoint = FindHoveredEndpoint(camera, _selectedMeasurement);
            if (endpoint == MeasurementEndpoint.None)
            {
                SetStatus("Move the pointer over an endpoint marker and drag it.", 4f);
                return;
            }

            _dragEndpoint = endpoint;
            _dragSnapNodeId = null;
            _dragSnapDescription = null;
            _dragPartTitle = null;
            _dragMoved = false;
            CommitPendingNameUndo();
            _dragUndoState = CaptureMeasurementUndoState();
            SetStatus("Dragging " + GetEndpointLabel(endpoint, _selectedMeasurement) +
                ". Release the mouse button to place it.", 5f);
        }

        private void UpdateEndpointDrag()
        {
            if (_dragEndpoint == MeasurementEndpoint.None ||
                _selectedMeasurement == null ||
                !_selectedMeasurement.IsComplete)
                return;

            Camera camera = GetEditorCamera();
            if (camera == null) return;

            Part part;
            Vector3 worldPoint;
            string snapNodeId;
            if (!TryGetVesselSurfacePoint(camera, out part, out worldPoint, out snapNodeId))
                return;

            if (_selectedMeasurement.ReplacePoint(
                _dragEndpoint,
                part,
                worldPoint,
                snapNodeId,
                _currentShip))
            {
                _dragMoved = true;
                _dragSnapNodeId = snapNodeId;
                _dragSnapDescription = _activeSnapDescription;
                MeasurementPoint movedPoint = _selectedMeasurement.GetPoint(_dragEndpoint);
                _dragPartTitle = movedPoint != null ? movedPoint.PartTitle : part.name;
            }
        }

        private void EndEndpointDrag()
        {
            MeasurementEndpoint endpoint = _dragEndpoint;
            _dragEndpoint = MeasurementEndpoint.None;

            if (!_dragMoved)
            {
                _dragUndoState = null;
                _dragSnapNodeId = null;
                _dragSnapDescription = null;
                SetStatus(GetEndpointLabel(endpoint, _selectedMeasurement) +
                    " was not moved. Drag over the vessel or a configured snap target to reposition it.", 5f);
                return;
            }

            PushCapturedMeasurementUndo(_dragUndoState,
                "Move " + GetEndpointLabel(endpoint, _selectedMeasurement));
            _dragUndoState = null;
            MarkPersistenceDirty(true);
            string snapText = !string.IsNullOrEmpty(_dragSnapDescription)
                ? " Snapped to " + _dragSnapDescription + "."
                : string.Empty;
            string partText = string.IsNullOrEmpty(_dragPartTitle)
                ? string.Empty
                : " on " + _dragPartTitle;
            SetStatus(GetEndpointLabel(endpoint, _selectedMeasurement) +
                " moved" + partText + "." + snapText, 6f);

            _dragMoved = false;
            _dragSnapNodeId = null;
            _dragSnapDescription = null;
            _dragPartTitle = null;
        }

        private MeasurementEndpoint FindHoveredEndpoint(Camera camera, MeasurementRecord measurement)
        {
            if (camera == null || measurement == null) return MeasurementEndpoint.None;

            float markerMultiplier = _settings != null ? _settings.MarkerSizeMultiplier : 1f;
            float selectedMultiplier = _settings != null && _settings.EmphasizeSelected
                ? _settings.SelectedMarkerMultiplier
                : 1f;
            float threshold = Mathf.Clamp(
                EndpointGrabPixels * markerMultiplier * selectedMultiplier,
                EndpointGrabPixels,
                70f);

            MeasurementEndpoint best = MeasurementEndpoint.None;
            float bestDistance = threshold;

            EvaluateEndpointHover(camera, measurement, MeasurementEndpoint.A, ref best, ref bestDistance);
            EvaluateEndpointHover(camera, measurement, MeasurementEndpoint.B, ref best, ref bestDistance);
            if (measurement.Kind == MeasurementKind.Angle)
                EvaluateEndpointHover(camera, measurement, MeasurementEndpoint.C, ref best, ref bestDistance);

            return best;
        }

        private static void EvaluateEndpointHover(
            Camera camera,
            MeasurementRecord measurement,
            MeasurementEndpoint endpoint,
            ref MeasurementEndpoint best,
            ref float bestDistance)
        {
            MeasurementPoint point = measurement.GetPoint(endpoint);
            if (point == null || !point.IsValid(measurement.LockMode)) return;

            Vector3 screen = camera.WorldToScreenPoint(measurement.GetWorldPosition(point));
            if (screen.z <= 0f) return;

            float distance = Vector2.Distance(
                new Vector2(screen.x, screen.y),
                new Vector2(Input.mousePosition.x, Input.mousePosition.y));

            if (distance > bestDistance) return;
            bestDistance = distance;
            best = endpoint;
        }

        private static string GetEndpointLabel(
            MeasurementEndpoint endpoint,
            MeasurementRecord measurement)
        {
            switch (endpoint)
            {
                case MeasurementEndpoint.A:
                    return "Point A";
                case MeasurementEndpoint.B:
                    return measurement != null && measurement.Kind == MeasurementKind.Angle
                        ? "Vertex B"
                        : "Point B";
                case MeasurementEndpoint.C:
                    return "Point C";
                default:
                    return "Endpoint";
            }
        }

        private bool TryGetVesselSurfacePoint(
            Camera camera,
            out Part nearestPart,
            out Vector3 selectedPoint,
            out string snapNodeId)
        {
            nearestPart = null;
            selectedPoint = Vector3.zero;
            snapNodeId = null;
            _activeSnapDescription = null;

            if (camera == null ||
                EditorLogic.fetch == null ||
                EditorLogic.fetch.ship == null ||
                EditorLogic.fetch.ship.parts == null)
                return false;

            ShipConstruct ship = EditorLogic.fetch.ship;
            bool hasRawWorldPoint = false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 10000f);

            // KSP Part colliders are often intentionally inset from the visible
            // model. Use collider hits only to identify candidate vessel Parts,
            // then intersect the actual visible render mesh so the measurement
            // point lands on the skin rather than a recessed/internal collider.
            HashSet<Part> candidateParts = new HashSet<Part>();
            Part fallbackPart = null;
            Vector3 fallbackPoint = Vector3.zero;
            float fallbackDistance = float.MaxValue;

            for (int i = 0; i < hits.Length; ++i)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null) continue;

                Part part = hit.collider.GetComponentInParent<Part>();
                if (part == null || !ship.parts.Contains(part)) continue;

                candidateParts.Add(part);
                if (hit.distance < fallbackDistance)
                {
                    fallbackDistance = hit.distance;
                    fallbackPart = part;
                    fallbackPoint = hit.point;
                }
            }

            float nearestVisualDistance = float.MaxValue;
            foreach (Part part in candidateParts)
            {
                Vector3 visualPoint;
                float visualDistance;
                if (!TryGetVisiblePartSurfacePoint(part, ray, out visualPoint, out visualDistance))
                    continue;

                if (visualDistance < nearestVisualDistance)
                {
                    nearestVisualDistance = visualDistance;
                    nearestPart = part;
                    selectedPoint = visualPoint;
                    hasRawWorldPoint = true;
                }
            }

            // Some mod parts do not expose a readable MeshFilter at runtime.
            // Preserve the previous collider behavior as a compatibility fallback.
            if (!hasRawWorldPoint && fallbackPart != null)
            {
                nearestPart = fallbackPart;
                selectedPoint = fallbackPoint;
                hasRawWorldPoint = true;
            }

            bool snappingActive = _settings != null &&
                (_settings.SnappingEnabled || IsSnapModifierHeld());

            if (snappingActive && _snapResolver != null)
            {
                bool hasAxisAnchor;
                Vector3 axisAnchor;
                TryGetSnapAxisAnchor(out hasAxisAnchor, out axisAnchor);

                SnapResult snap;
                if (_snapResolver.TryResolve(
                    ship,
                    nearestPart,
                    selectedPoint,
                    hasRawWorldPoint,
                    camera,
                    true,
                    _settings,
                    hasAxisAnchor,
                    axisAnchor,
                    out snap))
                {
                    nearestPart = snap.ReferencePart ?? nearestPart;
                    if (nearestPart == null && ship.parts.Count > 0)
                        nearestPart = ship.parts[0];

                    selectedPoint = snap.WorldPosition;
                    snapNodeId = snap.NodeId;
                    _activeSnapDescription = snap.Label;
                    return nearestPart != null;
                }
            }

            return hasRawWorldPoint && nearestPart != null;
        }

        private static bool TryGetVisiblePartSurfacePoint(
            Part part,
            Ray worldRay,
            out Vector3 worldPoint,
            out float worldDistance)
        {
            worldPoint = Vector3.zero;
            worldDistance = float.MaxValue;
            if (part == null || part.gameObject == null)
                return false;

            bool found = false;
            MeshFilter[] filters = part.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; ++i)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy)
                    continue;

                // Attached Parts may be transform children of other Parts. Only
                // test meshes whose nearest owning Part is the candidate Part.
                if (filter.GetComponentInParent<Part>() != part)
                    continue;

                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                float boundsDistance;
                if (!renderer.bounds.IntersectRay(worldRay, out boundsDistance))
                    continue;

                Vector3 hitPoint;
                float hitDistance;
                if (!TryIntersectMesh(filter.sharedMesh, filter.transform, worldRay, out hitPoint, out hitDistance))
                    continue;

                if (hitDistance < worldDistance)
                {
                    worldDistance = hitDistance;
                    worldPoint = hitPoint;
                    found = true;
                }
            }

            return found;
        }

        private static bool TryIntersectMesh(
            Mesh mesh,
            Transform meshTransform,
            Ray worldRay,
            out Vector3 worldPoint,
            out float worldDistance)
        {
            worldPoint = Vector3.zero;
            worldDistance = float.MaxValue;
            if (mesh == null || meshTransform == null)
                return false;

            try
            {
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                if (vertices == null || triangles == null || triangles.Length < 3)
                    return false;

                Matrix4x4 worldToLocal = meshTransform.worldToLocalMatrix;
                Vector3 localOrigin = worldToLocal.MultiplyPoint3x4(worldRay.origin);
                Vector3 localDirection = worldToLocal.MultiplyVector(worldRay.direction);

                bool found = false;
                float bestT = float.MaxValue;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int i0 = triangles[i];
                    int i1 = triangles[i + 1];
                    int i2 = triangles[i + 2];
                    if (i0 < 0 || i0 >= vertices.Length ||
                        i1 < 0 || i1 >= vertices.Length ||
                        i2 < 0 || i2 >= vertices.Length)
                        continue;

                    float t;
                    if (!RayIntersectsTriangle(
                        localOrigin,
                        localDirection,
                        vertices[i0],
                        vertices[i1],
                        vertices[i2],
                        out t))
                        continue;

                    if (t >= 0f && t < bestT)
                    {
                        bestT = t;
                        found = true;
                    }
                }

                if (!found)
                    return false;

                // The local ray was produced by applying the same affine matrix
                // to origin and direction, so its t parameter is the world-ray t.
                worldPoint = worldRay.origin + worldRay.direction * bestT;
                worldDistance = Vector3.Distance(worldRay.origin, worldPoint);
                return true;
            }
            catch
            {
                // A few mod meshes are not readable. Let the caller use the
                // collider fallback rather than making those Parts unmeasurable.
                return false;
            }
        }

        private static bool RayIntersectsTriangle(
            Vector3 origin,
            Vector3 direction,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            out float t)
        {
            t = 0f;
            const float epsilon = 0.000001f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < epsilon)
                return false;

            float inverse = 1f / determinant;
            Vector3 s = origin - v0;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
                return false;

            t = Vector3.Dot(edge2, q) * inverse;
            return t >= 0f;
        }

        private void TryGetSnapAxisAnchor(out bool hasAnchor, out Vector3 anchor)
        {
            hasAnchor = false;
            anchor = Vector3.zero;

            MeasurementRecord measurement = _endpointEditMode ? _selectedMeasurement : _activeMeasurement;
            if (measurement == null) return;

            MeasurementPoint point = null;

            if (_endpointEditMode && _dragEndpoint != MeasurementEndpoint.None)
            {
                switch (_dragEndpoint)
                {
                    case MeasurementEndpoint.A:
                        point = measurement.PointB;
                        break;
                    case MeasurementEndpoint.B:
                        point = measurement.PointA;
                        break;
                    case MeasurementEndpoint.C:
                        point = measurement.PointB;
                        break;
                }
            }
            else if (!measurement.IsComplete)
            {
                if (measurement.Kind == MeasurementKind.Angle)
                {
                    if (measurement.HasPointB && !measurement.HasPointC)
                        point = measurement.PointB;
                    else if (measurement.HasPointA && !measurement.HasPointB)
                        point = measurement.PointA;
                }
                else if (measurement.HasPointA && !measurement.HasPointB)
                {
                    point = measurement.PointA;
                }
            }

            if (point == null || !point.IsValid(measurement.LockMode)) return;
            anchor = measurement.GetWorldPosition(point);
            hasAnchor = true;
        }

        private void TrySelectPoint()
        {
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null) return;

            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (IsPointerOverGui(guiMouse)) return;

            Camera camera = GetEditorCamera();
            if (camera == null) return;

            Part nearestPart;
            Vector3 selectedPoint;
            string snapNodeId;
            if (!TryGetVesselSurfacePoint(camera, out nearestPart, out selectedPoint, out snapNodeId))
                return;

            string snapDescription = _activeSnapDescription;
            bool snapped = !string.IsNullOrEmpty(snapDescription);

            CommitPendingNameUndo();
            PushMeasurementUndo("Place measurement point");

            if (_activeMeasurement == null || _activeMeasurement.IsComplete)
            {
                MeasurementKind nextKind = _activeMeasurement != null
                    ? _activeMeasurement.Kind
                    : MeasurementKind.Distance;
                _activeMeasurement = CreateMeasurement(nextKind, false);
            }

            MeasurementRecord measurement = _activeMeasurement;
            measurement.AddPoint(
                nearestPart,
                selectedPoint,
                snapNodeId,
                _currentShip);

            if (measurement.IsComplete)
            {
                int symmetryCopies = _settings.SymmetryAwareMeasurements
                    ? CreateSymmetryCounterparts(measurement)
                    : 0;

                MarkPersistenceDirty(true);

                string symText = symmetryCopies > 0
                    ? " Created " + symmetryCopies + " symmetry counterpart" +
                        (symmetryCopies == 1 ? "." : "s.")
                    : string.Empty;

                SetStatus(
                    measurement.Name + " completed: " +
                    FormatMeasurementValue(measurement) +
                    (snapped ? " (last point snapped to " + snapDescription + ")" : string.Empty) +
                    symText,
                    7f);
            }
            else
            {
                SetStatus(
                    GetNextSelectionStatus(measurement) +
                    (snapped ? " Snapped to " + snapDescription + "." : string.Empty),
                    5f);
            }

            Debug.Log(
                LogPrefix + "Selected point on " + nearestPart.name +
                (snapped ? " snapped to " + snapDescription : string.Empty));
        }

        private int CreateSymmetryCounterparts(MeasurementRecord source)
        {
            if (source == null || !source.IsComplete || _currentShip == null) return 0;
            MeasurementPoint[] points = source.Kind == MeasurementKind.Angle
                ? new MeasurementPoint[] { source.PointA, source.PointB, source.PointC }
                : new MeasurementPoint[] { source.PointA, source.PointB };

            List<Part>[] groups = new List<Part>[points.Length];
            int maxCount = 1;
            for (int i = 0; i < points.Length; ++i)
            {
                if (points[i] == null || points[i].Part == null) return 0;
                groups[i] = GetSymmetryGroup(points[i].Part);
                maxCount = Mathf.Max(maxCount, groups[i].Count);
            }
            if (maxCount <= 1) return 0;

            int created = 0;
            for (int shift = 1; shift < maxCount; ++shift)
            {
                MeasurementPoint[] copies = new MeasurementPoint[points.Length];
                bool changed = false;
                bool valid = true;

                for (int i = 0; i < points.Length; ++i)
                {
                    List<Part> group = groups[i];
                    int sourceIndex = group.IndexOf(points[i].Part);
                    if (sourceIndex < 0) sourceIndex = 0;
                    Part target = group[(sourceIndex + (shift % group.Count)) % group.Count];
                    if (target != points[i].Part) changed = true;
                    copies[i] = points[i].CreateSymmetryCopy(target, _currentShip);
                    if (copies[i] == null) { valid = false; break; }
                }

                if (!valid || !changed) continue;
                string name = GetUniqueMeasurementName(source.Name + " [Sym " + (shift + 1) + "]");
                MeasurementRecord clone = new MeasurementRecord(
                    Guid.NewGuid().ToString("N"), name, source.Kind, source.LockMode,
                    copies[0], copies[1], source.Kind == MeasurementKind.Angle ? copies[2] : null);
                _measurements.Add(clone);
                created++;
            }
            return created;
        }

        private static List<Part> GetSymmetryGroup(Part part)
        {
            List<Part> group = new List<Part>();
            if (part == null) return group;
            group.Add(part);
            if (part.symmetryCounterparts != null)
            {
                for (int i = 0; i < part.symmetryCounterparts.Count; ++i)
                {
                    Part p = part.symmetryCounterparts[i];
                    if (p != null && !group.Contains(p)) group.Add(p);
                }
            }
            group.Sort(delegate(Part a, Part b) { return a.craftID.CompareTo(b.craftID); });
            return group;
        }

        private static string GetNextSelectionStatus(MeasurementRecord m)
        {
            if (m.Kind == MeasurementKind.Angle)
            {
                if (!m.HasPointB) return "Point A selected. Click vertex B.";
                if (!m.HasPointC) return "Vertex B selected. Click point C.";
            }
            else if (!m.HasPointB) return "Point A selected. Click point B.";
            return "Select the next point.";
        }

        private MeasurementRecord CreateMeasurement(MeasurementKind kind)
        {
            return CreateMeasurement(kind, true);
        }

        private MeasurementRecord CreateMeasurement(MeasurementKind kind, bool recordUndo)
        {
            if (recordUndo)
            {
                CommitPendingNameUndo();
                PushMeasurementUndo(kind == MeasurementKind.Angle
                    ? "Create angle measurement"
                    : "Create distance measurement");
            }

            MeasurementRecord m = new MeasurementRecord(kind, GetNextMeasurementName(kind), _settings.DefaultLockMode);
            _measurements.Add(m);
            return m;
        }

        private void SelectMeasurement(MeasurementRecord measurement)
        {
            if (measurement == null) return;

            if (_selectedMeasurement != measurement)
                CommitPendingNameUndo();

            bool changed = measurement != _selectedMeasurement;
            _selectedMeasurement = measurement;
            if (changed)
                RequestMainWindowResize();

            if (_endpointEditMode && !_selectedMeasurement.IsComplete)
                SetEndpointEditMode(false);

            if (changed)
                SetStatus(_selectedMeasurement.Name + " selected and highlighted in the editor.", 4f);
        }

        private void ClearSelection()
        {
            if (_selectedMeasurement == null) return;

            CommitPendingNameUndo();
            if (_endpointEditMode)
                SetEndpointEditMode(false);

            string name = _selectedMeasurement.Name;
            _selectedMeasurement = null;
            _measurementNameFieldFocused = false;
            RequestMainWindowResize();
            SetStatus(name + " is no longer selected. The measurement was not deleted.", 5f);
        }

        private void DeleteMeasurement(MeasurementRecord measurement)
        {
            if (measurement == null) return;
            CommitPendingNameUndo();
            PushMeasurementUndo("Delete " + measurement.Name);
            bool wasSelected = measurement == _selectedMeasurement;
            string name = measurement.Name;

            if (wasSelected && _endpointEditMode)
                SetEndpointEditMode(false);

            _measurements.Remove(measurement);
            if (wasSelected)
            {
                _selectedMeasurement = null;
            }
            RequestMainWindowResize();
            if (_activeMeasurement == measurement)
                _activeMeasurement = null;

            MarkPersistenceDirty(true);
            SetStatus(name + " deleted.", 4f);
        }

        private void RemoveInvalidMeasurements()
        {
            bool changed = false;
            for (int i = _measurements.Count - 1; i >= 0; --i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null || !m.ReferencesInvalidPart()) continue;
                if (_selectedMeasurement == m)
                {
                    if (_endpointEditMode) SetEndpointEditMode(false);
                    _selectedMeasurement = null;
                    RequestMainWindowResize();
                }
                if (_activeMeasurement == m)
                    _activeMeasurement = null;
                _measurements.RemoveAt(i);
                changed = true;
            }
            if (changed)
            {
                MarkPersistenceDirty(true);
                SetStatus("A measurement was removed because its active lock reference no longer exists.", 5f);
            }
        }

        private void EnsureShipContext()
        {
            ShipConstruct ship = EditorLogic.fetch.ship;
            if (!ReferenceEquals(_currentShip, ship))
            {
                _currentShip = ship;
                _currentShipName = ship != null ? ship.shipName : string.Empty;
                _snapResolver.Invalidate();
                RefreshVesselDimensions(ship);
                LoadMeasurementsForCurrentShip();
                return;
            }
            string shipName = ship != null ? ship.shipName : string.Empty;
            if (shipName != _currentShipName) { _currentShipName = shipName; MarkPersistenceDirty(); }
        }

        private void LoadMeasurementsForCurrentShip()
        {
            _measurementUndo.Clear();
            _nameEditUndoState = null;
            _nameEditMeasurementId = null;
            _dragUndoState = null;
            _measurements.Clear();
            _selectedMeasurement = null;
            _activeMeasurement = null;
            _persistenceDirty = false;
            if (_persistence == null || _currentShip == null || _currentShip.parts == null || _currentShip.parts.Count == 0) return;
            bool usedRootFallback;
            List<MeasurementRecord> loaded = _persistence.LoadForShip(_currentShip, GetEditorKey(), out usedRootFallback);
            _measurements.AddRange(loaded);
            if (usedRootFallback) MarkPersistenceDirty(true);
            if (_measurements.Count > 0)
                SetStatus("Loaded " + _measurements.Count + " saved measurement" + (_measurements.Count == 1 ? "." : "s."), 5f);
        }

        private MeasurementUndoState CaptureMeasurementUndoState()
        {
            return _measurementUndo.Capture(_measurements, _selectedMeasurement);
        }

        private void PushMeasurementUndo(string description)
        {
            _measurementUndo.Push(CaptureMeasurementUndoState(), description);
        }

        private void PushCapturedMeasurementUndo(MeasurementUndoState state, string description)
        {
            _measurementUndo.Push(state, description);
        }

        private void CommitPendingNameUndo()
        {
            if (_nameEditUndoState == null)
                return;

            _measurementUndo.Push(_nameEditUndoState, "Rename measurement");
            _nameEditUndoState = null;
            _nameEditMeasurementId = null;
        }

        private void UndoLastMeasurementChange()
        {
            CommitPendingNameUndo();

            MeasurementUndoEntry entry;
            if (!_measurementUndo.TryPop(out entry))
            {
                SetStatus("Nothing to undo in TapeMeasure.", 3f);
                return;
            }

            if (_endpointEditMode)
                SetEndpointEditMode(false);

            _dragEndpoint = MeasurementEndpoint.None;
            _dragMoved = false;
            _dragUndoState = null;

            int unresolvedPointCount;
            List<MeasurementRecord> restored = entry.State.Restore(
                _currentShip,
                out unresolvedPointCount);

            _measurements.Clear();
            _measurements.AddRange(restored);

            _activeMeasurement = null;
            _selectedMeasurement = null;
            string selectedId = entry.State.SelectedMeasurementId;
            if (!string.IsNullOrEmpty(selectedId))
            {
                for (int i = 0; i < _measurements.Count; ++i)
                {
                    if (_measurements[i] != null && _measurements[i].Id == selectedId)
                    {
                        _selectedMeasurement = _measurements[i];
                        break;
                    }
                }
            }

            RequestMainWindowResize();
            MarkPersistenceDirty(true);

            string unresolvedText = unresolvedPointCount > 0
                ? " " + unresolvedPointCount + " endpoint" +
                    (unresolvedPointCount == 1 ? " could" : "s could") +
                    " not be restored because the referenced Part no longer exists."
                : string.Empty;

            SetStatus("Undo: " + entry.Description + "." + unresolvedText, 6f);
        }

        private void MarkPersistenceDirty(bool saveImmediately = false)
        {
            _persistenceDirty = true;
            _saveAfterRealtime = saveImmediately ? Time.realtimeSinceStartup : Time.realtimeSinceStartup + 0.6f;
        }

        private void MarkSettingsDirty(bool saveImmediately = false)
        {
            if (_settings == null) return;
            _settingsDirty = true;
            _settingsSaveAfterRealtime = saveImmediately
                ? Time.realtimeSinceStartup
                : Time.realtimeSinceStartup + 0.6f;
        }

        private void SaveSettings()
        {
            if (_settings == null) return;
            CaptureWindowPreferences();
            _settings.Save();
            _settingsDirty = false;
        }

        private void CaptureWindowPreferences()
        {
            if (_settings == null) return;
            _settings.WindowX = _windowRect.x;
            _settings.WindowY = _windowRect.y;
            _settings.HasWindowPosition = true;
            _settings.SettingsWindowX = _settingsWindowRect.x;
            _settings.SettingsWindowY = _settingsWindowRect.y;
            _settings.HasSettingsWindowPosition = true;
            _settings.ShowSettingsPanel = _showSettings;
        }

        private void TrackWindowPosition()
        {
            if (_settings == null || Event.current == null || Event.current.type != EventType.Repaint)
                return;

            if (!_settings.HasWindowPosition ||
                Mathf.Abs(_settings.WindowX - _windowRect.x) > 0.5f ||
                Mathf.Abs(_settings.WindowY - _windowRect.y) > 0.5f)
            {
                _settings.WindowX = _windowRect.x;
                _settings.WindowY = _windowRect.y;
                _settings.HasWindowPosition = true;
                MarkSettingsDirty();
            }
        }

        private void TrackSettingsWindowPosition()
        {
            if (_settings == null || Event.current == null || Event.current.type != EventType.Repaint)
                return;

            if (!_settings.HasSettingsWindowPosition ||
                Mathf.Abs(_settings.SettingsWindowX - _settingsWindowRect.x) > 0.5f ||
                Mathf.Abs(_settings.SettingsWindowY - _settingsWindowRect.y) > 0.5f)
            {
                _settings.SettingsWindowX = _settingsWindowRect.x;
                _settings.SettingsWindowY = _settingsWindowRect.y;
                _settings.HasSettingsWindowPosition = true;
                MarkSettingsDirty();
            }
        }

        private void ClampWindowsToScreen()
        {
            float mainWidth = _windowRect.width > 1f ? _windowRect.width : 600f;
            float settingsWidth = _settingsWindowRect.width > 1f ? _settingsWindowRect.width : 560f;
            float maxY = Mathf.Max(0f, Screen.height - 30f);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - mainWidth));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxY);
            _settingsWindowRect.x = Mathf.Clamp(_settingsWindowRect.x, 0f, Mathf.Max(0f, Screen.width - settingsWidth));
            _settingsWindowRect.y = Mathf.Clamp(_settingsWindowRect.y, 0f, maxY);
        }

        private void SaveMeasurements()
        {
            if (_persistence == null || _currentShip == null) return;
            _persistence.SaveForShip(_currentShip, GetEditorKey(), _measurements);
            _persistenceDirty = false;
        }

        private string GetEditorKey()
        {
            if (!HighLogic.LoadedSceneIsEditor) return "EDITOR";

            if (EditorDriver.editorFacility == EditorFacility.SPH) return "SPH";
            if (EditorDriver.editorFacility == EditorFacility.VAB) return "VAB";
            return "EDITOR";
        }

        private string GetNextMeasurementName(MeasurementKind kind)
        {
            string prefix = kind == MeasurementKind.Angle ? "Angle " : "Distance ";
            int number = 1;
            while (true)
            {
                string candidate = prefix + number;
                bool exists = false;
                for (int i = 0; i < _measurements.Count; ++i)
                    if (string.Equals(_measurements[i].Name, candidate, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) return candidate;
                ++number;
            }
        }

        private string GetUniqueMeasurementName(string baseName)
        {
            string candidate = baseName;
            int suffix = 2;
            while (MeasurementNameExists(candidate)) candidate = baseName + " (" + suffix++ + ")";
            return candidate;
        }

        private bool MeasurementNameExists(string name)
        {
            for (int i = 0; i < _measurements.Count; ++i)
                if (string.Equals(_measurements[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string NormalizeMeasurementName(string name)
        {
            return string.IsNullOrEmpty(name) ? "Measurement" : name.Replace('\r', ' ').Replace('\n', ' ');
        }

        private void UpdateMeasurementPreview(Camera camera)
        {
            if (_renderer == null)
                return;

            bool visible = false;
            Vector3 start = Vector3.zero;
            Vector3 end = Vector3.zero;

            if (_measurementMode &&
                camera != null &&
                _activeMeasurement != null &&
                !_activeMeasurement.IsComplete)
            {
                MeasurementPoint startPoint = null;
                if (_activeMeasurement.Kind == MeasurementKind.Angle)
                {
                    if (_activeMeasurement.HasPointB && !_activeMeasurement.HasPointC)
                        startPoint = _activeMeasurement.PointB;
                    else if (_activeMeasurement.HasPointA && !_activeMeasurement.HasPointB)
                        startPoint = _activeMeasurement.PointA;
                }
                else if (_activeMeasurement.HasPointA && !_activeMeasurement.HasPointB)
                {
                    startPoint = _activeMeasurement.PointA;
                }

                Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (startPoint != null && !IsPointerOverGui(guiMouse))
                {
                    start = _activeMeasurement.GetWorldPosition(startPoint);
                    visible = TryGetPreviewPoint(camera, start, out end);
                }
            }

            _renderer.UpdatePreview(camera, visible, start, end, _settings);
        }

        private bool TryGetPreviewPoint(Camera camera, Vector3 start, out Vector3 end)
        {
            end = start;
            if (camera == null) return false;

            Part part;
            string snapNodeId;
            if (TryGetVesselSurfacePoint(camera, out part, out end, out snapNodeId))
                return true;

            // If the cursor is not over vessel geometry, project it onto a
            // camera-facing plane through the last selected endpoint. This
            // keeps the preview line visually attached to the mouse anywhere
            // in the editor view instead of disappearing off the vessel.
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(camera.transform.forward, start);
            float enter;
            if (plane.Raycast(ray, out enter) && enter >= 0f)
            {
                end = ray.GetPoint(enter);
                return true;
            }

            float depth = Vector3.Distance(camera.transform.position, start);
            end = ray.GetPoint(Mathf.Max(0.1f, depth));
            return true;
        }

        private void CopyMeasurementsCsvToClipboard()
        {
            string csv = BuildMeasurementsCsv();
            GUIUtility.systemCopyBuffer = csv;
            SetStatus(_measurements.Count + (_measurements.Count == 1 ? " measurement" : " measurements") +
                " copied to the clipboard as CSV.", 6f);
        }

        private void ExportMeasurementsCsv()
        {
            try
            {
                string saveFolder = string.IsNullOrEmpty(HighLogic.SaveFolder) ? "default" : HighLogic.SaveFolder;
                string exportDirectory = Path.Combine(
                    KSPUtil.ApplicationRootPath, "saves", saveFolder, "TapeMeasure", "Exports");
                Directory.CreateDirectory(exportDirectory);

                string craftName = string.IsNullOrEmpty(_currentShipName) ? "Untitled" : _currentShipName;
                string fileName = SanitizeFileName(GetEditorKey() + "_" + craftName + "_measurements_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
                string path = Path.Combine(exportDirectory, fileName);

                File.WriteAllText(path, BuildMeasurementsCsv(), new UTF8Encoding(true));
                SetStatus("Measurement CSV exported to " + path, 10f);
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "CSV export failed: " + ex);
                SetStatus("CSV export failed: " + ex.Message, 10f);
            }
        }

        private string BuildMeasurementsCsv()
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("Type,Name,Lock,Value,Distance_m,Angle_deg,X_m,Y_m,Z_m,Point_A,Point_B,Point_C");

            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null) continue;

                string distance = string.Empty;
                string angle = string.Empty;
                string x = string.Empty;
                string y = string.Empty;
                string z = string.Empty;

                if (m.IsComplete)
                {
                    if (m.Kind == MeasurementKind.Distance)
                    {
                        Vector3 axis = m.AxisDelta(EditorLogic.VesselRotation);
                        distance = m.Distance.ToString("R", CultureInfo.InvariantCulture);
                        x = Mathf.Abs(axis.x).ToString("R", CultureInfo.InvariantCulture);
                        y = Mathf.Abs(axis.y).ToString("R", CultureInfo.InvariantCulture);
                        z = Mathf.Abs(axis.z).ToString("R", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        angle = m.AngleDegrees.ToString("R", CultureInfo.InvariantCulture);
                    }
                }

                AppendCsvRow(csv,
                    m.Kind == MeasurementKind.Angle ? "Angle" : "Distance",
                    m.Name,
                    m.LockMode == MeasurementLockMode.PartRelative ? "Part-relative" : "Vessel-relative",
                    FormatMeasurementValue(m),
                    distance,
                    angle,
                    x,
                    y,
                    z,
                    m.PointA != null ? m.PointA.PartTitle : string.Empty,
                    m.PointB != null ? m.PointB.PartTitle : string.Empty,
                    m.PointC != null ? m.PointC.PartTitle : string.Empty);
            }

            return csv.ToString();
        }

        private static void AppendCsvRow(StringBuilder csv, params string[] values)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                if (i > 0) csv.Append(',');
                csv.Append(EscapeCsv(values[i]));
            }
            csv.AppendLine();
        }

        private static string EscapeCsv(string value)
        {
            if (value == null) value = string.Empty;
            bool quote = value.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) >= 0;
            if (value.IndexOf('"') >= 0) value = value.Replace("\"", "\"\"");
            return quote ? "\"" + value + "\"" : value;
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder clean = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; ++j)
                {
                    if (c == invalid[j]) { bad = true; break; }
                }
                clean.Append(bad ? '_' : c);
            }
            return clean.ToString();
        }

        private void HandleKeyboardShortcuts()
        {
            // Keep editor-wide shortcuts inactive while the TapeMeasure window is
            // hidden. Escape remains available whenever one of TapeMeasure's
            // interaction modes is active.
            if ((_measurementMode || _endpointEditMode) && Input.GetKeyDown(KeyCode.Escape))
            {
                SetMeasurementMode(false);
                SetEndpointEditMode(false);
                SetStatus("Measurement/edit mode exited.", 3f);
                return;
            }

            if (!_windowVisible || IsTextEntryActive())
                return;

            if (Input.GetKeyDown(KeyCode.M))
            {
                if (_measurementMode)
                {
                    SetMeasurementMode(false);
                    SetStatus("Measurement mode off.", 3f);
                }
                else
                {
                    if (_endpointEditMode)
                        SetEndpointEditMode(false);

                    // Enter measurement mode without changing list selection.
                    // A new distance record is created on the first click if
                    // there is no unfinished active measurement.
                    SetMeasurementMode(true);
                    SetStatus("Measurement mode on. Click the vessel to place the next point.", 4f);
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                if (_selectedMeasurement != null)
                    DeleteMeasurement(_selectedMeasurement);
                else
                    SetStatus("No measurement is selected.", 3f);
                return;
            }

            bool controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (controlHeld && Input.GetKeyDown(KeyCode.Z))
            {
                UndoLastMeasurementChange();
                return;
            }

            if (controlHeld && Input.GetKeyDown(KeyCode.C))
            {
                CopySelectedMeasurementValue();
            }
        }

        private static bool IsSnapModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private bool IsTextEntryActive()
        {
            if (_measurementNameFieldFocused)
                return true;

            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null)
                return false;

            Component[] components = selected.GetComponents<Component>();
            for (int i = 0; i < components.Length; ++i)
            {
                Component component = components[i];
                if (component == null) continue;

                string typeName = component.GetType().Name;
                if (typeName == "InputField" || typeName == "TMP_InputField")
                    return true;
            }

            return false;
        }

        private void CopySelectedMeasurementValue()
        {
            if (_selectedMeasurement == null)
            {
                SetStatus("No measurement is selected.", 3f);
                return;
            }

            if (!_selectedMeasurement.IsComplete)
            {
                SetStatus("The selected measurement is not complete yet.", 4f);
                return;
            }

            string label = _selectedMeasurement.Kind == MeasurementKind.Angle ? "Angle" : "Distance";
            CopyToClipboard(FormatMeasurementValue(_selectedMeasurement), label);
        }

        private void CopyToClipboard(string value, string label)
        {
            GUIUtility.systemCopyBuffer = value;
            SetStatus(label + " " + value + " copied to clipboard.", 5f);
        }

        private void SetStatus(string message, float seconds)
        {
            _statusMessage = message;
            _statusUntilRealtime = Time.realtimeSinceStartup + seconds;
        }

        private bool IsPointerOverGui(Vector2 guiMouse)
        {
            // The TapeMeasure window uses IMGUI, so test it explicitly.
            if (_windowRect.Contains(guiMouse))
                return true;
            if (_showSettings && _settingsWindowRect.Contains(guiMouse))
                return true;

            // Stock KSP editor UI is Unity UI.  EventSystem is the supported
            // way to determine whether the pointer is currently over it.
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        private Camera GetEditorCamera()
        {
            if (EditorLogic.fetch != null && EditorLogic.fetch.editorCamera != null) return EditorLogic.fetch.editorCamera;
            return Camera.main;
        }

        private void SetMeasurementMode(bool enabled)
        {
            bool changed = _measurementMode != enabled;
            _measurementMode = enabled;
            if (enabled)
            {
                _endpointEditMode = false;
                _dragEndpoint = MeasurementEndpoint.None;
            }
            UpdateEditorSoftLock();
            UpdateToolbarModeIcon();
            UpdateMeasurementCursor();

            // The instruction/status area changes when measuring starts or stops.
            // Reset the GUILayout window height so Stop Measuring immediately
            // shrinks the window to the content that is still visible.
            if (changed)
                RequestMainWindowResize();
        }

        private void SetEndpointEditMode(bool enabled)
        {
            if (enabled &&
                (_selectedMeasurement == null || !_selectedMeasurement.IsComplete))
            {
                _endpointEditMode = false;
                UpdateEditorSoftLock();
                SetStatus("Select a completed measurement before editing endpoints.", 5f);
                return;
            }

            _endpointEditMode = enabled;
            _dragEndpoint = MeasurementEndpoint.None;
            _dragMoved = false;
            _dragSnapNodeId = null;
            _dragSnapDescription = null;
            _dragPartTitle = null;
            _dragUndoState = null;

            if (enabled)
            {
                _measurementMode = false;
                SetStatus("Endpoint editing enabled. Drag a visible endpoint marker.", 5f);
            }

            UpdateEditorSoftLock();
            UpdateToolbarModeIcon();
            UpdateMeasurementCursor();
        }

        private void LoadMeasurementCursor()
        {
            try
            {
                // This texture is drawn by TapeMeasure itself instead of being
                // installed as the OS/KSP cursor. That avoids the stock editor
                // changing the hardware cursor underneath us and eliminates
                // the visible cursor flicker.
                string path = Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "GameData",
                    "TapeMeasure",
                    "Textures",
                    "cursor_measure.png");

                if (!File.Exists(path))
                {
                    Debug.LogWarning(LogPrefix + "Measurement cursor texture not found: " + path);
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "TapeMeasure.MeasureCursor";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

                if (!texture.LoadImage(bytes))
                {
                    Destroy(texture);
                    Debug.LogWarning(LogPrefix + "Unable to decode measurement cursor PNG.");
                    return;
                }

                _measurementCursorTexture = texture;
                Debug.Log(LogPrefix + "Loaded measurement cursor overlay.");
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "Failed to load measurement cursor: " + ex);
            }
        }

        private void UpdateMeasurementCursor()
        {
            bool overTapeMeasureWindow = IsPointerOverTapeMeasureWindow();
            bool shouldShow = _measurementMode &&
                _measurementCursorTexture != null &&
                !overTapeMeasureWindow;

            _measurementCursorVisible = shouldShow;

            // Use the normal pointer over TapeMeasure UI so buttons, text fields
            // and pane headers behave like ordinary editor controls. Outside the
            // windows, hide it and draw the measurement cursor overlay instead.
            Cursor.visible = !shouldShow;
        }

        private bool IsPointerOverTapeMeasureWindow()
        {
            Vector2 guiMouse = new Vector2(
                Input.mousePosition.x,
                Screen.height - Input.mousePosition.y);

            if (_windowVisible && _windowRect.Contains(guiMouse))
                return true;

            return _windowVisible && _showSettings &&
                _settingsWindowRect.Contains(guiMouse);
        }

        private void DrawMeasurementCursor()
        {
            if (!_measurementCursorVisible ||
                _measurementCursorTexture == null ||
                IsPointerOverTapeMeasureWindow())
                return;

            Vector2 mouse = Event.current != null
                ? Event.current.mousePosition
                : new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            // The pencil tip in the supplied artwork is near the lower-left
            // corner; keep that point aligned with the actual click location.
            const float hotspotX = 4f;
            float hotspotY = Mathf.Max(0f, _measurementCursorTexture.height - 4f);
            Rect cursorRect = new Rect(
                mouse.x - hotspotX,
                mouse.y - hotspotY,
                _measurementCursorTexture.width,
                _measurementCursorTexture.height);

            GUI.depth = -10000;
            GUI.DrawTexture(cursorRect, _measurementCursorTexture, ScaleMode.StretchToFill, true);
        }

        private void ResetMeasurementCursor()
        {
            _measurementCursorVisible = false;
            Cursor.visible = true;
        }

        private void ReleaseMeasurementCursor()
        {
            ResetMeasurementCursor();

            if (_measurementCursorTexture != null)
            {
                Destroy(_measurementCursorTexture);
                _measurementCursorTexture = null;
            }
        }

        private void UpdateEditorSoftLock()
        {
            bool shouldLock = _measurementMode || _endpointEditMode;

            if (shouldLock && !_ownsSoftLock)
            {
                // KSP 1.12.x no longer exposes the legacy EditorLogic soft-lock API.
                // Own an InputLockManager lock instead, and remove only our lock.
                InputLockManager.SetControlLock(EditorInputLockId);
                _ownsSoftLock = true;
            }
            else if (!shouldLock && _ownsSoftLock)
            {
                InputLockManager.RemoveControlLock(EditorInputLockId);
                _ownsSoftLock = false;
            }
        }

        private void CreateToolbarButton()
        {
            if (_toolbarControl != null) return;

            _toolbarControl = gameObject.AddComponent<ToolbarControl>();
            _toolbarControl.AddToAllToolbars(
                ShowWindow,
                HideWindow,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
                ModId,
                ToolbarButtonId,
                IconLargeNormal,
                IconSmallNormal,
                ModName);

            // Keep toolbar toggle state tied to window visibility. The texture is
            // changed independently when measurement mode starts/stops.
            if (_windowVisible) _toolbarControl.SetTrue(false);
            else _toolbarControl.SetFalse(false);
            UpdateToolbarModeIcon();
        }

        private void UpdateToolbarModeIcon()
        {
            if (_toolbarControl == null) return;
            _toolbarControl.SetTexture(
                _measurementMode ? IconLargeMeasuring : IconLargeNormal,
                _measurementMode ? IconSmallMeasuring : IconSmallNormal);
        }

        private void ShowWindow()
        {
            _windowVisible = true;
            ClampWindowsToScreen();
        }

        private void HideWindow()
        {
            CaptureWindowPreferences();
            MarkSettingsDirty(true);
            _windowVisible = false;
            SetMeasurementMode(false);
            SetEndpointEditMode(false);
        }

        private void EnsureStyles()
        {
            if (_styleSkin != GUI.skin)
            {
                _styleSkin = GUI.skin;
                _statusStyle = null;
                _tableHeaderStyle = null;
                _selectedNameStyle = null;
                _worldLabelStyle = null;
                _valueButtonStyle = null;
                _deleteButtonStyle = null;
            }

            if (_statusStyle == null) { _statusStyle = new GUIStyle(GUI.skin.label); _statusStyle.fontStyle = FontStyle.Bold; _statusStyle.wordWrap = true; }
            if (_tableHeaderStyle == null) { _tableHeaderStyle = new GUIStyle(GUI.skin.label); _tableHeaderStyle.fontStyle = FontStyle.Bold; _tableHeaderStyle.alignment = TextAnchor.MiddleCenter; }
            if (_selectedNameStyle == null) { _selectedNameStyle = new GUIStyle(GUI.skin.button); _selectedNameStyle.fontStyle = FontStyle.Bold; }
            if (_worldLabelStyle == null) { _worldLabelStyle = new GUIStyle(GUI.skin.box); _worldLabelStyle.fontStyle = FontStyle.Bold; _worldLabelStyle.alignment = TextAnchor.MiddleCenter; _worldLabelStyle.padding = new RectOffset(8, 8, 5, 5); }
            if (_valueButtonStyle == null)
            {
                _valueButtonStyle = new GUIStyle(GUI.skin.label);
                _valueButtonStyle.alignment = TextAnchor.MiddleCenter;
                Color valueBlue = new Color(0.30f, 0.70f, 1.00f, 1.00f);
                _valueButtonStyle.normal.textColor = valueBlue;
                _valueButtonStyle.hover.textColor = valueBlue;
                _valueButtonStyle.active.textColor = valueBlue;
                _valueButtonStyle.focused.textColor = valueBlue;
                _valueButtonStyle.onNormal.textColor = valueBlue;
                _valueButtonStyle.onHover.textColor = valueBlue;
                _valueButtonStyle.onActive.textColor = valueBlue;
                _valueButtonStyle.onFocused.textColor = valueBlue;
            }
            if (_deleteButtonStyle == null)
            {
                _deleteButtonStyle = new GUIStyle(GUI.skin.label);
                _deleteButtonStyle.alignment = TextAnchor.MiddleCenter;
                _deleteButtonStyle.fontStyle = FontStyle.Bold;
                Color deleteRed = Color.red;
                _deleteButtonStyle.normal.textColor = deleteRed;
                _deleteButtonStyle.hover.textColor = deleteRed;
                _deleteButtonStyle.active.textColor = deleteRed;
                _deleteButtonStyle.focused.textColor = deleteRed;
                _deleteButtonStyle.onNormal.textColor = deleteRed;
                _deleteButtonStyle.onHover.textColor = deleteRed;
                _deleteButtonStyle.onActive.textColor = deleteRed;
                _deleteButtonStyle.onFocused.textColor = deleteRed;
            }
        }

        private string FormatMeasurementValue(MeasurementRecord m)
        {
            if (m == null || !m.IsComplete) return "--";
            return m.Kind == MeasurementKind.Angle ? FormatAngle(m.AngleDegrees) : FormatDistance(m.Distance);
        }

        private static string FormatAngle(float degrees) { return degrees.ToString("F2") + "\u00B0"; }

        private string FormatDistance(float meters)
        {
            DistanceUnitMode units = _settings != null
                ? _settings.DistanceUnits
                : DistanceUnitMode.Auto;

            switch (units)
            {
                case DistanceUnitMode.Meters:
                    return meters.ToString("0.###") + " m";

                case DistanceUnitMode.Centimeters:
                    return (meters * 100f).ToString("0.##") + " cm";

                case DistanceUnitMode.Millimeters:
                    return (meters * 1000f).ToString("0.#") + " mm";

                case DistanceUnitMode.FeetInches:
                    return FormatFeetInches(meters);

                default:
                    if (meters < 1f) return (meters * 100f).ToString("F1") + " cm";
                    if (meters < 10f) return meters.ToString("F3") + " m";
                    if (meters < 100f) return meters.ToString("F2") + " m";
                    return meters.ToString("F1") + " m";
            }
        }

        private static string FormatFeetInches(float meters)
        {
            double totalInches = Math.Abs(meters) * 39.37007874015748;
            int feet = (int)Math.Floor(totalInches / 12.0);
            double inches = totalInches - (feet * 12.0);

            // Avoid displaying 12.0 inches because of rounding.
            inches = Math.Round(inches, 1);
            if (inches >= 12.0)
            {
                feet++;
                inches = 0.0;
            }

            string sign = meters < 0f ? "-" : string.Empty;
            return sign + feet + " ft " + inches.ToString("0.0") + " in";
        }

        private static Texture2D MakeFallbackIcon()
        {
            Texture2D texture = new Texture2D(38, 38, TextureFormat.ARGB32, false);
            Color transparent = new Color(0f, 0f, 0f, 0f);
            Color white = Color.white;
            for (int y = 0; y < 38; ++y) for (int x = 0; x < 38; ++x) texture.SetPixel(x, y, transparent);
            for (int x = 6; x <= 31; ++x) for (int y = 17; y <= 20; ++y) texture.SetPixel(x, y, white);
            for (int y = 12; y <= 25; ++y)
            {
                for (int x = 5; x <= 8; ++x) texture.SetPixel(x, y, white);
                for (int x = 29; x <= 32; ++x) texture.SetPixel(x, y, white);
            }
            texture.Apply();
            return texture;
        }
    }
}
