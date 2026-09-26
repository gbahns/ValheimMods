using System.Collections.Generic;
using UnityEngine;

namespace Comfortometer
{
    internal enum RowState
    {
        /// <summary>In range and counting towards the level.</summary>
        Counted,
        /// <summary>In range but a better piece of the same group is too, so it gives nothing.</summary>
        Superseded,
        /// <summary>In range but switched off: an unlit fire gives no comfort.</summary>
        Inactive,
        /// <summary>Was in range a moment ago and is not any more.</summary>
        Lost,
        /// <summary>Would count, but the player is not under a roof, so nothing counts.</summary>
        NoShelter,
    }

    /// <summary>One line of the panel: every piece of one name, folded together.</summary>
    internal sealed class ComfortRow
    {
        public string Name;
        public string Group;
        public int Comfort;
        public int InRange;
        /// <summary>At least one copy in range is switched on (a lit fire; anything else is always on).</summary>
        public bool Active;
        public float Distance;
        public bool Fireplace;
        public RowState State;
    }

    internal sealed class ComfortReport
    {
        public int Level;
        public bool Sheltered;
        public float RestedSeconds;
        public readonly List<ComfortRow> Rows = new List<ComfortRow>();
    }

    /// <summary>
    /// Builds the panel's picture of the comfort around the player, the same way SE_Rested
    /// builds the number: pieces within 10 m, sorted by group, best comfort first; within a group
    /// only the first counts, and two pieces of the same name count once. On top of that it
    /// remembers every piece that has been in range, so one you walk away from stays listed as
    /// lost until you are well clear of it.
    /// </summary>
    internal static class ComfortScan
    {
        /// <summary>SE_Rested's comfort radius. A constant in the game, private, so repeated here.</summary>
        internal const float Radius = 10f;

        private sealed class Known
        {
            public Piece Piece;
            public Vector3 Position;
            public string NameToken;
            public Piece.ComfortGroup Group;
            public int Comfort;         // the piece's full comfort, whether or not it is active now
            public bool Fireplace;
        }

        private static readonly List<Known> _known = new List<Known>();
        private static readonly List<Piece> _nearby = new List<Piece>();
        private static readonly Dictionary<string, ComfortRow> _byName = new Dictionary<string, ComfortRow>();
        private static readonly Dictionary<string, int> _maxActive = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _maxFull = new Dictionary<string, int>();
        private static readonly HashSet<string> _countedGroups = new HashSet<string>();

        internal static void Forget() => _known.Clear();

