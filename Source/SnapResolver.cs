using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TapeMeasure
{
    internal enum SnapTargetKind
    {
        None = 0,
        PartOrigin,
        AttachmentNode,
        SurfaceAttachment,
        PartCenter,
        VesselRoot,
        CenterOfMass,
        CenterOfLift,
        CenterOfThrust,
        VesselAxis,
        VesselGrid
    }

    internal struct SnapResult
    {
        public bool IsValid;
        public SnapTargetKind Kind;
        public Part ReferencePart;
        public Vector3 WorldPosition;
        public string NodeId;
        public string Label;
        public float PixelDistance;
    }

    /// <summary>
    /// Resolves all editor snap targets in one place. Several target types may
    /// be enabled simultaneously; the target nearest the mouse in screen space
    /// wins, provided it is within the configured snap radius.
    /// </summary>
    internal sealed class SnapResolver
    {
        private const float CenterRefreshSeconds = 0.15f;

        private ShipConstruct _cachedShip;
        private float _nextCenterRefresh;
        private bool _hasCoM;
        private bool _hasCoL;
        private bool _hasCoT;
        private Vector3 _coM;
        private Vector3 _coL;
        private Vector3 _coT;

        private static readonly FieldInfo CoMOffsetField = typeof(Part).GetField(
            "CoMOffset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo CoMOffsetProperty = typeof(Part).GetProperty(
            "CoMOffset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public bool TryResolve(
            ShipConstruct ship,
            Part hoveredPart,
            Vector3 rawWorldPoint,
            bool hasRawWorldPoint,
            Camera camera,
            bool snappingActive,
            TapeMeasureSettings settings,
            bool hasAxisAnchor,
            Vector3 axisAnchorWorld,
            out SnapResult result)
        {
            result = new SnapResult();
            if (!snappingActive || ship == null || ship.parts == null || ship.parts.Count == 0 || camera == null || settings == null)
                return false;

            float maxPixels = Mathf.Max(4f, settings.SnapPixelRadius);
            float best = maxPixels;
            Vector2 mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            Part root = GetRootPart(ship);

            if (hoveredPart != null)
            {
                if (settings.SnapPartOrigin)
                    EvaluatePoint(hoveredPart.transform.position, hoveredPart, null,
                        SnapTargetKind.PartOrigin, "Part origin", camera, mouse, ref best, ref result);

                if (settings.SnapAttachmentNodes)
                    EvaluateAttachmentNodes(hoveredPart, false, camera, mouse, ref best, ref result);

                if (settings.SnapSurfaceAttachmentPoint)
                    EvaluateAttachmentNodes(hoveredPart, true, camera, mouse, ref best, ref result);

                if (settings.SnapPartCenter)
                {
                    Vector3 partCenter;
                    if (TryGetPartVisualCenter(hoveredPart, out partCenter))
                        EvaluatePoint(partCenter, hoveredPart, null,
                            SnapTargetKind.PartCenter, "Part center", camera, mouse, ref best, ref result);
                }
            }

            if (root != null)
            {
                if (settings.SnapVesselRoot)
                    EvaluatePoint(root.transform.position, root, null,
                        SnapTargetKind.VesselRoot, "Vessel root", camera, mouse, ref best, ref result);

                if (settings.SnapCenterOfMass || settings.SnapCenterOfLift || settings.SnapCenterOfThrust)
                {
                    RefreshVesselCenters(ship);

                    if (settings.SnapCenterOfMass && _hasCoM)
                        EvaluatePoint(_coM, root, null,
                            SnapTargetKind.CenterOfMass, "Center of Mass", camera, mouse, ref best, ref result);

                    if (settings.SnapCenterOfLift && _hasCoL)
                        EvaluatePoint(_coL, root, null,
                            SnapTargetKind.CenterOfLift, "Center of Lift", camera, mouse, ref best, ref result);

                    if (settings.SnapCenterOfThrust && _hasCoT)
                        EvaluatePoint(_coT, root, null,
                            SnapTargetKind.CenterOfThrust, "Center of Thrust", camera, mouse, ref best, ref result);
                }

                if (settings.SnapVesselAxisGrid && hasRawWorldPoint)
                {
                    Quaternion vesselRotation = EditorLogic.VesselRotation;
                    Quaternion inverseRotation = Quaternion.Inverse(vesselRotation);

                    if (hasAxisAnchor)
                    {
                        Vector3 localDelta = inverseRotation * (rawWorldPoint - axisAnchorWorld);
                        EvaluatePoint(axisAnchorWorld + vesselRotation * new Vector3(localDelta.x, 0f, 0f),
                            root, null, SnapTargetKind.VesselAxis, "Vessel X axis", camera, mouse, ref best, ref result);
                        EvaluatePoint(axisAnchorWorld + vesselRotation * new Vector3(0f, localDelta.y, 0f),
                            root, null, SnapTargetKind.VesselAxis, "Vessel Y axis", camera, mouse, ref best, ref result);
                        EvaluatePoint(axisAnchorWorld + vesselRotation * new Vector3(0f, 0f, localDelta.z),
                            root, null, SnapTargetKind.VesselAxis, "Vessel Z axis", camera, mouse, ref best, ref result);
                    }

                    float grid = Mathf.Max(0.001f, settings.VesselGridSize);
                    Vector3 rootOrigin = root.transform.position;
                    Vector3 local = inverseRotation * (rawWorldPoint - rootOrigin);
                    Vector3 snappedLocal = new Vector3(
                        Mathf.Round(local.x / grid) * grid,
                        Mathf.Round(local.y / grid) * grid,
                        Mathf.Round(local.z / grid) * grid);
                    Vector3 gridPoint = rootOrigin + vesselRotation * snappedLocal;
                    EvaluatePoint(gridPoint, root, null,
                        SnapTargetKind.VesselGrid,
                        "Vessel grid (" + grid.ToString("0.###") + " m)",
                        camera, mouse, ref best, ref result);
                }
            }

            return result.IsValid;
        }

        public void Invalidate()
        {
            _cachedShip = null;
            _nextCenterRefresh = 0f;
            _hasCoM = _hasCoL = _hasCoT = false;
        }

        private static Part GetRootPart(ShipConstruct ship)
        {
            if (ship == null || ship.parts == null || ship.parts.Count == 0)
                return null;
            return ship.parts[0];
        }

        private static void EvaluateAttachmentNodes(
            Part part,
            bool surfaceOnly,
            Camera camera,
            Vector2 mouse,
            ref float best,
            ref SnapResult result)
        {
            if (part == null || part.transform == null)
                return;

            if (surfaceOnly)
            {
                AttachNode node = part.srfAttachNode;
                if (node != null)
                    EvaluatePoint(part.transform.TransformPoint(node.position), part, node.id,
                        SnapTargetKind.SurfaceAttachment, "Surface attachment point", camera, mouse, ref best, ref result);
                return;
            }

            if (part.attachNodes == null)
                return;

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < part.attachNodes.Count; ++i)
            {
                AttachNode node = part.attachNodes[i];
                if (node == null || string.IsNullOrEmpty(node.id) || visited.Contains(node.id))
                    continue;
                visited.Add(node.id);

                // The dedicated surface-attachment mode owns the surface node.
                if (part.srfAttachNode != null && ReferenceEquals(node, part.srfAttachNode))
                    continue;

                EvaluatePoint(part.transform.TransformPoint(node.position), part, node.id,
                    SnapTargetKind.AttachmentNode, "Attachment node " + node.id, camera, mouse, ref best, ref result);
            }
        }

        private static void EvaluatePoint(
            Vector3 world,
            Part referencePart,
            string nodeId,
            SnapTargetKind kind,
            string label,
            Camera camera,
            Vector2 mouse,
            ref float best,
            ref SnapResult result)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
                return;

            float pixels = Vector2.Distance(new Vector2(screen.x, screen.y), mouse);
            if (pixels > best)
                return;

            best = pixels;
            result.IsValid = true;
            result.Kind = kind;
            result.ReferencePart = referencePart;
            result.WorldPosition = world;
            result.NodeId = string.IsNullOrEmpty(nodeId) ? null : nodeId;
            result.Label = label;
            result.PixelDistance = pixels;
        }

        private static bool TryGetPartVisualCenter(Part part, out Vector3 center)
        {
            center = part != null && part.transform != null ? part.transform.position : Vector3.zero;
            if (part == null || part.gameObject == null)
                return false;

            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds combined = new Bounds();
            for (int i = 0; i < renderers.Length; ++i)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!any)
                {
                    combined = renderer.bounds;
                    any = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            if (!any)
                return part.transform != null;

            center = combined.center;
            return true;
        }

        private void RefreshVesselCenters(ShipConstruct ship)
        {
            if (_cachedShip == ship && Time.unscaledTime < _nextCenterRefresh)
                return;

            _cachedShip = ship;
            _nextCenterRefresh = Time.unscaledTime + CenterRefreshSeconds;
            _hasCoM = TryCalculateCenterOfMass(ship, out _coM);
            _hasCoL = TryCalculateCenterOfLift(ship, out _coL);
            _hasCoT = TryCalculateCenterOfThrust(ship, out _coT);
        }

        private static bool TryCalculateCenterOfMass(ShipConstruct ship, out Vector3 center)
        {
            center = Vector3.zero;
            if (ship == null || ship.parts == null || ship.parts.Count == 0)
                return false;

            double totalMass = 0.0;
            Vector3d weighted = Vector3d.zero;

            for (int i = 0; i < ship.parts.Count; ++i)
            {
                Part part = ship.parts[i];
                if (part == null || part.transform == null)
                    continue;

                double mass = Math.Max(0.0, part.mass);
                try { mass += Math.Max(0.0, part.GetResourceMass()); }
                catch { }

                if (mass <= 0.0)
                    continue;

                Vector3 localOffset = GetCoMOffset(part);
                Vector3 world = part.transform.TransformPoint(localOffset);
                weighted += (Vector3d)world * mass;
                totalMass += mass;
            }

            if (totalMass <= 1e-9)
                return false;

            center = (Vector3)(weighted / totalMass);
            return true;
        }

        private static Vector3 GetCoMOffset(Part part)
        {
            if (part == null)
                return Vector3.zero;

            try
            {
                if (CoMOffsetField != null)
                {
                    object value = CoMOffsetField.GetValue(part);
                    if (value is Vector3)
                        return (Vector3)value;
                }
                if (CoMOffsetProperty != null)
                {
                    object value = CoMOffsetProperty.GetValue(part, null);
                    if (value is Vector3)
                        return (Vector3)value;
                }
            }
            catch { }

            return Vector3.zero;
        }

        private static bool TryCalculateCenterOfLift(ShipConstruct ship, out Vector3 center)
        {
            center = Vector3.zero;
            if (ship == null || ship.parts == null || ship.parts.Count == 0)
                return false;

            try
            {
                CenterOfLiftQuery query = new CenterOfLiftQuery();
                query.Reset();
                for (int i = 0; i < ship.parts.Count; ++i)
                {
                    Part part = ship.parts[i];
                    if (part != null)
                        part.SendMessage("OnCenterOfLiftQuery", query, SendMessageOptions.DontRequireReceiver);
                }

                if (Mathf.Abs(query.lift) <= 1e-6f)
                    return false;

                center = query.pos / query.lift;
                return IsFinite(center);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCalculateCenterOfThrust(ShipConstruct ship, out Vector3 center)
        {
            center = Vector3.zero;
            if (ship == null || ship.parts == null || ship.parts.Count == 0)
                return false;

            try
            {
                CenterOfThrustQuery query = new CenterOfThrustQuery();
                query.Reset();
                for (int i = 0; i < ship.parts.Count; ++i)
                {
                    Part part = ship.parts[i];
                    if (part != null)
                        part.SendMessage("OnCenterOfThrustQuery", query, SendMessageOptions.DontRequireReceiver);
                }

                if (Mathf.Abs(query.thrust) <= 1e-6f)
                    return false;

                center = query.pos / query.thrust;
                return IsFinite(center);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }
    }
}
