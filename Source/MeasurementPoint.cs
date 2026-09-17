using UnityEngine;

namespace TapeMeasure
{
    internal enum MeasurementLockMode
    {
        PartRelative = 0,
        VesselRelative = 1
    }

    /// <summary>
    /// One endpoint. Every point retains both a part-relative and a vessel-root-
    /// relative representation so a completed measurement can switch lock modes
    /// without moving on screen.
    /// </summary>
    internal sealed class MeasurementPoint
    {
        public Part Part { get; private set; }
        public uint CraftId { get; private set; }
        public Vector3 PartLocalPosition { get; private set; }
        public string SnapNodeId { get; private set; }

        public Part VesselReferencePart { get; private set; }
        public uint VesselReferenceCraftId { get; private set; }
        public Vector3 VesselLocalPosition { get; private set; }
        public bool HasVesselLocalPosition { get; private set; }

        // Backward-compatible name used by older persistence code/data.
        public Vector3 LocalPosition { get { return PartLocalPosition; } }

        public MeasurementPoint(
            Part part,
            Vector3 worldPosition,
            string snapNodeId,
            ShipConstruct ship)
        {
            Part = part;
            CraftId = part != null ? part.craftID : 0u;
            SnapNodeId = string.IsNullOrEmpty(snapNodeId) ? null : snapNodeId;

            if (part != null)
            {
                AttachNode node = ResolveAttachNode(part, SnapNodeId);
                PartLocalPosition = node != null
                    ? node.position
                    : part.transform.InverseTransformPoint(worldPosition);
            }

            SetVesselReference(ship, worldPosition);
        }

        private MeasurementPoint(
            Part part,
            uint craftId,
            Vector3 partLocalPosition,
            string snapNodeId,
            Part vesselReferencePart,
            uint vesselReferenceCraftId,
            Vector3 vesselLocalPosition,
            bool hasVesselLocalPosition)
        {
            Part = part;
            CraftId = craftId;
            PartLocalPosition = partLocalPosition;
            SnapNodeId = string.IsNullOrEmpty(snapNodeId) ? null : snapNodeId;
            VesselReferencePart = vesselReferencePart;
            VesselReferenceCraftId = vesselReferenceCraftId;
            VesselLocalPosition = vesselLocalPosition;
            HasVesselLocalPosition = hasVesselLocalPosition;
        }

        public bool IsSnapped { get { return !string.IsNullOrEmpty(SnapNodeId); } }

        public bool IsValid(MeasurementLockMode mode)
        {
            if (mode == MeasurementLockMode.VesselRelative)
                return VesselReferencePart != null && VesselReferencePart.transform != null && HasVesselLocalPosition;

            return Part != null && Part.transform != null;
        }

        public Vector3 GetWorldPosition(MeasurementLockMode mode)
        {
            if (mode == MeasurementLockMode.VesselRelative &&
                VesselReferencePart != null &&
                VesselReferencePart.transform != null &&
                HasVesselLocalPosition)
            {
                return VesselReferencePart.transform.TransformPoint(VesselLocalPosition);
            }

            return GetPartRelativeWorldPosition();
        }

        public Vector3 GetPartRelativeWorldPosition()
        {
            if (Part == null || Part.transform == null)
                return Vector3.zero;

            AttachNode node = ResolveAttachNode(Part, SnapNodeId);
            if (node != null)
                return Part.transform.TransformPoint(node.position);

            return Part.transform.TransformPoint(PartLocalPosition);
        }

        public string PartTitle
        {
            get
            {
                if (Part == null)
                    return "(vessel-relative point)";

                string title = Part.partInfo != null && !string.IsNullOrEmpty(Part.partInfo.title)
                    ? Part.partInfo.title
                    : Part.name;

                if (IsSnapped)
                    title += " [" + SnapNodeId + "]";

                return title;
            }
        }

        public void Rebase(
            MeasurementLockMode oldMode,
            MeasurementLockMode newMode,
            ShipConstruct ship)
        {
            Vector3 world = GetWorldPosition(oldMode);

            if (newMode == MeasurementLockMode.VesselRelative)
            {
                SetVesselReference(ship, world);
            }
            else if (Part != null && Part.transform != null)
            {
                AttachNode node = ResolveAttachNode(Part, SnapNodeId);
                PartLocalPosition = node != null
                    ? node.position
                    : Part.transform.InverseTransformPoint(world);
            }
        }

