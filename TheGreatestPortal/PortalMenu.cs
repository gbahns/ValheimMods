using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// What the two destination lists both offer for a portal, and the renaming box they both use.
    ///
    /// The panel's list and the map's list are not the same menu: each opens with the act its own
    /// list is for -- travelling out of the portal you are standing in, choosing a destination,
    /// moving the map -- and ends with a way to see where the portal is, which means opening the
    /// map in one and panning it in the other. Between those two sit the items that are the same
    /// wherever they are used, and those live here rather than in both.
    /// </summary>
    internal static class PortalMenu
    {
        /// <summary>Stars or unstars a portal and says so. <paramref name="refresh"/> redraws the list it was on.</summary>
        internal static void ToggleFavorite(PortalInfo p, Action refresh)
        {
            if (p == null || p.Id == 0L)
            {
                TheGreatestPortalMod.Message("That portal has no id yet; try again in a moment", always: true);
                return;
            }
            bool on = Favorites.Toggle(p.Id);
            TheGreatestPortalMod.Message(on ? "Favorite: " + p.DisplayName : "No longer a favorite: " + p.DisplayName);
            refresh?.Invoke();
        }

        internal static KeyValuePair<string, Action> FavoriteItem(PortalInfo p, Action refresh)
        {
            return new KeyValuePair<string, Action>(Favorites.IsFavorite(p.Id) ? "Un-favorite" : "Favorite",
                                                    () => ToggleFavorite(p, refresh));
        }

        internal static KeyValuePair<string, Action> RenameItem(PortalInfo p, RowRename rename, UiKit.RowHandle row)
        {
            return new KeyValuePair<string, Action>("Rename", () => rename.Begin(p, row));
        }

        /// <summary>
        /// Making a portal private, or public again. Only offered for your own portals: the owner
        /// is the character that built it, and the server refuses anybody else.
        /// </summary>
        internal static void AddPrivateItem(List<KeyValuePair<string, Action>> items, PortalInfo p, Action refresh)
        {
            if (items == null || p == null || p.Id == 0L || !p.Mine || !PortalData.PrivateAllowed) return;
            long id = p.Id;
            bool on = p.Private;
            items.Add(new KeyValuePair<string, Action>(on ? "Make public" : "Make private", () =>
            {
                PortalNetwork.SendPrivate(id, !on);
                p.Private = !on;    // shown at once; the server's next portal list confirms it
                TheGreatestPortalMod.Message(on
                    ? p.DisplayName + " is public again"
                    : p.DisplayName + " is private: only you can see or use it");
                refresh?.Invoke();
            }));
        }

        /// <summary>
        /// Filing a portal under one of this player's own categories, from the row's menu.
        ///
        /// Every category in use is offered by name, so putting a portal in one is a single click
        /// and there is no list of categories to go and manage first. "New category" is the only
        /// way one comes into being, and a portal leaving the last one takes it away again.
        /// </summary>
        internal static void AddCategoryItems(List<KeyValuePair<string, Action>> items, PortalInfo p,
                                              RowRename rename, UiKit.RowHandle row, Action refresh)
        {
            if (items == null || p == null || p.Id == 0L) return;
            long id = p.Id;
            string mine = Favorites.CategoryOf(id);
            foreach (string name in Favorites.CategoryNames())
            {
                if (string.Equals(name, mine, StringComparison.OrdinalIgnoreCase)) continue;
                string to = name;
                items.Add(new KeyValuePair<string, Action>((mine == null ? "Add to " : "Move to ") + to, () =>
                {
                    Favorites.SetCategory(id, to);
                    TheGreatestPortalMod.Message(p.DisplayName + " is in " + to);
                    refresh?.Invoke();
                }));
            }
            if (rename != null && rename.Ready && row != null)
                items.Add(new KeyValuePair<string, Action>("New category", () => rename.BeginCategory(p, row)));
            if (!string.IsNullOrEmpty(mine))
                items.Add(new KeyValuePair<string, Action>("Take out of " + mine, () =>
                {
                    Favorites.SetCategory(id, null);
                    TheGreatestPortalMod.Message(p.DisplayName + " is out of " + mine);
                    refresh?.Invoke();
                }));
        }
    }

    /// <summary>
    /// Typing on a row of a list: a text box swapped in over the name, in place. One of these
    /// belongs to each list, and it is used for the two things a row can be asked to name -- the
    /// portal itself, and the category it is filed under. Which of them is being typed is the
    /// commit handler the box was opened with; everything about the box is the same either way.
    ///
    /// For a rename the server decides whether it is allowed and says so itself, so there is
    /// nothing to check here beyond the name being different.
    /// </summary>
    internal sealed class RowRename
    {
        private TMP_InputField _field;
        private Transform _home;            // where the box lives when it is not over a row
        private UiKit.RowHandle _row;
        private long _id;
        private Action<string> _onCommit;
        private bool _active;
        private int _doneFrame = -10;

        /// <summary>Redraws the list after a name changes.</summary>
        internal Action Refresh;

        /// <summary>Called with the new name once it has been sent, for whatever the list makes of it.</summary>
        internal Action<PortalInfo, string> Renamed;

        internal bool Active => _active;
        internal bool Ready => _field != null;

        /// <summary>True for a frame after a rename ended, so the key that ended it is not read twice.</summary>
        internal bool JustEnded => Time.frameCount - _doneFrame <= 1;

        internal void Build(Transform parent)
        {
            _home = parent;
            _field = UiKit.CloneInputField(parent, "Rename", "", TgpConfig.MaxNameLength.Value, null, Commit);
            if (_field == null) return;
            _field.onDeselect.AddListener(_ => Cancel());   // clicking away cancels
            _field.gameObject.SetActive(false);
        }

        /// <summary>Renaming the portal on this row.</summary>
        internal void Begin(PortalInfo p, UiKit.RowHandle row)
        {
            BeginEdit(p, row, p != null ? p.Name ?? "" : "", TgpConfig.MaxNameLength.Value, CommitRename);
        }

        /// <summary>Naming the category this portal goes in. An empty name takes it out of the one
        /// it is in, which is also how a category with nothing left in it disappears.</summary>
        internal void BeginCategory(PortalInfo p, UiKit.RowHandle row)
        {
            if (p == null) return;
            BeginEdit(p, row, Favorites.CategoryOf(p.Id) ?? "", Favorites.MaxCategoryLength, CommitCategory);
            if (!_active) return;
            TheGreatestPortalMod.Message("Type a category for " + p.DisplayName + ", or nothing to take it out of one", always: true);
        }

        private void BeginEdit(PortalInfo p, UiKit.RowHandle row, string initial, int limit, Action<string> commit)
        {
            if (_field == null || p == null || row == null || row.Root == null) return;
            Cancel();
            _active = true;
            _id = p.Id;
            _row = row;
            _onCommit = commit;
            if (row.Label != null) row.Label.gameObject.SetActive(false);
            var rt = _field.GetComponent<RectTransform>();
            rt.SetParent(row.Root.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(6f, 2f);
            rt.offsetMax = new Vector2(-row.RightInset, -2f);   // clear of the distance and the icons
            _field.gameObject.SetActive(true);
            _field.characterLimit = limit;
            _field.text = initial ?? "";
            _field.ActivateInputField();
        }

        internal void Cancel()
        {
            if (_active) End();
        }

        /// <summary>Puts the box away for good, as the list it sits in is torn down.</summary>
        internal void Clear()
        {
            _active = false;
            _row = null;
            _field = null;
            _home = null;
        }

        private void Commit(string text)
        {
            if (!_active) return;
            var handler = _onCommit;
            End();          // leaves _id alone: the handler below is the only thing that reads it
            handler?.Invoke(text);
        }

        private void CommitRename(string text)
        {
            long id = _id;
            string name = PortalData.CleanName(text, TgpConfig.MaxNameLength.Value);
            var p = Catalog.Get(id);
            string old = p != null ? p.Name : "";
            if (p == null || name == old) return;
            PortalNetwork.SendRename(id, name);
            p.Name = name;   // shown at once; the server's next portal list confirms it
            TheGreatestPortalMod.Message((string.IsNullOrEmpty(old) ? "Portal" : old) + " renamed to " + (string.IsNullOrEmpty(name) ? "(no name)" : name));
            Refresh?.Invoke();
            Renamed?.Invoke(p, name);
        }

        private void CommitCategory(string text)
        {
            long id = _id;
            var p = Catalog.Get(id);
            if (p == null) return;
            string name = Favorites.CleanCategory(text);
            string old = Favorites.CategoryOf(id);
            if (string.Equals(name, old, StringComparison.OrdinalIgnoreCase)) return;
            Favorites.SetCategory(id, name);
            TheGreatestPortalMod.Message(string.IsNullOrEmpty(name)
                ? p.DisplayName + " is out of " + old
                : p.DisplayName + " is in " + name);
            Refresh?.Invoke();
        }

        private void End()
        {
            _active = false;
            _doneFrame = Time.frameCount;
            if (_row != null && _row.Label != null) _row.Label.gameObject.SetActive(true);
            _row = null;
            if (_field == null) return;
            _field.DeactivateInputField();
            _field.gameObject.SetActive(false);
            if (_home != null) _field.transform.SetParent(_home, false);
        }
    }
}
