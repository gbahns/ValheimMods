using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// What is actually loaded around this machine, counted by prefab.
    ///
    /// The capture has said "nearby_pieces 5,995" for a long time, and that number turned out to
    /// be the easiest thing in the file to misread. On 2026-11-16 it was taken as the size of a
    /// base the group had built in the Ashlands; they had built almost nothing there, and the
    /// count was the biome's own fortress ruins. The conclusion about cost was right either way -
    /// a piece costs the same whoever placed it - but the attribution was invented, and the file
    /// gave no way to check it.
    ///
    /// A count cannot be questioned. A list can: "4,100 blackmarble, 900 charredstone" reads as
    /// ruins at a glance, and "3,000 wood_floor" reads as somebody's floor. One pass over the
    /// instance table answers it, which is the same pass the sampler already makes every second.
    /// </summary>
    internal static class Nearby
    {
        private static System.Reflection.FieldInfo _instancesField;
        private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        /// <summary>Prefab name for a hash, cached - GetPrefab is a dictionary lookup but the string is not.</summary>
        private static string NameOf(int hash)
        {
            if (_names.TryGetValue(hash, out string known)) return known;
            string name = null;
            try
            {
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
                if (prefab != null) name = prefab.name;
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = "unknown:" + hash.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _names[hash] = name;
            return name;
        }

        private sealed class Tally
        {
            internal int Count;
            internal int Owned;
            internal bool Piece;
        }

        /// <summary>
        /// The loaded instance table, grouped by prefab, commonest first.
        ///
        /// Pieces are listed separately from everything else because they are the ones that answer
        /// "why is it slow here" - trees and rocks load in their thousands in any forest and are
        /// not what distinguishes a dense place from an empty one.
        /// </summary>
        internal static string Describe(int top)
        {
            var scene = ZNetScene.instance;
            if (scene == null) return "Not in a world.";
            if (_instancesField == null)
            {
                _instancesField = AccessTools.Field(typeof(ZNetScene), "m_instances");
                if (_instancesField == null) return "Could not read the instance table.";
            }
            var instances = _instancesField.GetValue(scene) as Dictionary<ZDO, ZNetView>;
            if (instances == null) return "Could not read the instance table.";

            var byPrefab = new Dictionary<int, Tally>();
            int total = 0, pieces = 0, owned = 0;
            foreach (var kv in instances)
            {
                var zdo = kv.Key;
                if (zdo == null) continue;
                int hash = zdo.GetPrefab();
                if (!byPrefab.TryGetValue(hash, out var t))
                {
                    t = new Tally { Piece = Sampler.IsPieceHash(hash) };
                    byPrefab[hash] = t;
                }
                t.Count++;
                total++;
                if (t.Piece) pieces++;
                bool mine = false;
                try { mine = zdo.IsOwner(); } catch { }
                if (mine) { t.Owned++; owned++; }
            }

            var all = new List<KeyValuePair<int, Tally>>(byPrefab);
            all.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            var sb = new StringBuilder();
            string where = "";
            try
            {
                var who = Player.m_localPlayer;
                if (who != null)
                {
                    var p = who.transform.position;
                    where = $" in the {CaptureCsv.BiomeName((int)who.GetCurrentBiome())} " +
                            $"at ({p.x:0}, {p.z:0})";
                }
            }
            catch { }

            sb.AppendLine($"{total} object(s) loaded{where}: {pieces} piece(s), {total - pieces} other, " +
                          $"{owned} owned by this machine, across {byPrefab.Count} prefab(s).");
            sb.AppendLine();

            AppendGroup(sb, all, true, top, "PIECES - built and world-placed alike; the engine does not tell them apart");
            sb.AppendLine();
            AppendGroup(sb, all, false, top, "EVERYTHING ELSE");
            return sb.ToString().TrimEnd();
        }

        private static void AppendGroup(StringBuilder sb, List<KeyValuePair<int, Tally>> all,
                                        bool wantPiece, int top, string heading)
        {
            sb.AppendLine(heading);
            int shown = 0, rest = 0, restKinds = 0;
            foreach (var kv in all)
            {
                if (kv.Value.Piece != wantPiece) continue;
                if (shown < top)
                {
                    sb.AppendLine($"  {kv.Value.Count,6}  {NameOf(kv.Key),-34}" +
                                  (kv.Value.Owned > 0 ? $" ({kv.Value.Owned} owned here)" : ""));
                    shown++;
                }
                else { rest += kv.Value.Count; restKinds++; }
            }
            if (shown == 0) sb.AppendLine("  none");
            if (restKinds > 0) sb.AppendLine($"  {rest,6}  in {restKinds} further kind(s)");
        }
    }
}