        public MeasurementPoint CreateSymmetryCopy(Part targetPart, ShipConstruct ship)
        {
            if (targetPart == null || targetPart.transform == null)
                return null;

            string nodeId = SnapNodeId;
            AttachNode node = ResolveAttachNode(targetPart, nodeId);
            Vector3 world;

            if (node != null)
            {
                world = targetPart.transform.TransformPoint(node.position);
            }
            else
            {
                nodeId = null;
                world = targetPart.transform.TransformPoint(PartLocalPosition);
            }

            return new MeasurementPoint(targetPart, world, nodeId, ship);
        }

        public static MeasurementPoint Resolve(
            ShipConstruct ship,
            uint craftId,
            Vector3 partLocalPosition,
            string snapNodeId,
            uint vesselReferenceCraftId,
            Vector3 vesselLocalPosition,
            bool hasVesselLocalPosition,
            MeasurementLockMode lockMode)
        {
            if (ship == null || ship.parts == null || ship.parts.Count == 0)
                return null;

            Part part = FindPart(ship, craftId);
            Part vesselReferencePart = FindPart(ship, vesselReferenceCraftId);
            if (vesselReferencePart == null)
                vesselReferencePart = ship.parts[0];

            if (part == null && lockMode == MeasurementLockMode.PartRelative)
                return null;

            MeasurementPoint result = new MeasurementPoint(
                part,
                craftId,
                partLocalPosition,
                snapNodeId,
                vesselReferencePart,
                vesselReferencePart != null ? vesselReferencePart.craftID : vesselReferenceCraftId,
                vesselLocalPosition,
                hasVesselLocalPosition);

            // Version 3 files have no vessel coordinates. Derive them from the
            // restored part point the first time they are loaded.
            if (!result.HasVesselLocalPosition && part != null && vesselReferencePart != null)
            {
                Vector3 world = result.GetPartRelativeWorldPosition();
                result.VesselLocalPosition = vesselReferencePart.transform.InverseTransformPoint(world);
                result.HasVesselLocalPosition = true;
            }

            if (lockMode == MeasurementLockMode.VesselRelative && !result.HasVesselLocalPosition)
                return null;

            return result;
        }

        private void SetVesselReference(ShipConstruct ship, Vector3 worldPosition)
        {
            Part root = ship != null && ship.parts != null && ship.parts.Count > 0
                ? ship.parts[0]
                : null;

            VesselReferencePart = root;
            VesselReferenceCraftId = root != null ? root.craftID : 0u;

            if (root != null && root.transform != null)
            {
                VesselLocalPosition = root.transform.InverseTransformPoint(worldPosition);
                HasVesselLocalPosition = true;
            }
            else
            {
                VesselLocalPosition = Vector3.zero;
                HasVesselLocalPosition = false;
            }
        }

        private static Part FindPart(ShipConstruct ship, uint craftId)
        {
            if (craftId == 0u || ship == null || ship.parts == null)
                return null;

            for (int i = 0; i < ship.parts.Count; ++i)
            {
                Part part = ship.parts[i];
                if (part != null && part.craftID == craftId)
                    return part;
            }

            return null;
        }

        private static AttachNode ResolveAttachNode(Part part, string nodeId)
        {
            if (part == null || string.IsNullOrEmpty(nodeId))
                return null;

            // Surface attachment is kept separate from the normal attachNodes
            // list by KSP. Resolve it explicitly so a surface-snap endpoint
            // continues to follow the part after the craft is moved/reloaded.
            try
            {
                if (part.srfAttachNode != null &&
                    !string.IsNullOrEmpty(part.srfAttachNode.id) &&
                    string.Equals(part.srfAttachNode.id, nodeId, System.StringComparison.OrdinalIgnoreCase))
                    return part.srfAttachNode;

                return part.FindAttachNode(nodeId);
            }
            catch { return null; }
        }
    }
}
