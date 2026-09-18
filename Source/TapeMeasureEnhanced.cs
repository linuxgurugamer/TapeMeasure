using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapeMeasure
{
    public sealed partial class TapeMeasure
    {
        private const string MeasurementGroupControlName = "TapeMeasure.MeasurementGroup";
        private const string MeasurementFilterControlName = "TapeMeasure.MeasurementFilter";
        private string _measurementFilter = string.Empty;
        private readonly Dictionary<string, bool> _groupExpanded =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private string _hoveredListMeasurementId;
        private string _hoveredEditorMeasurementId;
        private bool _listHoverSeen;
        private ShortcutAction _capturingShortcut = ShortcutAction.None;
        private string _groupEditBuffer = string.Empty;
        private string _groupEditMeasurementId;
        private bool _filterFieldFocused;
        private bool _groupFieldFocused;

        private static readonly Color[] MeasurementPalette = new Color[]
        {
            new Color(1.00f, 0.90f, 0.10f, 1f),
            new Color(0.20f, 0.75f, 1.00f, 1f),
            new Color(0.25f, 1.00f, 0.35f, 1f),
            new Color(1.00f, 0.45f, 0.25f, 1f),
            new Color(0.85f, 0.40f, 1.00f, 1f),
            new Color(1.00f, 0.35f, 0.65f, 1f),
            new Color(0.30f, 1.00f, 0.85f, 1f),
            new Color(1.00f, 0.70f, 0.20f, 1f),
            new Color(0.55f, 0.70f, 1.00f, 1f),
            new Color(0.80f, 1.00f, 0.25f, 1f),
            new Color(1.00f, 1.00f, 1.00f, 1f),
            new Color(0.65f, 0.65f, 0.65f, 1f)
        };

        private sealed class MeasurementDisplayGroup
        {
            public string Name;
            public readonly List<MeasurementRecord> Items = new List<MeasurementRecord>();
        }


        private struct MeasurementListLayout
        {
            public float VisibilityWidth;
            public float ColorWidth;
            public float TypeWidth;
            public float NameWidth;
            public float LockWidth;
            public float ValueWidth;
            public float XWidth;
            public float YWidth;
            public float ZWidth;
            public float DeleteWidth;
            public float Gap;
            public float NameGap;

            public float TotalWidth
            {
                get
                {
                    return VisibilityWidth + Gap +
                           ColorWidth + Gap +
                           TypeWidth + Gap +
                           NameWidth + NameGap +
                           LockWidth + Gap +
                           ValueWidth + Gap +
                           XWidth + Gap +
                           YWidth + Gap +
                           ZWidth + Gap +
                           DeleteWidth;
                }
            }
        }

        private static float MeasureListTextWidth(GUIStyle style, string text, float minimum)
        {
            if (style == null)
                return minimum;

            Vector2 size = style.CalcSize(new GUIContent(text ?? string.Empty));
            // A few extra pixels keep glyphs away from the cell edge even on
            // skins whose padding is very small.
            return Mathf.Max(minimum, Mathf.Ceil(size.x) + 6f);
        }

        private MeasurementListLayout CalculateMeasurementListLayout()
        {
            MeasurementListLayout layout = new MeasurementListLayout
            {
                VisibilityWidth = 26f,
                ColorWidth = 22f,
                TypeWidth = 64f,
                // Per the list UI design, Name remains fixed. Its displayed
                // value is already limited to 12 characters with a tooltip for
                // the complete name.
                NameWidth = 115f,
                LockWidth = 55f,
                ValueWidth = 64f,
                XWidth = 50f,
                YWidth = 50f,
                ZWidth = 50f,
                DeleteWidth = 24f,
                Gap = 2f,
                NameGap = 6f
            };

            GUIStyle labelStyle = GUI.skin != null ? GUI.skin.label : null;
            GUIStyle valueStyle = _valueButtonStyle ?? labelStyle;
            GUIStyle headerStyle = _tableHeaderStyle ?? labelStyle;
            GUIStyle deleteStyle = _deleteButtonStyle ?? labelStyle;

            layout.TypeWidth = Mathf.Max(layout.TypeWidth,
                MeasureListTextWidth(headerStyle, "Type", 0f));
            layout.TypeWidth = Mathf.Max(layout.TypeWidth,
                MeasureListTextWidth(labelStyle, "Distance", 0f));
            layout.TypeWidth = Mathf.Max(layout.TypeWidth,
                MeasureListTextWidth(labelStyle, "Angle", 0f));

            layout.LockWidth = Mathf.Max(layout.LockWidth,
                MeasureListTextWidth(headerStyle, "Lock", 0f));
            layout.LockWidth = Mathf.Max(layout.LockWidth,
                MeasureListTextWidth(labelStyle, "Vessel", 0f));
            layout.LockWidth = Mathf.Max(layout.LockWidth,
                MeasureListTextWidth(labelStyle, "Part", 0f));

            layout.ValueWidth = Mathf.Max(layout.ValueWidth,
                MeasureListTextWidth(headerStyle, "Value", 0f));
            layout.XWidth = Mathf.Max(layout.XWidth,
                MeasureListTextWidth(headerStyle, "X", 0f));
            layout.YWidth = Mathf.Max(layout.YWidth,
                MeasureListTextWidth(headerStyle, "Y", 0f));
            layout.ZWidth = Mathf.Max(layout.ZWidth,
                MeasureListTextWidth(headerStyle, "Z", 0f));
            layout.DeleteWidth = Mathf.Max(layout.DeleteWidth,
                MeasureListTextWidth(deleteStyle, "X", 0f));

            // Measure the actual formatted values that can appear in every
            // numeric column. This uses the current units and precision, so a
            // switch to millimetres or four decimals automatically increases
            // the minimum window width enough to keep every item on one line.
            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null || !m.IsComplete)
                    continue;

                if (m.Kind == MeasurementKind.Angle)
                {
                    layout.ValueWidth = Mathf.Max(layout.ValueWidth,
                        MeasureListTextWidth(valueStyle, FormatAngle(m.AngleDegrees), 0f));
                    continue;
                }

                Vector3 axis = m.AxisDelta(EditorLogic.VesselRotation);
                layout.ValueWidth = Mathf.Max(layout.ValueWidth,
                    MeasureListTextWidth(valueStyle, FormatDistance(m.Distance), 0f));
                layout.XWidth = Mathf.Max(layout.XWidth,
                    MeasureListTextWidth(valueStyle, FormatDistance(Mathf.Abs(axis.x)), 0f));
                layout.YWidth = Mathf.Max(layout.YWidth,
                    MeasureListTextWidth(valueStyle, FormatDistance(Mathf.Abs(axis.y)), 0f));
                layout.ZWidth = Mathf.Max(layout.ZWidth,
                    MeasureListTextWidth(valueStyle, FormatDistance(Mathf.Abs(axis.z)), 0f));
            }

            // Incomplete/angle rows display dashes in the axis columns.
            float dashWidth = MeasureListTextWidth(labelStyle, "--", 0f);
            layout.XWidth = Mathf.Max(layout.XWidth, dashWidth);
            layout.YWidth = Mathf.Max(layout.YWidth, dashWidth);
            layout.ZWidth = Mathf.Max(layout.ZWidth, dashWidth);

            return layout;
        }

        private MeasurementListLayout ExpandMeasurementDataColumns(
            MeasurementListLayout minimumLayout,
            float availableWidth)
        {
            float extra = Mathf.Max(0f, availableWidth - minimumLayout.TotalWidth);
            if (extra <= 0f)
                return minimumLayout;

            // Keep the Name/Type/Lock controls stable. Extra user-resized space
            // is shared by the four data columns.
            float perColumn = extra / 4f;
            minimumLayout.ValueWidth += perColumn;
            minimumLayout.XWidth += perColumn;
            minimumLayout.YWidth += perColumn;
            minimumLayout.ZWidth += perColumn;
            return minimumLayout;
        }

        private float GetMeasurementListRequiredWindowWidth(MeasurementListLayout layout)
        {
            float chrome = MainWindowResizeGripWidth + 18f;
            if (GUI.skin != null)
            {
                if (GUI.skin.window != null)
                    chrome += GUI.skin.window.padding.left + GUI.skin.window.padding.right;
                if (GUI.skin.box != null)
                    chrome += GUI.skin.box.padding.left + GUI.skin.box.padding.right;

                GUIStyle scrollbar = GUI.skin.verticalScrollbar;
                if (scrollbar != null)
                {
                    float scrollbarWidth = scrollbar.fixedWidth > 0f ? scrollbar.fixedWidth : 16f;
                    chrome += scrollbarWidth + scrollbar.margin.left + scrollbar.margin.right;
                }
            }

            return Mathf.Max(MainWindowMinWidth, layout.TotalWidth + chrome);
        }

        private void EnsureMainWindowWideEnoughForMeasurementList()
        {
            if (_settings == null || !_settings.ShowMeasurementListPane || !_windowVisible ||
                IsMainWindowTemporarilyHidden())
                return;

            MeasurementListLayout layout = CalculateMeasurementListLayout();
            float requiredWidth = GetMeasurementListRequiredWindowWidth(layout);
            float maximumWidth = Mathf.Max(320f, Screen.width - 8f);
            float targetWidth = Mathf.Min(requiredWidth, maximumWidth);

            if (_mainWindowWidth + 0.5f >= targetWidth)
                return;

            _mainWindowWidth = targetWidth;
            _windowRect.width = targetWidth;
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - targetWidth));

            _settings.WindowWidth = targetWidth;
            MarkSettingsDirty();
            RequestMainWindowResize();
        }

        private void DrawMeasurementListHeader(MeasurementListLayout minimumLayout)
        {
            Rect row = GUILayoutUtility.GetRect(
                minimumLayout.TotalWidth,
                22f,
                GUILayout.MinWidth(minimumLayout.TotalWidth),
                GUILayout.ExpandWidth(true));
            MeasurementListLayout layout = ExpandMeasurementDataColumns(minimumLayout, row.width);

            float x = row.x;
            Rect visibilityRect = new Rect(x, row.y, layout.VisibilityWidth, row.height); x += layout.VisibilityWidth + layout.Gap;
            Rect colorRect = new Rect(x, row.y, layout.ColorWidth, row.height); x += layout.ColorWidth + layout.Gap;
            Rect typeRect = new Rect(x, row.y, layout.TypeWidth, row.height); x += layout.TypeWidth + layout.Gap;
            Rect nameRect = new Rect(x, row.y, layout.NameWidth, row.height); x += layout.NameWidth + layout.NameGap;
            Rect lockRect = new Rect(x, row.y, layout.LockWidth, row.height); x += layout.LockWidth + layout.Gap;
            Rect valueRect = new Rect(x, row.y, layout.ValueWidth, row.height); x += layout.ValueWidth + layout.Gap;
            Rect xRect = new Rect(x, row.y, layout.XWidth, row.height); x += layout.XWidth + layout.Gap;
            Rect yRect = new Rect(x, row.y, layout.YWidth, row.height); x += layout.YWidth + layout.Gap;
            Rect zRect = new Rect(x, row.y, layout.ZWidth, row.height); x += layout.ZWidth + layout.Gap;
            Rect deleteRect = new Rect(x, row.y, layout.DeleteWidth, row.height);

            GUI.Label(visibilityRect, "Vis", _tableHeaderStyle);
            GUI.Label(colorRect, string.Empty, _tableHeaderStyle);
            GUI.Label(typeRect, "Type", _tableHeaderStyle);
            GUI.Label(nameRect, "Name", _tableHeaderStyle);
            GUI.Label(lockRect, "Lock", _tableHeaderStyle);
            GUI.Label(valueRect, "Value", _tableHeaderStyle);
            GUI.Label(xRect, "X", _tableHeaderStyle);
            GUI.Label(yRect, "Y", _tableHeaderStyle);
            GUI.Label(zRect, "Z", _tableHeaderStyle);
            GUI.Label(deleteRect, string.Empty, _tableHeaderStyle);
        }

        private void DrawEnhancedMeasurementListPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            string countText = _measurements.Count + (_measurements.Count == 1 ? " measurement" : " measurements");
            if (!DrawPaneHeader("Measurement list", ref _settings.ShowMeasurementListPane, countText))
            {
                _hoveredListMeasurementId = null;
                _filterFieldFocused = false;
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(38f));
            GUI.SetNextControlName(MeasurementFilterControlName);
            _measurementFilter = GUILayout.TextField(_measurementFilter ?? string.Empty, GUILayout.Width(140f));
            _filterFieldFocused = GUI.GetNameOfFocusedControl() == MeasurementFilterControlName;
            if (GUILayout.Button("Clear", GUILayout.Width(44f)))
                _measurementFilter = string.Empty;
            GUILayout.Space(8f);
            GUILayout.Label("Sort:", GUILayout.Width(30f));
            int sort = (int)_settings.MeasurementListSort;
            if (GUILayout.Toggle(sort == (int)MeasurementSortMode.Creation, "Created", GUI.skin.button, GUILayout.Width(66f)))
                sort = (int)MeasurementSortMode.Creation;
            if (GUILayout.Toggle(sort == (int)MeasurementSortMode.Name, "Name", GUI.skin.button, GUILayout.Width(48f)))
                sort = (int)MeasurementSortMode.Name;
            if (GUILayout.Toggle(sort == (int)MeasurementSortMode.Type, "Type", GUI.skin.button, GUILayout.Width(48f)))
                sort = (int)MeasurementSortMode.Type;
            if (GUILayout.Toggle(sort == (int)MeasurementSortMode.Value, "Value", GUI.skin.button, GUILayout.Width(50f)))
                sort = (int)MeasurementSortMode.Value;
            if (sort != (int)_settings.MeasurementListSort)
            {
                _settings.MeasurementListSort = (MeasurementSortMode)sort;
                MarkSettingsDirty();
            }
            string direction = _settings.MeasurementListSortAscending ? "▲" : "▼";
            if (GUILayout.Button(direction, GUILayout.Width(26f)))
            {
                _settings.MeasurementListSortAscending = !_settings.MeasurementListSortAscending;
                MarkSettingsDirty();
            }
            GUILayout.EndHorizontal();

            MeasurementListLayout listLayout = CalculateMeasurementListLayout();
            DrawMeasurementListHeader(listLayout);

            List<MeasurementDisplayGroup> groups = BuildDisplayGroups();
            int visibleRows = 0;
            for (int g = 0; g < groups.Count; ++g)
                visibleRows += 1 + (IsGroupExpanded(groups[g].Name) ? groups[g].Items.Count : 0);
            float height = Mathf.Clamp(visibleRows * 28f + 4f, 68f, 230f);
            _measurementScroll = GUILayout.BeginScrollView(_measurementScroll,
                GUILayout.Height(height), GUILayout.ExpandWidth(true));

            _listHoverSeen = false;
            if (groups.Count == 0)
            {
                GUILayout.Label(_measurements.Count == 0
                    ? "No measurements. Click New Distance or New Angle."
                    : "No measurements match the filter.");
            }
            else
            {
                bool stop = false;
                for (int g = 0; g < groups.Count && !stop; ++g)
                {
                    MeasurementDisplayGroup group = groups[g];
                    if (!DrawMeasurementGroupHeader(group)) continue;
                    for (int i = 0; i < group.Items.Count; ++i)
                    {
                        if (DrawEnhancedMeasurementRow(group.Items[i], listLayout))
                        {
                            stop = true;
                            break;
                        }
                    }
                }
            }
            if (!_listHoverSeen)
                _hoveredListMeasurementId = null;

            GUILayout.EndScrollView();
            GUILayout.Label("Click a color swatch to cycle its color. Group headings collapse and show/hide all measurements in that group.");
            GUILayout.EndVertical();
        }

        private List<MeasurementDisplayGroup> BuildDisplayGroups()
        {
            Dictionary<string, MeasurementDisplayGroup> map =
                new Dictionary<string, MeasurementDisplayGroup>(StringComparer.OrdinalIgnoreCase);
            string filter = (_measurementFilter ?? string.Empty).Trim();

            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null || !MeasurementMatchesFilter(m, filter)) continue;
                string groupName = GetDisplayGroupName(m);
                MeasurementDisplayGroup group;
                if (!map.TryGetValue(groupName, out group))
                {
                    group = new MeasurementDisplayGroup { Name = groupName };
                    map.Add(groupName, group);
                }
                group.Items.Add(m);
            }

            List<MeasurementDisplayGroup> result = new List<MeasurementDisplayGroup>(map.Values);
            if (_settings.MeasurementListSort == MeasurementSortMode.Creation)
            {
                result.Sort(delegate(MeasurementDisplayGroup a, MeasurementDisplayGroup b)
                {
                    int ai = FirstCreationIndex(a);
                    int bi = FirstCreationIndex(b);
                    int compare = ai.CompareTo(bi);
                    return _settings.MeasurementListSortAscending ? compare : -compare;
                });
            }
            else
            {
                result.Sort(delegate(MeasurementDisplayGroup a, MeasurementDisplayGroup b)
                {
                    bool au = string.Equals(a.Name, "Ungrouped", StringComparison.OrdinalIgnoreCase);
                    bool bu = string.Equals(b.Name, "Ungrouped", StringComparison.OrdinalIgnoreCase);
                    if (au != bu) return au ? -1 : 1;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            for (int i = 0; i < result.Count; ++i)
                SortDisplayMeasurements(result[i].Items);
            return result;
        }

        private int FirstCreationIndex(MeasurementDisplayGroup group)
        {
            int best = int.MaxValue;
            if (group == null) return best;
            for (int i = 0; i < group.Items.Count; ++i)
            {
                int index = _measurements.IndexOf(group.Items[i]);
                if (index >= 0 && index < best) best = index;
            }
            return best;
        }

        private bool MeasurementMatchesFilter(MeasurementRecord m, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string type = m.Kind == MeasurementKind.Angle ? "Angle" : "Distance";
            return ContainsIgnoreCase(m.Name, filter) ||
                   ContainsIgnoreCase(GetDisplayGroupName(m), filter) ||
                   ContainsIgnoreCase(m.Notes, filter) ||
                   ContainsIgnoreCase(type, filter);
        }

        private static bool ContainsIgnoreCase(string value, string filter)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SortDisplayMeasurements(List<MeasurementRecord> items)
        {
            if (items == null || items.Count < 2 || _settings.MeasurementListSort == MeasurementSortMode.Creation)
            {
                if (!_settings.MeasurementListSortAscending && items != null)
                    items.Reverse();
                return;
            }

            items.Sort(delegate(MeasurementRecord a, MeasurementRecord b)
            {
                int compare = 0;
                switch (_settings.MeasurementListSort)
                {
                    case MeasurementSortMode.Name:
                        compare = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        break;
                    case MeasurementSortMode.Type:
                        compare = a.Kind.CompareTo(b.Kind);
                        if (compare == 0)
                            compare = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        break;
                    case MeasurementSortMode.Value:
                        compare = GetSortableMeasurementValue(a).CompareTo(GetSortableMeasurementValue(b));
                        break;
                }
                return _settings.MeasurementListSortAscending ? compare : -compare;
            });
        }

        private static float GetSortableMeasurementValue(MeasurementRecord m)
        {
            if (m == null || !m.IsComplete) return float.MaxValue;
            return m.Kind == MeasurementKind.Angle ? m.AngleDegrees : m.Distance;
        }

        private bool IsGroupExpanded(string groupName)
        {
            bool expanded;
            return !_groupExpanded.TryGetValue(groupName, out expanded) || expanded;
        }

        private bool DrawMeasurementGroupHeader(MeasurementDisplayGroup group)
        {
            bool expanded;
            if (!_groupExpanded.TryGetValue(group.Name, out expanded))
                expanded = true;

            bool allVisible = AreAllMeasurementsInGroupVisible(group.Name);
            GUILayout.BeginHorizontal(GUI.skin.box);
            bool groupVisible = GUILayout.Toggle(allVisible, string.Empty, GUILayout.Width(22f));
            if (groupVisible != allVisible)
                SetGroupVisibility(group.Name, groupVisible);

            GUIStyle groupHeaderStyle = new GUIStyle(GUI.skin.label);
            groupHeaderStyle.fontStyle = FontStyle.Bold;
            groupHeaderStyle.alignment = TextAnchor.MiddleLeft;
            string text = (expanded ? "▼ " : "▶ ") + group.Name + "  (" + group.Items.Count + ")";
            if (GUILayout.Button(text, groupHeaderStyle, GUILayout.ExpandWidth(true), GUILayout.Height(23f)))
            {
                expanded = !expanded;
                _groupExpanded[group.Name] = expanded;
                RequestMainWindowResize();
            }
            GUILayout.EndHorizontal();
            return expanded;
        }

        private bool AreAllMeasurementsInGroupVisible(string displayGroupName)
        {
            bool any = false;
            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null || !string.Equals(GetDisplayGroupName(m), displayGroupName, StringComparison.OrdinalIgnoreCase)) continue;
                any = true;
                if (!m.Visible) return false;
            }
            return any;
        }

        private void SetGroupVisibility(string displayGroupName, bool visible)
        {
            CommitPendingNameUndo();
            PushMeasurementUndo((visible ? "Show group " : "Hide group ") + displayGroupName);
            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m != null && string.Equals(GetDisplayGroupName(m), displayGroupName, StringComparison.OrdinalIgnoreCase))
                    m.Visible = visible;
            }
            MarkPersistenceDirty(true);
        }

        private bool DrawEnhancedMeasurementRow(MeasurementRecord m, MeasurementListLayout minimumLayout)
        {
            Rect row = GUILayoutUtility.GetRect(
                minimumLayout.TotalWidth,
                26f,
                GUILayout.MinWidth(minimumLayout.TotalWidth),
                GUILayout.ExpandWidth(true));
            MeasurementListLayout layout = ExpandMeasurementDataColumns(minimumLayout, row.width);
            bool rowHovered = row.Contains(Event.current.mousePosition);
            if (rowHovered)
            {
                _hoveredListMeasurementId = m.Id;
                _listHoverSeen = true;
            }
            if (rowHovered || string.Equals(m.Id, _hoveredEditorMeasurementId, StringComparison.Ordinal))
                GUI.Box(row, GUIContent.none, GUI.skin.box);

            float x = row.x;
            Rect visibilityRect = new Rect(x, row.y, layout.VisibilityWidth, row.height); x += layout.VisibilityWidth + layout.Gap;
            Rect colorRect = new Rect(x, row.y + 3f, layout.ColorWidth, row.height - 6f); x += layout.ColorWidth + layout.Gap;
            Rect typeRect = new Rect(x, row.y, layout.TypeWidth, row.height); x += layout.TypeWidth + layout.Gap;
            Rect nameRect = new Rect(x, row.y, layout.NameWidth, row.height); x += layout.NameWidth + layout.NameGap;
            Rect lockRect = new Rect(x, row.y, layout.LockWidth, row.height); x += layout.LockWidth + layout.Gap;
            Rect valueRect = new Rect(x, row.y, layout.ValueWidth, row.height); x += layout.ValueWidth + layout.Gap;
            Rect xRect = new Rect(x, row.y, layout.XWidth, row.height); x += layout.XWidth + layout.Gap;
            Rect yRect = new Rect(x, row.y, layout.YWidth, row.height); x += layout.YWidth + layout.Gap;
            Rect zRect = new Rect(x, row.y, layout.ZWidth, row.height); x += layout.ZWidth + layout.Gap;
            Rect deleteRect = new Rect(x, row.y, layout.DeleteWidth, row.height);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rowHovered &&
                !visibilityRect.Contains(Event.current.mousePosition) &&
                !colorRect.Contains(Event.current.mousePosition) &&
                !deleteRect.Contains(Event.current.mousePosition))
            {
                SelectMeasurement(m);
            }

            bool visible = GUI.Toggle(visibilityRect, m.Visible, string.Empty);
            if (visible != m.Visible)
            {
                CommitPendingNameUndo();
                PushMeasurementUndo(visible ? "Show measurement" : "Hide measurement");
                m.Visible = visible;
                MarkPersistenceDirty(true);
            }

            Color oldGuiColor = GUI.color;
            GUI.color = m.DisplayColor;
            if (GUI.Button(colorRect, GUIContent.none))
                CycleMeasurementColor(m);
            GUI.color = oldGuiColor;

            GUI.Label(typeRect, m.Kind == MeasurementKind.Angle ? "Angle" : "Distance");
            GUIStyle nameStyle = m == _selectedMeasurement || rowHovered || m.Id == _hoveredEditorMeasurementId
                ? _selectedNameStyle : GUI.skin.button;
            if (GUI.Button(nameRect, new GUIContent(GetMeasurementListDisplayName(m.Name), m.Name), nameStyle))
                SelectMeasurement(m);
            GUI.Label(lockRect, m.LockMode == MeasurementLockMode.PartRelative ? "Part" : "Vessel");

            if (m.IsComplete)
            {
                if (m.Kind == MeasurementKind.Angle)
                {
                    string angleText = FormatAngle(m.AngleDegrees);
                    if (GUI.Button(valueRect, angleText, _valueButtonStyle)) CopyToClipboard(angleText, "Angle");
                    GUI.Label(xRect, "--"); GUI.Label(yRect, "--"); GUI.Label(zRect, "--");
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
                GUI.Label(valueRect, "--"); GUI.Label(xRect, "--"); GUI.Label(yRect, "--"); GUI.Label(zRect, "--");
            }

            bool delete = GUI.Button(deleteRect, "X", _deleteButtonStyle);
            if (!delete) return false;
            DeleteMeasurement(m);
            return true;
        }

        private void CycleMeasurementColor(MeasurementRecord measurement)
        {
            if (measurement == null) return;
            int nearest = FindNearestPaletteColor(measurement.DisplayColor);
            int next = (nearest + 1) % MeasurementPalette.Length;
            PushMeasurementUndo("Change measurement color");
            measurement.DisplayColor = MeasurementPalette[next];
            MarkPersistenceDirty(true);
        }

        private static int FindNearestPaletteColor(Color color)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < MeasurementPalette.Length; ++i)
            {
                Color p = MeasurementPalette[i];
                float d = (color.r - p.r) * (color.r - p.r) +
                          (color.g - p.g) * (color.g - p.g) +
                          (color.b - p.b) * (color.b - p.b);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        private Color GetNextMeasurementColor()
        {
            return MeasurementPalette[_measurements.Count % MeasurementPalette.Length];
        }

        private static string GetDisplayGroupName(MeasurementRecord m)
        {
            return m == null || string.IsNullOrEmpty(m.Group) ? "Ungrouped" : m.Group;
        }

        private void DrawSelectedGroupAndColor()
        {
            if (_selectedMeasurement == null) return;

            if (_groupEditMeasurementId != _selectedMeasurement.Id)
            {
                _groupEditMeasurementId = _selectedMeasurement.Id;
                _groupEditBuffer = _selectedMeasurement.Group ?? string.Empty;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Group:", GUILayout.Width(70f));
            GUI.SetNextControlName(MeasurementGroupControlName);
            _groupEditBuffer = GUILayout.TextField(_groupEditBuffer ?? string.Empty, GUILayout.Width(210f));
            _groupFieldFocused = GUI.GetNameOfFocusedControl() == MeasurementGroupControlName;
            if (GUILayout.Button("Set", GUILayout.Width(52f)))
            {
                string normalized = NormalizeGroupName(_groupEditBuffer);
                if (!string.Equals(normalized, _selectedMeasurement.Group, StringComparison.Ordinal))
                {
                    PushMeasurementUndo("Change measurement group");
                    _selectedMeasurement.Group = normalized;
                    _groupEditBuffer = normalized;
                    MarkPersistenceDirty(true);
                    RequestMainWindowResize();
                }
            }
            if (GUILayout.Button("Clear", GUILayout.Width(52f)))
            {
                if (!string.IsNullOrEmpty(_selectedMeasurement.Group))
                {
                    PushMeasurementUndo("Clear measurement group");
                    _selectedMeasurement.Group = string.Empty;
                    _groupEditBuffer = string.Empty;
                    MarkPersistenceDirty(true);
                    RequestMainWindowResize();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Color:", GUILayout.Width(70f));
            for (int i = 0; i < MeasurementPalette.Length; ++i)
            {
                Color old = GUI.color;
                GUI.color = MeasurementPalette[i];
                if (GUILayout.Button(GUIContent.none, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (FindNearestPaletteColor(_selectedMeasurement.DisplayColor) != i)
                    {
                        PushMeasurementUndo("Change measurement color");
                        _selectedMeasurement.DisplayColor = MeasurementPalette[i];
                        MarkPersistenceDirty(true);
                    }
                }
                GUI.color = old;
            }
            GUILayout.EndHorizontal();
        }

        private static string NormalizeGroupName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private void UpdateEditorHover(Camera camera)
        {
            _hoveredEditorMeasurementId = null;
            if (!_windowVisible)
            {
                _hoveredListMeasurementId = null;
                return;
            }
            if (IsMainWindowTemporarilyHidden())
                _hoveredListMeasurementId = null;
            if (camera == null || _measurementMode || _endpointEditMode) return;
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (IsPointerOverGui(guiMouse)) return;

            Vector2 mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            float best = 13f;
            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m == null || !m.Visible || !m.IsComplete) continue;
                float distance = MeasurementScreenDistance(camera, m, mouse, _settings != null && _settings.ShowMeasurementLines);
                if (distance < best)
                {
                    best = distance;
                    _hoveredEditorMeasurementId = m.Id;
                }
            }
        }

        private void HandleEditorMeasurementSelectionClick(Camera camera)
        {
            if (camera == null || _measurementMode || _endpointEditMode ||
                !Input.GetMouseButtonDown(0) || string.IsNullOrEmpty(_hoveredEditorMeasurementId))
                return;

            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (IsPointerOverGui(guiMouse)) return;

            MeasurementRecord measurement = FindMeasurementById(_hoveredEditorMeasurementId);
            if (measurement == null || !measurement.Visible) return;

            SelectMeasurement(measurement);
            SetStatus(measurement.Name + " selected from the editor view.", 4f);
        }

        private MeasurementRecord FindMeasurementById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _measurements.Count; ++i)
            {
                MeasurementRecord m = _measurements[i];
                if (m != null && string.Equals(m.Id, id, StringComparison.Ordinal))
                    return m;
            }
            return null;
        }

        private static float MeasurementScreenDistance(Camera camera, MeasurementRecord m, Vector2 mouse, bool includeLines)
        {
            float best = float.MaxValue;
            Vector3 a3 = camera.WorldToScreenPoint(m.GetWorldPosition(m.PointA));
            Vector3 b3 = camera.WorldToScreenPoint(m.GetWorldPosition(m.PointB));
            if (a3.z > 0f) best = Mathf.Min(best, Vector2.Distance(mouse, new Vector2(a3.x, a3.y)));
            if (b3.z > 0f) best = Mathf.Min(best, Vector2.Distance(mouse, new Vector2(b3.x, b3.y)));
            if (includeLines && a3.z > 0f && b3.z > 0f)
                best = Mathf.Min(best, DistanceToSegment(mouse, new Vector2(a3.x, a3.y), new Vector2(b3.x, b3.y)));

            if (m.Kind == MeasurementKind.Angle && m.PointC != null)
            {
                Vector3 c3 = camera.WorldToScreenPoint(m.GetWorldPosition(m.PointC));
                if (c3.z > 0f) best = Mathf.Min(best, Vector2.Distance(mouse, new Vector2(c3.x, c3.y)));
                if (includeLines && b3.z > 0f && c3.z > 0f)
                    best = Mathf.Min(best, DistanceToSegment(mouse, new Vector2(b3.x, b3.y), new Vector2(c3.x, c3.y)));
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            return Vector2.Distance(p, a + ab * t);
        }

        private string GetHoverMeasurementId()
        {
            return !string.IsNullOrEmpty(_hoveredListMeasurementId)
                ? _hoveredListMeasurementId
                : _hoveredEditorMeasurementId;
        }

        private string GetActiveAxisConstraintName()
        {
            if (_settings == null) return null;
            if (_settings.ShortcutAxisX != null && _settings.ShortcutAxisX.IsHeldAllowExtraModifiers()) return "X";
            if (_settings.ShortcutAxisY != null && _settings.ShortcutAxisY.IsHeldAllowExtraModifiers()) return "Y";
            if (_settings.ShortcutAxisZ != null && _settings.ShortcutAxisZ.IsHeldAllowExtraModifiers()) return "Z";
            return null;
        }

        private bool ApplyAxisConstraintIfHeld(ref Vector3 worldPoint, out string description)
        {
            description = null;
            string axisName = GetActiveAxisConstraintName();
            if (axisName == null) return false;

            Vector3 axisLocal = axisName == "X"
                ? Vector3.right
                : axisName == "Y"
                    ? Vector3.up
                    : Vector3.forward;

            bool hasAnchor;
            Vector3 anchor;
            TryGetSnapAxisAnchor(out hasAnchor, out anchor);
            if (!hasAnchor) return false;

            Quaternion rotation = EditorLogic.VesselRotation;
            Vector3 axisWorld = (rotation * axisLocal).normalized;

            // Constrain from the mouse ray, not from the surface point under the
            // mouse. Projecting a surface hit onto the axis can make the live
            // preview appear to lag behind or detach from the cursor as the
            // underlying vessel geometry changes. The closest point between the
            // camera ray and the infinite vessel-axis line follows the mouse in
            // screen space as closely as the camera view permits.
            Camera camera = GetEditorCamera();
            if (camera != null)
            {
                Ray mouseRay = camera.ScreenPointToRay(Input.mousePosition);
                Vector3 rayDirection = mouseRay.direction.normalized;
                Vector3 fromAnchorToRay = mouseRay.origin - anchor;

                float rayDotAxis = Vector3.Dot(rayDirection, axisWorld);
                float denominator = 1f - rayDotAxis * rayDotAxis;

                // When the view ray is almost parallel to the constrained axis,
                // there is no stable screen-space solution. In that special case
                // fall back to the previous world-point projection.
                if (Mathf.Abs(denominator) > 0.00001f)
                {
                    float rayOffset = Vector3.Dot(rayDirection, fromAnchorToRay);
                    float axisOffset = Vector3.Dot(axisWorld, fromAnchorToRay);
                    float axisDistance = (axisOffset - rayDotAxis * rayOffset) / denominator;
                    worldPoint = anchor + axisWorld * axisDistance;
                    description = "Vessel " + axisName + " axis constraint";
                    return true;
                }
            }

            Vector3 localDelta = Quaternion.Inverse(rotation) * (worldPoint - anchor);
            float component = axisName == "X" ? localDelta.x : axisName == "Y" ? localDelta.y : localDelta.z;
            worldPoint = anchor + rotation * axisLocal * component;
            description = "Vessel " + axisName + " axis constraint";
            return true;
        }

        private void DrawKeyboardShortcutSettings(float listHeight)
        {
            GUILayout.Label("Keyboard bindings", _statusStyle);
            GUILayout.Space(8f);
            GUILayout.Label(_capturingShortcut == ShortcutAction.None
                ? "Click a binding, then press the replacement key combination."
                : "Press the new key combination for " + ShortcutActionLabel(_capturingShortcut) + ".");
            GUILayout.Space(5f);

            // Keep all shortcut bindings in a dedicated list box immediately
            // below the Settings tabs. This avoids making the entire Settings
            // window taller as more key bindings are added.
            GUILayout.BeginVertical(GUI.skin.box);
            _keyboardShortcutScroll = GUILayout.BeginScrollView(
                _keyboardShortcutScroll,
                GUILayout.Height(listHeight),
                GUILayout.ExpandWidth(true));

            DrawShortcutRow("Measurement mode", ShortcutAction.ToggleMeasurement);
            DrawShortcutRow("Cancel mode", ShortcutAction.CancelMode);
            DrawShortcutRow("New distance", ShortcutAction.NewDistance);
            DrawShortcutRow("New angle", ShortcutAction.NewAngle);
            DrawShortcutRow("Edit endpoints", ShortcutAction.EditEndpoints);
            DrawShortcutRow("Toggle labels", ShortcutAction.ToggleLabels);
            DrawShortcutRow("Delete selected", ShortcutAction.DeleteSelected);
            DrawShortcutRow("Copy selected", ShortcutAction.CopySelected);
            DrawShortcutRow("Undo", ShortcutAction.Undo);
            DrawShortcutRow("Redo", ShortcutAction.Redo);
            DrawShortcutRow("Redo alternate", ShortcutAction.RedoAlternate);
            DrawShortcutRow("Snap while held", ShortcutAction.SnapModifier);
            DrawShortcutRow("Constrain vessel X", ShortcutAction.AxisX);
            DrawShortcutRow("Constrain vessel Y", ShortcutAction.AxisY);
            DrawShortcutRow("Constrain vessel Z", ShortcutAction.AxisZ);

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(6f);

            if (GUILayout.Button("Reset keyboard shortcuts to defaults", GUILayout.Width(260f)))
            {
                _settings.ResetKeyboardShortcuts();
                _capturingShortcut = ShortcutAction.None;
                MarkSettingsDirty(true);
            }
        }

        private void DrawShortcutRow(string label, ShortcutAction action)
        {
            ShortcutBinding binding = GetShortcutBinding(action);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(155f));
            string text = _capturingShortcut == action ? "Press key..." : (binding != null ? binding.ToString() : "Unbound");
            if (GUILayout.Button(text, GUILayout.Width(135f)))
                _capturingShortcut = action;
            if (GUILayout.Button("Clear", GUILayout.Width(44f)))
            {
                SetShortcutBinding(action, new ShortcutBinding(KeyCode.None));
                _capturingShortcut = ShortcutAction.None;
                MarkSettingsDirty(true);
            }
            GUILayout.EndHorizontal();
        }

        private void HandleShortcutCaptureEvent()
        {
            if (_capturingShortcut == ShortcutAction.None || Event.current == null || Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;
            if (key == KeyCode.None) return;
            if (ShortcutBinding.IsModifierKey(key) && _capturingShortcut != ShortcutAction.SnapModifier)
                return;
            if (key == KeyCode.Escape && _capturingShortcut != ShortcutAction.CancelMode)
            {
                _capturingShortcut = ShortcutAction.None;
                Event.current.Use();
                return;
            }

            SetShortcutBinding(_capturingShortcut, ShortcutBinding.FromEvent(Event.current));
            SetStatus(ShortcutActionLabel(_capturingShortcut) + " shortcut updated.", 4f);
            _capturingShortcut = ShortcutAction.None;
            MarkSettingsDirty(true);
            Event.current.Use();
        }

        private ShortcutBinding GetShortcutBinding(ShortcutAction action)
        {
            switch (action)
            {
                case ShortcutAction.ToggleMeasurement: return _settings.ShortcutToggleMeasurement;
                case ShortcutAction.CancelMode: return _settings.ShortcutCancelMode;
                case ShortcutAction.NewDistance: return _settings.ShortcutNewDistance;
                case ShortcutAction.NewAngle: return _settings.ShortcutNewAngle;
                case ShortcutAction.EditEndpoints: return _settings.ShortcutEditEndpoints;
                case ShortcutAction.ToggleLabels: return _settings.ShortcutToggleLabels;
                case ShortcutAction.DeleteSelected: return _settings.ShortcutDeleteSelected;
                case ShortcutAction.CopySelected: return _settings.ShortcutCopySelected;
                case ShortcutAction.Undo: return _settings.ShortcutUndo;
                case ShortcutAction.Redo: return _settings.ShortcutRedo;
                case ShortcutAction.RedoAlternate: return _settings.ShortcutRedoAlternate;
                case ShortcutAction.SnapModifier: return _settings.ShortcutSnapModifier;
                case ShortcutAction.AxisX: return _settings.ShortcutAxisX;
                case ShortcutAction.AxisY: return _settings.ShortcutAxisY;
                case ShortcutAction.AxisZ: return _settings.ShortcutAxisZ;
                default: return null;
            }
        }

        private void SetShortcutBinding(ShortcutAction action, ShortcutBinding binding)
        {
            switch (action)
            {
                case ShortcutAction.ToggleMeasurement: _settings.ShortcutToggleMeasurement = binding; break;
                case ShortcutAction.CancelMode: _settings.ShortcutCancelMode = binding; break;
                case ShortcutAction.NewDistance: _settings.ShortcutNewDistance = binding; break;
                case ShortcutAction.NewAngle: _settings.ShortcutNewAngle = binding; break;
                case ShortcutAction.EditEndpoints: _settings.ShortcutEditEndpoints = binding; break;
                case ShortcutAction.ToggleLabels: _settings.ShortcutToggleLabels = binding; break;
                case ShortcutAction.DeleteSelected: _settings.ShortcutDeleteSelected = binding; break;
                case ShortcutAction.CopySelected: _settings.ShortcutCopySelected = binding; break;
                case ShortcutAction.Undo: _settings.ShortcutUndo = binding; break;
                case ShortcutAction.Redo: _settings.ShortcutRedo = binding; break;
                case ShortcutAction.RedoAlternate: _settings.ShortcutRedoAlternate = binding; break;
                case ShortcutAction.SnapModifier: _settings.ShortcutSnapModifier = binding; break;
                case ShortcutAction.AxisX: _settings.ShortcutAxisX = binding; break;
                case ShortcutAction.AxisY: _settings.ShortcutAxisY = binding; break;
                case ShortcutAction.AxisZ: _settings.ShortcutAxisZ = binding; break;
            }
        }

        private static string ShortcutActionLabel(ShortcutAction action)
        {
            switch (action)
            {
                case ShortcutAction.ToggleMeasurement: return "Measurement mode";
                case ShortcutAction.CancelMode: return "Cancel mode";
                case ShortcutAction.NewDistance: return "New distance";
                case ShortcutAction.NewAngle: return "New angle";
                case ShortcutAction.EditEndpoints: return "Edit endpoints";
                case ShortcutAction.ToggleLabels: return "Toggle labels";
                case ShortcutAction.DeleteSelected: return "Delete selected";
                case ShortcutAction.CopySelected: return "Copy selected";
                case ShortcutAction.Undo: return "Undo";
                case ShortcutAction.Redo: return "Redo";
                case ShortcutAction.RedoAlternate: return "Redo alternate";
                case ShortcutAction.SnapModifier: return "Snap modifier";
                case ShortcutAction.AxisX: return "Vessel X constraint";
                case ShortcutAction.AxisY: return "Vessel Y constraint";
                case ShortcutAction.AxisZ: return "Vessel Z constraint";
                default: return action.ToString();
            }
        }
    }
}
