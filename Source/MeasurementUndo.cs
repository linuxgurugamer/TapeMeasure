using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapeMeasure
{
    /// <summary>
    /// In-memory, measurement-only undo/redo history. It snapshots only
    /// TapeMeasure data, never KSP editor state, so Undo/Redo cannot modify
    /// vessel parts or interfere with KSP's own editor history.
    /// </summary>
    internal sealed class MeasurementUndoStack
    {
        private const int MaximumEntries = 50;
        private readonly List<MeasurementUndoEntry> _undoEntries = new List<MeasurementUndoEntry>();
        private readonly List<MeasurementUndoEntry> _redoEntries = new List<MeasurementUndoEntry>();

        public int Count { get { return _undoEntries.Count; } }
        public int RedoCount { get { return _redoEntries.Count; } }
        public bool CanUndo { get { return _undoEntries.Count > 0; } }
        public bool CanRedo { get { return _redoEntries.Count > 0; } }

        public string NextDescription
        {
            get
            {
                return _undoEntries.Count > 0
                    ? _undoEntries[_undoEntries.Count - 1].Description
                    : string.Empty;
            }
        }

        public string NextRedoDescription
        {
            get
            {
                return _redoEntries.Count > 0
                    ? _redoEntries[_redoEntries.Count - 1].Description
                    : string.Empty;
            }
        }

        public MeasurementUndoState Capture(
            IList<MeasurementRecord> measurements,
            MeasurementRecord selectedMeasurement)
        {
            return MeasurementUndoState.Capture(
                measurements,
                selectedMeasurement != null ? selectedMeasurement.Id : null);
        }

        /// <summary>
        /// Records a new user operation. Any new operation invalidates redo.
        /// </summary>
        public void Push(MeasurementUndoState state, string description)
        {
            if (state == null) return;
            AddBounded(_undoEntries, new MeasurementUndoEntry(
                state,
                string.IsNullOrEmpty(description) ? "measurement change" : description));
            _redoEntries.Clear();
        }

        public bool TryUndo(
            MeasurementUndoState currentState,
            out MeasurementUndoEntry entry)
        {
            if (_undoEntries.Count == 0)
            {
                entry = null;
                return false;
            }

            int index = _undoEntries.Count - 1;
            entry = _undoEntries[index];
            _undoEntries.RemoveAt(index);

            if (currentState != null)
                AddBounded(_redoEntries, new MeasurementUndoEntry(currentState, entry.Description));
            return true;
        }

        public bool TryRedo(
            MeasurementUndoState currentState,
            out MeasurementUndoEntry entry)
        {
            if (_redoEntries.Count == 0)
            {
                entry = null;
                return false;
            }

            int index = _redoEntries.Count - 1;
            entry = _redoEntries[index];
            _redoEntries.RemoveAt(index);

            if (currentState != null)
                AddBounded(_undoEntries, new MeasurementUndoEntry(currentState, entry.Description));
            return true;
        }

        public void Clear()
        {
            _undoEntries.Clear();
            _redoEntries.Clear();
        }

        private static void AddBounded(
            List<MeasurementUndoEntry> entries,
            MeasurementUndoEntry entry)
        {
            if (entries.Count >= MaximumEntries)
                entries.RemoveAt(0);
            entries.Add(entry);
        }
    }

    internal sealed class MeasurementUndoEntry
    {
        public MeasurementUndoState State { get; private set; }
        public string Description { get; private set; }

        public MeasurementUndoEntry(MeasurementUndoState state, string description)
        {
            State = state;
            Description = description;
        }
    }

    internal sealed class MeasurementUndoState
    {
        private readonly List<MeasurementRecordState> _records;
        public string SelectedMeasurementId { get; private set; }

        private MeasurementUndoState(
            List<MeasurementRecordState> records,
            string selectedMeasurementId)
        {
            _records = records;
            SelectedMeasurementId = selectedMeasurementId;
        }

        public static MeasurementUndoState Capture(
            IList<MeasurementRecord> measurements,
            string selectedMeasurementId)
        {
            List<MeasurementRecordState> records = new List<MeasurementRecordState>();
            if (measurements != null)
            {
                for (int i = 0; i < measurements.Count; ++i)
                {
                    MeasurementRecord measurement = measurements[i];
                    if (measurement != null)
                        records.Add(MeasurementRecordState.Capture(measurement));
                }
            }

            return new MeasurementUndoState(records, selectedMeasurementId);
        }

        public List<MeasurementRecord> Restore(
            ShipConstruct ship,
            out int unresolvedPointCount)
        {
            unresolvedPointCount = 0;
            List<MeasurementRecord> result = new List<MeasurementRecord>();

            for (int i = 0; i < _records.Count; ++i)
            {
                MeasurementRecord restored = _records[i].Restore(ship, ref unresolvedPointCount);
                if (restored != null)
                    result.Add(restored);
            }

            return result;
        }
    }

    internal sealed class MeasurementRecordState
    {
        private string _id;
        private string _name;
        private MeasurementKind _kind;
        private MeasurementLockMode _lockMode;
        private bool _visible;
        private string _group;
        private Color _displayColor;
        private string _notes;
        private MeasurementPointState _pointA;
        private MeasurementPointState _pointB;
        private MeasurementPointState _pointC;

        public static MeasurementRecordState Capture(MeasurementRecord measurement)
        {
            MeasurementRecordState state = new MeasurementRecordState();
            state._id = measurement.Id;
            state._name = measurement.Name;
            state._kind = measurement.Kind;
            state._lockMode = measurement.LockMode;
            state._visible = measurement.Visible;
            state._group = measurement.Group;
            state._displayColor = measurement.DisplayColor;
            state._notes = measurement.Notes;
            state._pointA = MeasurementPointState.Capture(measurement.PointA);
            state._pointB = MeasurementPointState.Capture(measurement.PointB);
            state._pointC = MeasurementPointState.Capture(measurement.PointC);
            return state;
        }

        public MeasurementRecord Restore(ShipConstruct ship, ref int unresolvedPointCount)
        {
            MeasurementPoint a = RestorePoint(_pointA, ship, _lockMode, ref unresolvedPointCount);
            MeasurementPoint b = RestorePoint(_pointB, ship, _lockMode, ref unresolvedPointCount);
            MeasurementPoint c = RestorePoint(_pointC, ship, _lockMode, ref unresolvedPointCount);

            return new MeasurementRecord(
                _id,
                _name,
                _kind,
                _lockMode,
                a,
                b,
                c,
                _visible,
                _group,
                _displayColor,
                _notes);
        }

        private static MeasurementPoint RestorePoint(
            MeasurementPointState pointState,
            ShipConstruct ship,
            MeasurementLockMode lockMode,
            ref int unresolvedPointCount)
        {
            if (pointState == null)
                return null;

            MeasurementPoint point = pointState.Restore(ship, lockMode);
            if (point == null)
                unresolvedPointCount++;
            return point;
        }
    }

    internal sealed class MeasurementPointState
    {
        private uint _craftId;
        private Vector3 _partLocalPosition;
        private string _snapNodeId;
        private uint _vesselReferenceCraftId;
        private Vector3 _vesselLocalPosition;
        private bool _hasVesselLocalPosition;

        public static MeasurementPointState Capture(MeasurementPoint point)
        {
            if (point == null)
                return null;

            MeasurementPointState state = new MeasurementPointState();
            state._craftId = point.CraftId;
            state._partLocalPosition = point.PartLocalPosition;
            state._snapNodeId = point.SnapNodeId;
            state._vesselReferenceCraftId = point.VesselReferenceCraftId;
            state._vesselLocalPosition = point.VesselLocalPosition;
            state._hasVesselLocalPosition = point.HasVesselLocalPosition;
            return state;
        }

        public MeasurementPoint Restore(ShipConstruct ship, MeasurementLockMode lockMode)
        {
            return MeasurementPoint.Resolve(
                ship,
                _craftId,
                _partLocalPosition,
                _snapNodeId,
                _vesselReferenceCraftId,
                _vesselLocalPosition,
                _hasVesselLocalPosition,
                lockMode);
        }
    }
}
