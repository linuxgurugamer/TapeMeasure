using System.Collections.Generic;
using UnityEngine;

namespace TapeMeasure
{
    internal sealed class MeasurementRenderer
    {
        private const string RootName = "TapeMeasure_RenderRoot";

        private readonly GameObject _root;
        private readonly Material _markerMaterialA;
        private readonly Material _markerMaterialB;
        private readonly Material _markerMaterialC;
        private readonly Material _lineMaterial;
        private readonly Material _previewMaterial;
        private readonly LineRenderer _previewLine;
        private readonly Material _guideMaterialX;
        private readonly Material _guideMaterialY;
        private readonly Material _guideMaterialZ;
        private readonly Material _boundingBoxMaterial;
        private readonly LineRenderer _boundingBoxLine;
        private readonly Dictionary<string, MeasurementVisual> _visuals =
            new Dictionary<string, MeasurementVisual>();

        private readonly Color _colorA = new Color(0.20f, 1.00f, 0.25f, 0.95f);
        private readonly Color _colorB = new Color(1.00f, 0.65f, 0.10f, 0.95f);
        private readonly Color _colorC = new Color(0.20f, 0.75f, 1.00f, 0.95f);
        private readonly Color _lineColor = new Color(1.00f, 0.90f, 0.10f, 0.95f);
        private readonly Color _previewColor = new Color(1.00f, 1.00f, 1.00f, 0.90f);
        private readonly Color _guideColorX = new Color(1.00f, 0.25f, 0.20f, 0.90f);
        private readonly Color _guideColorY = new Color(0.25f, 1.00f, 0.30f, 0.90f);
        private readonly Color _guideColorZ = new Color(0.25f, 0.55f, 1.00f, 0.90f);
        private readonly Color _boundingBoxColor = new Color(0.25f, 0.90f, 1.00f, 0.75f);

        public MeasurementRenderer()
        {
            _root = new GameObject(RootName);
            _root.hideFlags = HideFlags.HideAndDontSave;
            _markerMaterialA = CreateMaterial(_colorA);
            _markerMaterialB = CreateMaterial(_colorB);
            _markerMaterialC = CreateMaterial(_colorC);
            _lineMaterial = CreateMaterial(Color.white);
            _previewMaterial = CreateMaterial(_previewColor);
            _previewLine = CreateLine("Measurement Preview", _root.transform, _previewMaterial);
            _guideMaterialX = CreateMaterial(_guideColorX);
            _guideMaterialY = CreateMaterial(_guideColorY);
            _guideMaterialZ = CreateMaterial(_guideColorZ);
            _boundingBoxMaterial = CreateMaterial(_boundingBoxColor);
            _boundingBoxLine = CreateLine("Vessel Bounding Box", _root.transform, _boundingBoxMaterial);
        }

        public void Update(
            IList<MeasurementRecord> measurements,
            Camera camera,
            bool visible,
            string selectedId,
            string hoveredId,
            TapeMeasureSettings settings)
        {
            if (_root == null) return;
            _root.SetActive(visible);
            if (!visible) return;

            if (settings == null) settings = new TapeMeasureSettings();
            HashSet<string> activeIds = new HashSet<string>();

            if (measurements != null)
            {
                for (int i = 0; i < measurements.Count; ++i)
                {
                    MeasurementRecord m = measurements[i];
                    if (m == null) continue;
                    activeIds.Add(m.Id);
                    MeasurementVisual visual;
                    if (!_visuals.TryGetValue(m.Id, out visual))
                    {
                        visual = CreateVisual(m.Id);
                        _visuals.Add(m.Id, visual);
                    }

                    visual.Root.SetActive(m.Visible);
                    if (m.Visible)
                        UpdateVisual(visual, m, camera,
                            m.Id == selectedId,
                            m.Id == hoveredId,
                            settings);
                }
            }

            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, MeasurementVisual> pair in _visuals)
                if (!activeIds.Contains(pair.Key)) remove.Add(pair.Key);

            for (int i = 0; i < remove.Count; ++i)
            {
                DestroyVisual(_visuals[remove[i]]);
                _visuals.Remove(remove[i]);
            }
        }

