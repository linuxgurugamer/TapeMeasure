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

        public MeasurementRenderer()
        {
            _root = new GameObject(RootName);
            _root.hideFlags = HideFlags.HideAndDontSave;
            _markerMaterialA = CreateMaterial(_colorA);
            _markerMaterialB = CreateMaterial(_colorB);
            _markerMaterialC = CreateMaterial(_colorC);
            _lineMaterial = CreateMaterial(_lineColor);
            _previewMaterial = CreateMaterial(_previewColor);
            _previewLine = CreateLine("Measurement Preview", _root.transform, _previewMaterial);
            _guideMaterialX = CreateMaterial(_guideColorX);
            _guideMaterialY = CreateMaterial(_guideColorY);
            _guideMaterialZ = CreateMaterial(_guideColorZ);
        }

        public void Update(
            IList<MeasurementRecord> measurements,
            Camera camera,
            bool visible,
            string selectedId,
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
                    UpdateVisual(visual, m, camera, m.Id == selectedId, settings);
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

            float opacity = selected || !settings.EmphasizeSelected ? 1f : settings.InactiveOpacity;
            SetRendererColor(visual.RendererA, WithAlpha(_colorA, opacity));
            SetRendererColor(visual.RendererB, WithAlpha(_colorB, opacity));
            SetRendererColor(visual.RendererC, WithAlpha(_colorC, opacity));

            Color lineColor = WithAlpha(_lineColor, opacity);
            visual.Line.startColor = lineColor;
            visual.Line.endColor = lineColor;

            bool showGuides = selected &&
                              settings.ShowMeasurementGuides &&
                              m.Kind == MeasurementKind.Distance &&
                              m.IsComplete;
            UpdateGuideGeometry(visual, m, showGuides);

            if (camera == null) return;

            float selectedMarker = selected && settings.EmphasizeSelected
                ? settings.SelectedMarkerMultiplier : 1f;
            float selectedLine = selected && settings.EmphasizeSelected
                ? settings.SelectedLineMultiplier : 1f;

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

                if (showGuides)
                {
                    float guideWidth = width * 0.65f;
                    SetGuideWidth(visual, guideWidth);
                }
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

            public LineRenderer GuideAX;
            public LineRenderer GuideAY;
            public LineRenderer GuideAZ;
            public LineRenderer GuideBX;
            public LineRenderer GuideBY;
            public LineRenderer GuideBZ;
        }
    }
}
