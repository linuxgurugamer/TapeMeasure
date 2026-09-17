using System;
using UnityEngine;

namespace TapeMeasure
{
    /// <summary>
    /// Visible vessel envelope in TapeMeasure's vessel/editor frame.
    /// X = width, Y = height, Z = length (fore/aft).
    /// </summary>
    internal struct VesselDimensions
    {
        public bool IsValid;
        public Vector3 Minimum;
        public Vector3 Maximum;

        public Vector3 Size
        {
            get { return IsValid ? Maximum - Minimum : Vector3.zero; }
        }

        public float Width
        {
            get { return Size.x; }
        }

        public float Height
        {
            get { return Size.y; }
        }

        public float Length
        {
            get { return Size.z; }
        }
    }

    /// <summary>
    /// Calculates the editor craft's visible bounding envelope from active
    /// render geometry. Mesh-local bounds are transformed into the same
    /// vessel frame used by TapeMeasure axis distances.
    /// </summary>
    internal static class VesselDimensionsCalculator
    {
        public static VesselDimensions Calculate(ShipConstruct ship, Quaternion vesselRotation)
        {
            VesselDimensions result = new VesselDimensions();
            if (ship == null || ship.parts == null || ship.parts.Count == 0)
                return result;

            Quaternion toVessel = Quaternion.Inverse(vesselRotation);
            bool any = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            for (int i = 0; i < ship.parts.Count; ++i)
            {
                Part part = ship.parts[i];
                if (part == null || part.gameObject == null || !part.gameObject.activeInHierarchy)
                    continue;

                Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; ++r)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;

                    Bounds localBounds;
                    Transform boundsTransform;
                    if (!TryGetLocalBounds(renderer, out localBounds, out boundsTransform))
                        continue;

                    AddLocalBounds(localBounds, boundsTransform.localToWorldMatrix,
                        toVessel, ref any, ref min, ref max);
                }
            }

            if (!any)
                return result;

            result.IsValid = true;
            result.Minimum = min;
            result.Maximum = max;
            return result;
        }

        private static bool TryGetLocalBounds(Renderer renderer, out Bounds bounds, out Transform transform)
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                bounds = skinned.localBounds;
                transform = skinned.transform;
                return true;
            }

            MeshRenderer meshRenderer = renderer as MeshRenderer;
            if (meshRenderer != null)
            {
                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    bounds = filter.sharedMesh.bounds;
                    transform = filter.transform;
                    return true;
                }
            }

            bounds = new Bounds();
            transform = null;
            return false;
        }

        private static void AddLocalBounds(
            Bounds bounds,
            Matrix4x4 localToWorld,
            Quaternion toVessel,
            ref bool any,
            ref Vector3 min,
            ref Vector3 max)
        {
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 local = c + new Vector3(e.x * x, e.y * y, e.z * z);
                        Vector3 world = localToWorld.MultiplyPoint3x4(local);
                        AddPoint(toVessel * world, ref any, ref min, ref max);
                    }
                }
            }
        }

        private static void AddPoint(Vector3 point, ref bool any, ref Vector3 min, ref Vector3 max)
        {
            if (!any)
            {
                min = point;
                max = point;
                any = true;
                return;
            }

            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
    }
}