        public void UpdateBoundingBox(
            Camera camera,
            bool visible,
            VesselDimensions dimensions,
            Quaternion vesselRotation,
            TapeMeasureSettings settings)
        {
            if (_boundingBoxLine == null)
                return;

            bool enabled = visible && settings != null && settings.ShowBoundingBox && dimensions.IsValid;
            _boundingBoxLine.enabled = enabled;
            if (!enabled)
                return;

            Vector3 min = dimensions.Minimum;
            Vector3 max = dimensions.Maximum;
            Vector3[] c = new Vector3[8];
            c[0] = vesselRotation * new Vector3(min.x, min.y, min.z);
            c[1] = vesselRotation * new Vector3(max.x, min.y, min.z);
            c[2] = vesselRotation * new Vector3(min.x, max.y, min.z);
            c[3] = vesselRotation * new Vector3(max.x, max.y, min.z);
            c[4] = vesselRotation * new Vector3(min.x, min.y, max.z);
            c[5] = vesselRotation * new Vector3(max.x, min.y, max.z);
            c[6] = vesselRotation * new Vector3(min.x, max.y, max.z);
            c[7] = vesselRotation * new Vector3(max.x, max.y, max.z);

            int[] path = new int[]
            {
                0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4
            };

            _boundingBoxLine.positionCount = path.Length;
            for (int i = 0; i < path.Length; ++i)
                _boundingBoxLine.SetPosition(i, c[path[i]]);

            _boundingBoxLine.startColor = _boundingBoxColor;
            _boundingBoxLine.endColor = _boundingBoxColor;

            if (camera != null)
            {
                Vector3 centerLocal = (min + max) * 0.5f;
                Vector3 centerWorld = vesselRotation * centerLocal;
                float distance = Vector3.Distance(camera.transform.position, centerWorld);
                float width = Mathf.Clamp(distance * 0.0012f, 0.006f, 0.10f)
                    * settings.LineWidthMultiplier;
                _boundingBoxLine.startWidth = width;
                _boundingBoxLine.endWidth = width;
            }
        }

        public void UpdatePreview(
            Camera camera,
            bool visible,
            Vector3 start,
            Vector3 end,
            TapeMeasureSettings settings)
        {
            if (_previewLine == null) return;

            _previewLine.enabled = visible;
            if (!visible) return;

            _previewLine.positionCount = 2;
            _previewLine.SetPosition(0, start);
            _previewLine.SetPosition(1, end);
            _previewLine.startColor = _previewColor;
            _previewLine.endColor = _previewColor;

            if (camera == null) return;
            if (settings == null) settings = new TapeMeasureSettings();

            Vector3 midpoint = (start + end) * 0.5f;
            float distance = Vector3.Distance(camera.transform.position, midpoint);
            float width = Mathf.Clamp(distance * 0.0015f, 0.008f, 0.12f)
                * settings.LineWidthMultiplier;
            _previewLine.startWidth = width;
            _previewLine.endWidth = width;
        }

        public void Destroy()
        {
            foreach (KeyValuePair<string, MeasurementVisual> pair in _visuals)
                DestroyVisual(pair.Value);
            _visuals.Clear();

            if (_root != null) Object.Destroy(_root);
            if (_markerMaterialA != null) Object.Destroy(_markerMaterialA);
            if (_markerMaterialB != null) Object.Destroy(_markerMaterialB);
            if (_markerMaterialC != null) Object.Destroy(_markerMaterialC);
            if (_lineMaterial != null) Object.Destroy(_lineMaterial);
            if (_previewMaterial != null) Object.Destroy(_previewMaterial);
            if (_guideMaterialX != null) Object.Destroy(_guideMaterialX);
            if (_guideMaterialY != null) Object.Destroy(_guideMaterialY);
            if (_guideMaterialZ != null) Object.Destroy(_guideMaterialZ);
            if (_boundingBoxMaterial != null) Object.Destroy(_boundingBoxMaterial);
        }

        private MeasurementVisual CreateVisual(string id)
        {
            MeasurementVisual visual = new MeasurementVisual();
            visual.Root = new GameObject("Measurement_" + id);
            visual.Root.hideFlags = HideFlags.HideAndDontSave;
            visual.Root.transform.SetParent(_root.transform, false);

            visual.MarkerA = CreateMarker("Point A", visual.Root.transform, _markerMaterialA, out visual.RendererA);
            visual.MarkerB = CreateMarker("Point B", visual.Root.transform, _markerMaterialB, out visual.RendererB);
            visual.MarkerC = CreateMarker("Point C", visual.Root.transform, _markerMaterialC, out visual.RendererC);

            visual.Line = CreateLine("Measurement Line", visual.Root.transform, _lineMaterial);
            visual.AngleArc = CreateLine("Angle Arc", visual.Root.transform, _lineMaterial);
            visual.EndCapA = CreateLine("Dimension End A", visual.Root.transform, _lineMaterial);
            visual.EndCapB = CreateLine("Dimension End B", visual.Root.transform, _lineMaterial);
            visual.GuideAX = CreateLine("Guide A X", visual.Root.transform, _guideMaterialX);
            visual.GuideAY = CreateLine("Guide A Y", visual.Root.transform, _guideMaterialY);
            visual.GuideAZ = CreateLine("Guide A Z", visual.Root.transform, _guideMaterialZ);
            visual.GuideBX = CreateLine("Guide B X", visual.Root.transform, _guideMaterialX);
            visual.GuideBY = CreateLine("Guide B Y", visual.Root.transform, _guideMaterialY);
            visual.GuideBZ = CreateLine("Guide B Z", visual.Root.transform, _guideMaterialZ);

            return visual;
        }

