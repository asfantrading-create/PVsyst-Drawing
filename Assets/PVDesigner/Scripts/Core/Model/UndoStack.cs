using System.Collections.Generic;
using PVDesigner.Core.Serialization;

namespace PVDesigner.Core.Model
{
    /// <summary>Snapshot based undo / redo (meteo data is shared and not part of the snapshots).</summary>
    public class UndoStack
    {
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        public int Capacity = 60;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Call before modifying the project.</summary>
        public void Record(PVProject current)
        {
            _undo.Add(ProjectSerializer.Serialize(current, includeMeteo: false, indented: false));
            if (_undo.Count > Capacity) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public PVProject Undo(PVProject current) => Swap(current, _undo, _redo);
        public PVProject Redo(PVProject current) => Swap(current, _redo, _undo);

        private static PVProject Swap(PVProject current, List<string> from, List<string> to)
        {
            if (from.Count == 0) return current;
            to.Add(ProjectSerializer.Serialize(current, includeMeteo: false, indented: false));
            string json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            var p = ProjectSerializer.Deserialize(json);
            p.Meteo = current.Meteo;
            return p;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
