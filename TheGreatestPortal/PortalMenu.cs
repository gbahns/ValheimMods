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
    }

    /// <summary>
    /// Renaming a portal from a row of a list: a text box swapped in over the name, in place. One
    /// of these belongs to each list. The server decides whether the rename is allowed and says so
    /// itself, so there is nothing to check here beyond the name being different.
    /// </summary>
    internal sealed class RowRename
    {
        private TMP_InputField _field;
        private Transform _home;            // where the box lives when it is not over a row
        private UiKit.RowHandle _row;
        private long _id;
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

        internal void Begin(PortalInfo p, UiKit.RowHandle row)
        {
            if (_field == null || p == null || row == null || row.Root == null) return;
            Cancel();
            _active = true;
            _id = p.Id;
            _row = row;
            if (row.Label != null) row.Label.gameObject.SetActive(false);
            var rt = _field.GetComponent<RectTransform>();
            rt.SetParent(row.Root.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(6f, 2f);
            rt.offsetMax = new Vector2(-row.RightInset, -2f);   // clear of the distance and the icons
            _field.gameObject.SetActive(true);
            _field.characterLimit = TgpConfig.MaxNameLength.Value;
            _field.text = p.Name ?? "";
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
            long id = _id;
            string name = PortalData.CleanName(text, TgpConfig.MaxNameLength.Value);
            var p = Catalog.Get(id);
            string old = p != null ? p.Name : "";
            End();
            if (p == null || name == old) return;
            PortalNetwork.SendRename(id, name);
            p.Name = name;   // shown at once; the server's next portal list confirms it
            TheGreatestPortalMod.Message((string.IsNullOrEmpty(old) ? "Portal" : old) + " renamed to " + (string.IsNullOrEmpty(name) ? "(no name)" : name));
            Refresh?.Invoke();
            Renamed?.Invoke(p, name);
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