        private void UpdateVisual(
            MeasurementVisual visual,
            MeasurementRecord m,
            Camera camera,
            bool selected,
            bool hovered,
            TapeMeasureSettings settings)
        {
            bool hasA = m.HasPointA;
            bool hasB = m.HasPointB;
            bool hasC = m.Kind == MeasurementKind.Angle && m.HasPointC;

            visual.MarkerA.SetActive(hasA);
            visual.MarkerB.SetActive(hasB);
            visual.MarkerC.SetActive(hasC);

            if (hasA) visual.MarkerA.transform.position = m.GetWorldPosition(m.PointA);
            if (hasB) visual.MarkerB.transform.position = m.GetWorldPosition(m.PointB);
            if (hasC) visual.MarkerC.transform.position = m.GetWorldPosition(m.PointC);

            bool hasLineGeometry = hasA && hasB;
            visual.Line.enabled = settings.ShowMeasurementLines && hasLineGeometry;
            if (hasLineGeometry)
            {
                if (m.Kind == MeasurementKind.Angle && hasC)
                {
                    visual.Line.positionCount = 3;
                    visual.Line.SetPosition(0, m.GetWorldPosition(m.PointA));
                    visual.Line.SetPosition(1, m.GetWorldPosition(m.PointB));
                    visual.Line.SetPosition(2, m.GetWorldPosition(m.PointC));
                }
                else
                {
                    visual.Line.positionCount = 2;
                    visual.Line.SetPosition(0, m.GetWorldPosition(m.PointA));
                    visual.Line.SetPosition(1, m.GetWorldPosition(m.PointB));
                }
            }

            bool showAngleArc = settings.ShowAngleArcs &&
                                m.Kind == MeasurementKind.Angle &&
                                m.IsComplete;
            UpdateAngleArc(visual.AngleArc, m, showAngleArc);

            bool emphasized = selected || hovered;
            float opacity = hovered || selected || !settings.EmphasizeSelected ? 1f : settings.InactiveOpacity;
            Color measurementColor = m.DisplayColor;
            measurementColor.a = 1f;
            Color visualColor = WithAlpha(measurementColor, opacity);
            SetRendererColor(visual.RendererA, visualColor);
            SetRendererColor(visual.RendererB, visualColor);
            SetRendererColor(visual.RendererC, visualColor);

            visual.Line.startColor = visualColor;
            visual.Line.endColor = visualColor;
            visual.AngleArc.startColor = visualColor;
            visual.AngleArc.endColor = visualColor;
            visual.EndCapA.startColor = visualColor;
            visual.EndCapA.endColor = visualColor;
            visual.EndCapB.startColor = visualColor;
            visual.EndCapB.endColor = visualColor;

            bool showGuides = selected &&
                              settings.ShowMeasurementGuides &&
                              m.Kind == MeasurementKind.Distance &&
                              m.IsComplete;
            UpdateGuideGeometry(visual, m, showGuides);

            if (camera == null) return;

            float selectedMarker = hovered
                ? Mathf.Max(1.35f, settings.SelectedMarkerMultiplier)
                : (selected && settings.EmphasizeSelected ? settings.SelectedMarkerMultiplier : 1f);
            float selectedLine = hovered
                ? Mathf.Max(1.50f, settings.SelectedLineMultiplier)
                : (selected && settings.EmphasizeSelected ? settings.SelectedLineMultiplier : 1f);

            if (hasA) ScaleMarker(visual.MarkerA, camera, settings.MarkerSizeMultiplier * selectedMarker);
            if (hasB) ScaleMarker(visual.MarkerB, camera, settings.MarkerSizeMultiplier * selectedMarker);
            if (hasC) ScaleMarker(visual.MarkerC, camera, settings.MarkerSizeMultiplier * selectedMarker);

            if (hasLineGeometry)
            {
                Vector3 reference = m.Kind == MeasurementKind.Angle && hasB
                    ? m.GetWorldPosition(m.PointB)
                    : (m.GetWorldPosition(m.PointA) + m.GetWorldPosition(m.PointB)) * 0.5f;
                float distance = Vector3.Distance(camera.transform.position, reference);
                float width = Mathf.Clamp(distance * 0.0015f, 0.008f, 0.12f)
                    * settings.LineWidthMultiplier * selectedLine;

                if (visual.Line.enabled)
                {
                    visual.Line.startWidth = width;
                    visual.Line.endWidth = width;
                }

                if (visual.AngleArc.enabled)
                {
                    visual.AngleArc.startWidth = width * 0.80f;
                    visual.AngleArc.endWidth = width * 0.80f;
                }

                if (showGuides)
                {
                    float guideWidth = width * 0.65f;
                    SetGuideWidth(visual, guideWidth);
                }

                bool showEndCaps = settings.ShowDimensionEndCaps &&
                                   settings.ShowMeasurementLines &&
                                   m.Kind == MeasurementKind.Distance &&
                                   m.IsComplete;
                UpdateDimensionEndCaps(visual, m, camera, showEndCaps, width);
            }
            else
            {
                visual.EndCapA.enabled = false;
                visual.EndCapB.enabled = false;
            }
        }

