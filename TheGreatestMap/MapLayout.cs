using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// A measuring tape for the large map's own layout, for tgm_mapui. Whether the map can be made
    /// bigger by growing a rect or only by scaling it turns on how its parts are anchored inside
    /// m_largeRoot, and that lives in the game's prefab rather than in any code, so the only honest
    /// way to find out is to ask the running game.
    ///
    /// What matters in the answer:
    ///  * whether m_largeRoot fills the screen, and how much is going spare;
    ///  * whether the map image and the pin root stretch to their parent. If they both do, growing
    ///    a parent rect grows them together and every pin still lands where it belongs, because
    ///    pins are placed from the image's rect. If they do not, only scaling is safe.
    /// </summary>
    internal static class MapLayout
    {
        internal static List<string> Describe()
        {
            var lines = new List<string>();
            var map = Minimap.instance;
            if (map == null) { lines.Add("no minimap"); return lines; }
            lines.Add($"screen: {Screen.width}x{Screen.height}, map mode: {map.m_mode}");
            if (map.m_largeRoot == null) { lines.Add("no large root"); return lines; }
            if (!map.m_largeRoot.activeSelf)
                lines.Add("NOTE: the large map is not open, so its layout may not be laid out yet. Open the map and run this again.");

            var root = map.m_largeRoot.transform as RectTransform;
            if (root == null) { lines.Add("the large root is not a RectTransform"); return lines; }
            lines.Add("root " + Describe(root, Screen.width, Screen.height));
            lines.Add("  parent chain: " + Chain(root));

            var image = map.m_mapImageLarge != null ? map.m_mapImageLarge.rectTransform : null;
            if (image != null) lines.Add("map image " + Describe(image, Screen.width, Screen.height) + " parent: " + Name(image.parent));
            var pinRoot = map.m_pinRootLarge;
            if (pinRoot != null) lines.Add("pin root " + Describe(pinRoot, Screen.width, Screen.height) + " parent: " + Name(pinRoot.parent));
            var nameRoot = map.m_pinNameRootLarge;
            if (nameRoot != null) lines.Add("pin name root " + Describe(nameRoot, Screen.width, Screen.height) + " parent: " + Name(nameRoot.parent));

            if (image != null && pinRoot != null)
            {
                bool same = SameRect(image, pinRoot);
                lines.Add(same
                    ? "the pin root covers the same ground as the map image: growing a shared parent should carry pins with it"
                    : "the pin root does NOT match the map image: growing a rect would move the map out from under the pins, so scale instead");
            }

            lines.Add("children of the root:");
            foreach (Transform child in root)
            {
                var rt = child as RectTransform;
                lines.Add(rt != null
                    ? "  " + child.name + " " + Describe(rt, Screen.width, Screen.height) + (child.gameObject.activeSelf ? "" : " (inactive)")
                    : "  " + child.name + " (not a RectTransform)");
            }
            return lines;
        }

        private static string Describe(RectTransform rt, int screenW, int screenH)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float w = Mathf.Abs(corners[2].x - corners[0].x);
            float h = Mathf.Abs(corners[2].y - corners[0].y);
            string stretch = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one ? " STRETCHED to parent" : "";
            return $"rect {rt.rect.width:0}x{rt.rect.height:0}, on screen {w:0}x{h:0} " +
                   $"({100f * w / Mathf.Max(1, screenW):0}% x {100f * h / Mathf.Max(1, screenH):0}% of screen), " +
                   $"anchors {rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##}..{rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}, " +
                   $"pivot {rt.pivot.x:0.##},{rt.pivot.y:0.##}, pos {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}, " +
                   $"sizeDelta {rt.sizeDelta.x:0},{rt.sizeDelta.y:0}, scale {rt.localScale.x:0.###}{stretch}";
        }

        private static bool SameRect(RectTransform a, RectTransform b)
        {
            var ca = new Vector3[4];
            var cb = new Vector3[4];
            a.GetWorldCorners(ca);
            b.GetWorldCorners(cb);
            for (int i = 0; i < 4; i++)
                if (Vector3.Distance(ca[i], cb[i]) > 1f) return false;
            return true;
        }

        private static string Chain(Transform t)
        {
            var parts = new List<string>();
            for (var p = t.parent; p != null; p = p.parent) parts.Add(p.name);
            return parts.Count == 0 ? "(none)" : string.Join(" < ", parts.ToArray());
        }

        private static string Name(Transform t) => t == null ? "(none)" : t.name;
    }
}
