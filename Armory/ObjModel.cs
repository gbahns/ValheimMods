using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Reads a Wavefront OBJ embedded in the DLL into polygons RackVisual can texture and
    /// triangulate.  Only positions, faces, object names and material names are used: the
    /// textures are the game's and are laid on at runtime, so the file's UVs and normals are
    /// ignored (the exporter is told not to write them).
    ///
    /// OBJ is right-handed and Unity left-handed, so X is mirrored and every face's vertex
    /// order reversed, which keeps the outside facing out.
    /// </summary>
    internal static class ObjModel
    {
        internal sealed class Polygon
        {
            public string    Object;
            public int       Material;   // RackVisual's submesh: 0 body, 1 panel, 2 iron
            public Vector3[] Points;
            public bool      IsDoor;     // the object's name starts with "Door"
            public int       Side;       // -1 or +1 by the object's mean X, for a door's hinge
            public float     Shift;      // a per-object place along the grain
        }

        internal static List<Polygon> LoadEmbedded(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            string res = null;
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) res = n;
            if (res == null)
            {
                Jotunn.Logger.LogWarning($"[Armory] Embedded model '{fileName}' not found.");
                return null;
            }
            try
            {
                using (var stream = asm.GetManifestResourceStream(res))
                using (var reader = new StreamReader(stream))
                {
                    var polys = Parse(reader);
                    Jotunn.Logger.LogInfo($"[Armory] Model '{fileName}': {polys.Count} faces.");
                    return polys;
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Armory] Model '{fileName}' failed to load: {e.Message}");
                return null;
            }
        }

        internal static List<Polygon> Parse(TextReader reader)
        {
            var verts   = new List<Vector3>();
            var polys   = new List<Polygon>();
            var current = new List<Polygon>();   // the object being read
            string objectName = "";
            int material = 0;

            void FinishObject()
            {
                if (current.Count == 0) return;
                float meanX = 0f; int n = 0;
                foreach (var p in current) foreach (var q in p.Points) { meanX += q.x; n++; }
                int side = meanX / Mathf.Max(1, n) < 0f ? -1 : 1;
                bool door = objectName.StartsWith("Door", StringComparison.OrdinalIgnoreCase);
                float shift = Mathf.Repeat(StableHash(objectName) * 0.618034f, 1f);
                foreach (var p in current) { p.IsDoor = door; p.Side = side; p.Shift = shift; }
                current.Clear();
            }

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v":
                        verts.Add(new Vector3(-F(parts[1]), F(parts[2]), F(parts[3])));   // mirrored X
                        break;
                    case "o":
                    case "g":
                        FinishObject();
                        objectName = parts.Length > 1 ? parts[1] : "";
                        break;
                    case "usemtl":
                        material = MaterialIndex(parts.Length > 1 ? parts[1] : "");
                        break;
                    case "f":
                        var pts = new Vector3[parts.Length - 1];
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string tok = parts[i];
                            int slash = tok.IndexOf('/');
                            if (slash >= 0) tok = tok.Substring(0, slash);
                            int idx = int.Parse(tok, CultureInfo.InvariantCulture);
                            if (idx < 0) idx = verts.Count + idx + 1;
                            pts[parts.Length - 1 - i] = verts[idx - 1];   // reversed: see the class note
                        }
                        var poly = new Polygon { Object = objectName, Material = material, Points = pts };
                        polys.Add(poly);
                        current.Add(poly);
                        break;
                }
            }
            FinishObject();
            return polys;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static int MaterialIndex(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "panel": return 1;
                case "iron":  return 2;
                default:      return 0;
            }
        }

        // The same number every run, which string.GetHashCode does not promise.
        private static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h & 0x7fffffff;
            }
        }
    }
}