        private static void UpdateDimensionEndCaps(
            MeasurementVisual visual,
            MeasurementRecord measurement,
            Camera camera,
            bool enabled,
            float lineWidth)
        {
            visual.EndCapA.enabled = enabled;
            visual.EndCapB.enabled = enabled;
            if (!enabled || camera == null) return;

            Vector3 a = measurement.GetWorldPosition(measurement.PointA);
            Vector3 b = measurement.GetWorldPosition(measurement.PointB);
            Vector3 direction = b - a;
            if (direction.sqrMagnitude < 1e-8f)
            {
                visual.EndCapA.enabled = false;
                visual.EndCapB.enabled = false;
                return;
            }

            direction.Normalize();

            // Keep the tick perpendicular to the measurement as seen by the
            // camera.  Using the view vector at the measurement midpoint gives
            // a stable screen-facing orientation even when the editor camera
            // is not aligned with camera.transform.forward.
            Vector3 midpoint = (a + b) * 0.5f;
            Vector3 viewDirection = (midpoint - camera.transform.position).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, viewDirection);
            if (perpendicular.sqrMagnitude < 1e-6f)
                perpendicular = Vector3.Cross(direction, camera.transform.up);
            if (perpendicular.sqrMagnitude < 1e-6f)
                perpendicular = Vector3.Cross(direction, camera.transform.right);
            if (perpendicular.sqrMagnitude < 1e-6f)
                perpendicular = Vector3.up;
            perpendicular.Normalize();

            float distance = Vector3.Distance(camera.transform.position, midpoint);

            // Make the CAD ticks deliberately larger/thicker than the normal
            // measurement line.  The endpoints lie on the vessel surface, so
            // move the tick centers slightly toward the camera to prevent the
            // vessel skin or endpoint spheres from hiding them.
            float halfLength = Mathf.Clamp(distance * 0.016f, 0.060f, 0.50f);
            float surfaceOffset = Mathf.Clamp(distance * 0.0015f, 0.004f, 0.05f);
            Vector3 aTowardCamera = (camera.transform.position - a).normalized;
            Vector3 bTowardCamera = (camera.transform.position - b).normalized;
            Vector3 capA = a + aTowardCamera * surfaceOffset;
            Vector3 capB = b + bTowardCamera * surfaceOffset;

