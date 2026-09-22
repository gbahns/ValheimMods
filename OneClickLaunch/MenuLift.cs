using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OneClickLaunch
{
    /// <summary>
    /// The menu list stacks its buttons downward from a fixed top, so every button a mod adds
    /// (ours, ServerConnect's, anyone's) pushes Quit further down until it leaves the screen.
    /// This measures where the lowest button really ends up and moves the list so it stays a
    /// margin above the bottom of the screen, and moves it back down when buttons go away.
    ///
    /// The measurement is deferred: on the frame the menu is built, layout has not run and the
    /// slide-in animation has not started, and a measurement taken then said the menu fit when
    /// Quit was plainly off the screen. So the check runs after the first frame and again a few
    /// times over the following seconds.
    /// </summary>
    internal static class MenuLift
    {
        private static readonly float[] Rechecks = { 0.5f, 1.5f, 3f };

        // The list this menu instance has, where vanilla put it, and how far it has been moved.
        private static RectTransform s_list;
        private static Vector2 s_basePosition;
        private static float s_applied;

        internal static void Schedule(FejdStartup fs)
        {
            if (fs == null || fs.m_menuList == null) return;
            var list = fs.m_menuList.transform as RectTransform;
            if (list != s_list)
            {
                s_list = list;
                s_basePosition = list != null ? list.anchoredPosition : Vector2.zero;
                s_applied = 0f;
            }
            fs.StartCoroutine(Run(fs, "first frame"));
        }

        /// <summary>
        /// After the buttons changed: measure again from the next frame on, once the buttons a
        /// rebuild destroyed are really gone, and keep checking as usual.
        /// </summary>
        internal static void Reapply(FejdStartup fs)
        {
            if (fs == null || fs.m_menuList == null) return;
            fs.StartCoroutine(Run(fs, "rebuild"));
        }

        private static IEnumerator Run(FejdStartup fs, string when)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (fs == null || fs.m_menuList == null) yield break;
            Apply(fs, when);
            foreach (float wait in Rechecks)
            {
                yield return new WaitForSecondsRealtime(wait);
                if (fs == null || fs.m_menuList == null) yield break;
                Apply(fs, $"{wait}s later");
            }
        }

        private static RectTransform Root(RectTransform list)
        {
            Canvas canvas = list.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        }

        /// <summary>Bottom edge of the lowest active button in the list, in the root canvas's units.</summary>
        private static float Lowest(FejdStartup fs, RectTransform root)
        {
            var corners = new Vector3[4];
            float lowest = float.MaxValue;
            foreach (Button button in fs.m_menuList.GetComponentsInChildren<Button>(false))
            {
                var rect = button.transform as RectTransform;
                if (rect == null) continue;
                rect.GetWorldCorners(corners);
                lowest = Mathf.Min(lowest, root.InverseTransformPoint(corners[0]).y);
            }
            return lowest;
        }

        private static void Apply(FejdStartup fs, string when)
        {
            int margin = OneClickLaunchMod.MenuBottomMargin.Value;
            RectTransform list = fs.m_menuList.transform as RectTransform;
            RectTransform root = list != null ? Root(list) : null;
            if (root == null || list != s_list) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            float lowest = Lowest(fs, root);
            if (lowest == float.MaxValue) return;

            // Where the lowest button would sit with no lift at all, and what it needs from there.
            float unlifted = lowest - s_applied;
            float floor = root.rect.yMin + margin;
            float needed = margin < 0 ? 0f : Mathf.Max(0f, floor - unlifted);
            if (Mathf.Abs(needed - s_applied) <= 0.5f) return;

            s_applied = needed;
            list.anchoredPosition = s_basePosition + new Vector2(0f, s_applied);
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            OneClickLaunchMod.Log.LogInfo($"Menu lift ({when}): lowest button was at {lowest:0}, floor {floor:0}; lift is now {s_applied:0}, button at {Lowest(fs, root):0}.");
        }

    }
}
