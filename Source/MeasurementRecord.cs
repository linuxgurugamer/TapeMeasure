using System;
using UnityEngine;

namespace TapeMeasure
{
    internal enum MeasurementKind
    {
        Distance = 0,
        Angle = 1
    }

    internal enum MeasurementEndpoint
    {
        None = 0,
        A = 1,
        B = 2,
        C = 3
    }

    internal sealed class MeasurementRecord
    {
        public string Id { get; private set; }
        public string Name { get; set; }
        public MeasurementKind Kind { get; private set; }
        public MeasurementLockMode LockMode { get; private set; }

        public MeasurementPoint PointA { get; private set; }
        public MeasurementPoint PointB { get; private set; }
        public MeasurementPoint PointC { get; private set; }

        public MeasurementRecord(MeasurementKind kind, string name, MeasurementLockMode lockMode)
            : this(Guid.NewGuid().ToString("N"), name, kind, lockMode, null, null, null)
        {
        }

        public MeasurementRecord(
            string id,
            string name,
            MeasurementKind kind,
            MeasurementLockMode lockMode,
            MeasurementPoint pointA,
            MeasurementPoint pointB,
            MeasurementPoint pointC)
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
            Name = string.IsNullOrEmpty(name) ? "Measurement" : name;
            Kind = kind;
            LockMode = lockMode;
            PointA = pointA;
            PointB = pointB;
            PointC = pointC;
        }

        public bool HasPointA { get { return PointA != null && PointA.IsValid(LockMode); } }
        public bool HasPointB { get { return PointB != null && PointB.IsValid(LockMode); } }
        public bool HasPointC { get { return PointC != null && PointC.IsValid(LockMode); } }

        public bool IsComplete
        {
            get
            {
                return Kind == MeasurementKind.Angle
                    ? HasPointA && HasPointB && HasPointC
                    : HasPointA && HasPointB;
            }
        }

        public float Distance
        {
            get
            {
                if (Kind != MeasurementKind.Distance || !IsComplete) return 0f;
                return Vector3.Distance(GetWorldPosition(PointA), GetWorldPosition(PointB));
            }
        }

        public float AngleDegrees
        {
            get
            {
                if (Kind != MeasurementKind.Angle || !IsComplete) return 0f;
                Vector3 b = GetWorldPosition(PointB);
                Vector3 ba = GetWorldPosition(PointA) - b;
                Vector3 bc = GetWorldPosition(PointC) - b;
                if (ba.sqrMagnitude < 1e-10f || bc.sqrMagnitude < 1e-10f) return 0f;
                return Vector3.Angle(ba, bc);
            }
        }

        public Vector3 WorldDelta
        {
            get
            {
                if (Kind != MeasurementKind.Distance || !IsComplete) return Vector3.zero;
                return GetWorldPosition(PointB) - GetWorldPosition(PointA);
            }
        }

        public Vector3 LabelPosition
        {
            get
            {
                if (!IsComplete) return Vector3.zero;
                if (Kind == MeasurementKind.Angle) return GetWorldPosition(PointB);
                return (GetWorldPosition(PointA) + GetWorldPosition(PointB)) * 0.5f;
            }
        }

        public Vector3 AxisDelta(Quaternion vesselRotation)
        {
            if (Kind != MeasurementKind.Distance || !IsComplete) return Vector3.zero;
            return Quaternion.Inverse(vesselRotation) * WorldDelta;
        }

        public Vector3 GetWorldPosition(MeasurementPoint point)
        {
            return point != null ? point.GetWorldPosition(LockMode) : Vector3.zero;
        }

        public void AddPoint(Part part, Vector3 worldPosition, string snapNodeId, ShipConstruct ship)
        {
            if (part == null || IsComplete) return;
            MeasurementPoint point = new MeasurementPoint(part, worldPosition, snapNodeId, ship);
            if (!HasPointA) { PointA = point; return; }
            if (!HasPointB) { PointB = point; return; }
            if (Kind == MeasurementKind.Angle && !HasPointC) PointC = point;
        }

        public MeasurementPoint GetPoint(MeasurementEndpoint endpoint)
        {
            switch (endpoint)
            {
                case MeasurementEndpoint.A: return PointA;
                case MeasurementEndpoint.B: return PointB;
                case MeasurementEndpoint.C: return PointC;
                default: return null;
            }
        }

        public bool ReplacePoint(
            MeasurementEndpoint endpoint,
            Part part,
            Vector3 worldPosition,
            string snapNodeId,
            ShipConstruct ship)
        {
            if (part == null || endpoint == MeasurementEndpoint.None) return false;
            if (endpoint == MeasurementEndpoint.C && Kind != MeasurementKind.Angle) return false;

            MeasurementPoint point = new MeasurementPoint(part, worldPosition, snapNodeId, ship);
            switch (endpoint)
            {
                case MeasurementEndpoint.A:
                    PointA = point;
                    return true;
                case MeasurementEndpoint.B:
                    PointB = point;
                    return true;
                case MeasurementEndpoint.C:
                    PointC = point;
                    return true;
                default:
                    return false;
            }
        }

        public void SetLockMode(MeasurementLockMode mode, ShipConstruct ship)
        {
            if (mode == LockMode) return;
            if (PointA != null) PointA.Rebase(LockMode, mode, ship);
            if (PointB != null) PointB.Rebase(LockMode, mode, ship);
            if (PointC != null) PointC.Rebase(LockMode, mode, ship);
            LockMode = mode;
        }

        public bool ReferencesInvalidPart()
        {
            if (PointA != null && !PointA.IsValid(LockMode)) return true;
            if (PointB != null && !PointB.IsValid(LockMode)) return true;
            if (Kind == MeasurementKind.Angle && PointC != null && !PointC.IsValid(LockMode)) return true;
            return false;
        }
    }
}