            SetGuide(visual.EndCapA, capA - perpendicular * halfLength, capA + perpendicular * halfLength);
            SetGuide(visual.EndCapB, capB - perpendicular * halfLength, capB + perpendicular * halfLength);
            SetLineWidth(visual.EndCapA, lineWidth * 1.6f);
            SetLineWidth(visual.EndCapB, lineWidth * 1.6f);
        }

        private static void UpdateAngleArc(
            LineRenderer arc,
            MeasurementRecord measurement,
            bool enabled)
        {
            if (arc == null)
                return;

            arc.enabled = enabled;
            if (!enabled)
                return;

            Vector3 vertex = measurement.GetWorldPosition(measurement.PointB);
            Vector3 ba = measurement.GetWorldPosition(measurement.PointA) - vertex;
            Vector3 bc = measurement.GetWorldPosition(measurement.PointC) - vertex;
            float lenA = ba.magnitude;
            float lenC = bc.magnitude;
            if (lenA < 0.0001f || lenC < 0.0001f)
            {
                arc.enabled = false;
                return;
            }

            const int segments = 24;
            float radius = Mathf.Max(0.02f, Mathf.Min(lenA, lenC) * 0.22f);
            Vector3 dirA = ba / lenA;
            Vector3 dirC = bc / lenC;

            arc.positionCount = segments + 1;
            for (int i = 0; i <= segments; ++i)
            {
                float t = i / (float)segments;
                Vector3 dir = Vector3.Slerp(dirA, dirC, t);
                if (dir.sqrMagnitude < 1e-8f)
                    dir = Vector3.Lerp(dirA, dirC, t).normalized;
                arc.SetPosition(i, vertex + dir.normalized * radius);
            }
        }

        private static void UpdateGuideGeometry(
            MeasurementVisual visual,
            MeasurementRecord m,
            bool enabled)
        {
            SetGuideEnabled(visual, enabled);
            if (!enabled) return;

            Vector3 a = m.GetWorldPosition(m.PointA);
            Vector3 b = m.GetWorldPosition(m.PointB);
            Quaternion rotation = EditorLogic.VesselRotation;
            Vector3 delta = m.AxisDelta(rotation);

            Vector3 x = rotation * Vector3.right * delta.x;
            Vector3 y = rotation * Vector3.up * delta.y;
            Vector3 z = rotation * Vector3.forward * delta.z;

            SetGuide(visual.GuideAX, a, a + x);
            SetGuide(visual.GuideAY, a, a + y);
            SetGuide(visual.GuideAZ, a, a + z);

            SetGuide(visual.GuideBX, b, b - x);
            SetGuide(visual.GuideBY, b, b - y);
            SetGuide(visual.GuideBZ, b, b - z);
        }

        private static void SetGuideEnabled(MeasurementVisual visual, bool enabled)
        {
            visual.GuideAX.enabled = enabled;
            visual.GuideAY.enabled = enabled;
            visual.GuideAZ.enabled = enabled;
            visual.GuideBX.enabled = enabled;
            visual.GuideBY.enabled = enabled;
            visual.GuideBZ.enabled = enabled;
        }

        private static void SetGuideWidth(MeasurementVisual visual, float width)
        {
            SetLineWidth(visual.GuideAX, width);
            SetLineWidth(visual.GuideAY, width);
            SetLineWidth(visual.GuideAZ, width);
            SetLineWidth(visual.GuideBX, width);
            SetLineWidth(visual.GuideBY, width);
            SetLineWidth(visual.GuideBZ, width);
        }

        private static void SetGuide(LineRenderer line, Vector3 start, Vector3 end)
        {
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private static void SetLineWidth(LineRenderer line, float width)
        {
            line.startWidth = width;
            line.endWidth = width;
        }

        private static LineRenderer CreateLine(string name, Transform parent, Material material)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.hideFlags = HideFlags.HideAndDontSave;
            lineObject.transform.SetParent(parent, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.sharedMaterial = material;
            line.startColor = material.color;
            line.endColor = material.color;
            line.numCapVertices = 4;
            line.enabled = false;
            return line;
        }

        private static GameObject CreateMarker(string name, Transform parent, Material material, out Renderer renderer)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.hideFlags = HideFlags.HideAndDontSave;
            marker.transform.SetParent(parent, false);

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            renderer = marker.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;

            marker.SetActive(false);
            return marker;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (shader == null) shader = Shader.Find("Diffuse");

            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            return material;
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        private static Color WithAlpha(Color color, float opacity)
        {
            color.a *= Mathf.Clamp01(opacity);
            return color;
        }

        private static void ScaleMarker(GameObject marker, Camera camera, float multiplier)
        {
            float distance = Vector3.Distance(camera.transform.position, marker.transform.position);
            float diameter = Mathf.Clamp(distance * 0.012f, 0.05f, 0.40f) * multiplier;
            marker.transform.localScale = Vector3.one * diameter;
        }

        private static void DestroyVisual(MeasurementVisual visual)
        {
            if (visual != null && visual.Root != null) Object.Destroy(visual.Root);
        }

        private sealed class MeasurementVisual
        {
            public GameObject Root;
            public GameObject MarkerA;
            public GameObject MarkerB;
            public GameObject MarkerC;
            public Renderer RendererA;
            public Renderer RendererB;
            public Renderer RendererC;
            public LineRenderer Line;
            public LineRenderer AngleArc;
            public LineRenderer EndCapA;
            public LineRenderer EndCapB;

            public LineRenderer GuideAX;
            public LineRenderer GuideAY;
            public LineRenderer GuideAZ;
            public LineRenderer GuideBX;
            public LineRenderer GuideBY;
            public LineRenderer GuideBZ;
        }
    }
}