        internal static ComfortReport Scan(Player player)
        {
            var report = new ComfortReport();
            if (player == null) return report;
            Vector3 pos = player.transform.position;
            float forget = Mathf.Max(Radius, ComfortometerMod.ForgetBeyond.Value);

            report.Sheltered = player.InShelter();
            report.Level = SE_Rested.CalculateComfortLevel(player);
            report.RestedSeconds = RestedSeconds(report.Level);

            // Every comfort piece in range right now joins the memory; the rest of the memory ages.
            _nearby.Clear();
            Piece.GetAllComfortPiecesInRadius(pos, Radius, _nearby);
            foreach (var piece in _nearby)
            {
                if (piece == null) continue;
                var known = Find(piece);
                if (known == null)
                {
                    known = new Known
                    {
                        Piece = piece,
                        NameToken = piece.m_name ?? "",
                        Group = piece.m_comfortGroup,
                        Fireplace = piece.GetComponent<Fireplace>() != null,
                    };
                    _known.Add(known);
                }
                known.Position = piece.transform.position;
                known.Comfort = piece.m_comfort;
            }
            for (int i = _known.Count - 1; i >= 0; i--)
            {
                var k = _known[i];
                if (k.Piece != null) k.Position = k.Piece.transform.position;
                if (Vector3.Distance(pos, k.Position) >= forget) _known.RemoveAt(i);
            }

            // Fold the memory into one row per name, the way the game counts a name once.
            _byName.Clear();
            _maxActive.Clear();
            _maxFull.Clear();
            foreach (var k in _known)
            {
                float d = Vector3.Distance(pos, k.Position);
                bool inRange = k.Piece != null && d < Radius;
                int active = inRange ? k.Piece.GetComfort() : 0;

                if (!_byName.TryGetValue(k.NameToken, out var row))
                {
                    row = new ComfortRow
                    {
                        Name = Localization.instance != null ? Localization.instance.Localize(k.NameToken) : k.NameToken,
                        Group = k.Group == Piece.ComfortGroup.None ? "" : k.Group.ToString().ToLowerInvariant(),
                        Distance = d,
                        Fireplace = k.Fireplace,
                    };
                    _byName[k.NameToken] = row;
                    _maxActive[k.NameToken] = 0;
                    _maxFull[k.NameToken] = 0;
                }
                if (d < row.Distance) row.Distance = d;
                if (inRange) row.InRange++;
                if (active > _maxActive[k.NameToken]) _maxActive[k.NameToken] = active;
                if (k.Comfort > _maxFull[k.NameToken]) _maxFull[k.NameToken] = k.Comfort;
            }
            foreach (var pair in _byName)
            {
                var row = pair.Value;
                int active = _maxActive[pair.Key];
                row.Active = active > 0;
                if (active > 0) { row.State = RowState.Counted; row.Comfort = active; }
                else
                {
                    row.State = row.InRange > 0 ? RowState.Inactive : RowState.Lost;
                    row.Comfort = _maxFull[pair.Key];
                }
            }

            // Within a group only the best counts. The game sorts by comfort, then by name
            // descending, and takes the first; the rest are superseded.
            var rows = new List<ComfortRow>(_byName.Values);
            rows.Sort(VanillaOrder);
            ComfortRow previous = null;
            _countedGroups.Clear();
            foreach (var row in rows)
            {
                if (row.State != RowState.Counted) { continue; }
                if (previous != null && row.Group.Length > 0 && row.Group == previous.Group)
                {
                    row.State = RowState.Superseded;
                    continue;
                }
                previous = row;
                if (row.Group.Length > 0) _countedGroups.Add(row.Group);
                if (!report.Sheltered) row.State = RowState.NoShelter;
            }
            // Something out of range, or unlit, whose group is covered by a counting piece was
            // never giving anything: a stool you walk away from while the chair still counts is
            // not a loss. It is dimmed if still in range and dropped once it is not.
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                var row = rows[i];
                if (row.Group.Length == 0 || !_countedGroups.Contains(row.Group)) continue;
                if (row.State == RowState.Inactive) row.State = RowState.Superseded;
                else if (row.State == RowState.Lost) rows.RemoveAt(i);
            }

            rows.Sort(DisplayOrder);
            foreach (var row in rows)
            {
                if (row.State == RowState.Superseded && !ComfortometerMod.ShowSuperseded.Value) continue;
                report.Rows.Add(row);
            }
            return report;
        }

        private static Known Find(Piece piece)
        {
            foreach (var k in _known)
                if (k.Piece == piece) return k;
            return null;
        }

        /// <summary>SE_Rested.PieceComfortSort, on rows: group, then comfort descending, then name descending.</summary>
        private static int VanillaOrder(ComfortRow x, ComfortRow y)
        {
            int g = string.CompareOrdinal(x.Group, y.Group);
            if (g != 0) return g;
            if (x.Comfort != y.Comfort) return y.Comfort.CompareTo(x.Comfort);
            return string.CompareOrdinal(y.Name, x.Name);
        }

        /// <summary>What counts first, then the dimmed extras, then what is missing, nearest first.</summary>
        private static int DisplayOrder(ComfortRow x, ComfortRow y)
        {
            int rx = Rank(x.State), ry = Rank(y.State);
            if (rx != ry) return rx.CompareTo(ry);
            if (rx >= 2) return x.Distance.CompareTo(y.Distance);
            return VanillaOrder(x, y);
        }

        private static int Rank(RowState s)
        {
            switch (s)
            {
                case RowState.Counted: case RowState.NoShelter: return 0;
                case RowState.Superseded: return 1;
                case RowState.Inactive: return 2;
                default: return 3;
            }
        }

        private static float _baseTtl = -1f, _ttlPerLevel;

        /// <summary>How long Rested lasts at a comfort level, from the game's own SE_Rested asset.</summary>
        private static float RestedSeconds(int level)
        {
            if (_baseTtl < 0f)
            {
                var se = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested : null;
                if (se == null) return 300f + (level - 1) * 60f;   // the shipped values, if the asset is not there yet
                _baseTtl = se.m_baseTTL;
                _ttlPerLevel = se.m_TTLPerComfortLevel;
            }
            return _baseTtl + (level - 1) * _ttlPerLevel;
        }
    }
}
