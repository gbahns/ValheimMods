using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace GrabMaterials
{
	// A round badge floating over each nearby container with its number of free slots, so
	// you can find a chest with room without opening them one by one (#45).
	//
	// The badges are ordinary screen-space UI on Jotunn's back canvas, placed every frame by
	// projecting a point just above each container into the screen.  That keeps them behind
	// every panel, lets them scale with distance, and costs nothing per container beyond a
	// matrix multiply.  Which containers get a badge is decided five times a second: in
	// range, in front of the camera, and (by default) not hidden behind a wall, which one
	// linecast per candidate answers.
	//
	// The "Container Badges" key and `/badges` step the "Display" setting through off, badges,
	// and badges with item icons; which of those the key visits is the "Toggle Cycle" setting.
	// The state is written back to the setting so it persists.
	public static class ContainerBadges
	{
		public enum Display
		{
			Off,
			Badges,           // the free-slot disc only
			BadgesAndIcons,   // the disc plus the icons of what the container mostly holds
		}

		// Which Display states the toggle key walks through.
		public enum Cycle
		{
			ThreeWay,              // off, badges, badges and icons
			OffOrBadges,           // off, badges
			OffOrBadgesAndIcons,   // off, badges and icons
		}

		public enum IconSide
		{
			Right,      // a row out to the right of the disc
			Down,       // a column under the disc
			Left,
			Up,
			BelowRow,   // a row centered under the disc: disc and two icons make a triangle
			AboveRow,
		}

		private const float ScanInterval = 0.2f;
		private const float RingWidth = 2f;
		private const float AboveTop = 0.15f;   // meters above the container's collider
		private const float IconGap = 3f;
		private const int MaxIcons = 2;

		private sealed class Badge
		{
			public GameObject Root;
			public RectTransform Rect;
			public Image Ring;
			public Image Fill;
			public Text Label;
			public Image[] Icons;   // up to two, beside the disc: what the container mostly holds
			public Container Owner;
		}

		private sealed class Candidate
		{
			public Container Container;
			public Vector3 Anchor;
			public int Free;
			public bool Empty;
			public float Distance;
			public Sprite[] Icons;  // null when out of icon range or empty
		}

		private static GameObject _root;
		private static RectTransform _rootRect;
		private static Canvas _canvas;
		private static Sprite _circle;
		private static float _lastScan;
		private static int _occluders;
		private static readonly List<Badge> _pool = new List<Badge>();
		private static readonly List<Badge> _inUse = new List<Badge>();
		private static readonly List<Candidate> _candidates = new List<Candidate>();
		private static readonly Dictionary<Container, Collider> _colliders = new Dictionary<Container, Collider>();

		private static GrabMaterialsMod.GrabMaterialsMod Mod => GrabMaterialsMod.GrabMaterialsMod.Instance;
		private static Display Mode      => Mod?.BadgeDisplay?.Value ?? Display.BadgesAndIcons;
		private static Cycle ToggleCycle => Mod?.BadgeToggleCycle?.Value ?? Cycle.ThreeWay;
		private static IconSide Side     => Mod?.BadgeIconPosition?.Value ?? IconSide.Right;
		private static float Range       => Mod?.BadgeRange?.Value ?? 15f;
		private static float Size        => Mod?.BadgeSize?.Value ?? 28f;
		private static bool  HideFull    => Mod?.BadgeHideFull?.Value ?? false;
		private static bool  OnlyVisible => Mod?.BadgeOnlyVisible?.Value ?? true;
		private static float IconRange   => Mod?.BadgeIconRange?.Value ?? 10f;

		public static void Tick()
		{
			EnsureCreated();
			if (_root == null) return;

			var show = Wanted();
			if (_root.activeSelf != show) _root.SetActive(show);
			if (!show) return;

			var cam = Utils.GetMainCamera();
			if (cam == null) return;

			if (Time.time - _lastScan >= ScanInterval)
			{
				_lastScan = Time.time;
				Scan(cam);
			}
			Place(cam);
		}

		// Called on toggle so the badges appear or vanish at once.
		public static void Refresh()
		{
			_lastScan = 0f;
		}

		// The toggle key and `/badges`: step Display to the next state in the configured cycle.
		// A state outside the cycle (set in the config) steps to the cycle's first, which is off.
		public static void CycleDisplay()
		{
			var setting = Mod?.BadgeDisplay;
			if (setting == null) return;
			Display[] states;
			switch (ToggleCycle)
			{
				case Cycle.OffOrBadges:         states = new[] { Display.Off, Display.Badges }; break;
				case Cycle.OffOrBadgesAndIcons: states = new[] { Display.Off, Display.BadgesAndIcons }; break;
				default:                        states = new[] { Display.Off, Display.Badges, Display.BadgesAndIcons }; break;
			}
			var at = System.Array.IndexOf(states, setting.Value);
			setting.Value = states[(at + 1) % states.Length];
			Refresh();

			string message;
			switch (setting.Value)
			{
				case Display.Off:    message = "Container badges off"; break;
				case Display.Badges: message = "Container badges on"; break;
				default:             message = "Container badges on, with contents"; break;
			}
			Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, message);
		}

		private static bool Wanted()
		{
			if (Mode == Display.Off) return false;
			var player = Player.m_localPlayer;
			var hud = Hud.instance;
			if (player == null || hud == null) return false;
			if (player.IsDead() || player.IsTeleporting() || player.InCutscene()) return false;
			if (Hud.IsUserHidden()) return false;
			if (Menu.IsVisible() || Minimap.IsOpen()) return false;
			return true;
		}

		// Decide which containers get a badge this fifth of a second.
		private static void Scan(Camera cam)
		{
			_candidates.Clear();
			var player = Player.m_localPlayer;
			var origin = player.transform.position;
			var eye = cam.transform.position;
			var range = Range;
			var rangeSq = range * range;
			var screen = new Rect(-40f, -40f, Screen.width + 80f, Screen.height + 80f);

			foreach (var container in Boxes.Containers)
			{
				if (container == null) continue;
				var distSq = (container.transform.position - origin).sqrMagnitude;
				if (distSq > rangeSq) continue;
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				if (!ContainerAccess.HasAccess(container, out _)) continue;

				var free = inventory.GetEmptySlots();
				if (free <= 0 && HideFull) continue;

				var anchor = AnchorFor(container);
				var sp = cam.WorldToScreenPoint(anchor);
				if (sp.z <= 0f || !screen.Contains(new Vector2(sp.x, sp.y))) continue;

				if (OnlyVisible && Physics.Linecast(eye, anchor, out var hit, _occluders, QueryTriggerInteraction.Ignore))
				{
					// Hitting the container itself still counts as seeing it.
					if (hit.collider.GetComponentInParent<Container>() != container) continue;
				}

				var distance = Mathf.Sqrt(distSq);
				var empty = inventory.NrOfItems() == 0;
				var icons = Mode == Display.BadgesAndIcons && !empty && distance <= IconRange;
				_candidates.Add(new Candidate
				{
					Container = container,
					Anchor = anchor,
					Free = free,
					Empty = empty,
					Distance = distance,
					Icons = icons ? TopIcons(inventory) : null,
				});
			}
		}

		// Move every badge to its container's screen position; this runs every frame so the
		// badges stick to the world as the camera turns.
		private static void Place(Camera cam)
		{
			// Return badges whose container is no longer a candidate.
			for (var i = _inUse.Count - 1; i >= 0; i--)
			{
				var badge = _inUse[i];
				var still = false;
				foreach (var c in _candidates)
				{
					if (c.Container == badge.Owner) { still = true; break; }
				}
				if (still) continue;
				badge.Owner = null;
				badge.Root.SetActive(false);
				_inUse.RemoveAt(i);
				_pool.Add(badge);
			}

			var canvasCam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceCamera ? _canvas.worldCamera : null;
			foreach (var c in _candidates)
			{
				var badge = Find(c.Container) ?? Take(c.Container);
				var sp = cam.WorldToScreenPoint(c.Anchor);
				if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, new Vector2(sp.x, sp.y), canvasCam, out var local)) continue;
				badge.Rect.anchoredPosition = local;

				// Nearer badges are a little bigger; the clamp keeps them readable at the edge of range.
				var scale = Mathf.Clamp(8f / Mathf.Max(c.Distance, 0.1f), 0.6f, 1.25f);
				var diameter = Size * scale;
				badge.Rect.sizeDelta = new Vector2(diameter, diameter);
				badge.Fill.rectTransform.sizeDelta = new Vector2(diameter - RingWidth * 2f, diameter - RingWidth * 2f);
				badge.Label.fontSize = Mathf.Max(8, Mathf.RoundToInt(diameter * 0.55f));
				badge.Label.text = c.Free.ToString();
				badge.Fill.color = ColorFor(c.Free, c.Empty);
				badge.Ring.color = c.Empty ? EmptyRing : DarkRing;

				// Item icons sit in a row out from the disc on the configured side, the first one
				// touching it.
				var iconSize = diameter * 0.95f;
				var first = diameter / 2f + IconGap + iconSize / 2f;
				var step = iconSize + IconGap;
				var side = Side;
				var dir = Direction(side);
				var count = c.Icons != null ? c.Icons.Length : 0;
				for (var i = 0; i < MaxIcons; i++)
				{
					var image = badge.Icons[i];
					var sprite = c.Icons != null && i < c.Icons.Length ? c.Icons[i] : null;
					if (sprite == null)
					{
						if (image.gameObject.activeSelf) image.gameObject.SetActive(false);
						continue;
					}
					if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
					image.sprite = sprite;
					image.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
					if (side == IconSide.BelowRow || side == IconSide.AboveRow)
					{
						// A centered row one icon-height away, in the disc's pivot plane.
						var x = (i - (count - 1) / 2f) * step;
						var y = side == IconSide.BelowRow ? -first : first;
						image.rectTransform.anchoredPosition = new Vector2(x, y);
					}
					else
					{
						image.rectTransform.anchoredPosition = dir * (first + i * step);
					}
				}
			}
		}

		private static Vector2 Direction(IconSide side)
		{
			switch (side)
			{
				case IconSide.Down: return Vector2.down;
				case IconSide.Left: return Vector2.left;
				case IconSide.Up:   return Vector2.up;
				default:            return Vector2.right;
			}
		}

		// The one or two items that take the most slots in the container: slots, not counts,
		// because thirty slots of wood and one stack of coins make a wood chest.  One icon when
		// a single item holds half or more of the used slots, otherwise the top two.
		private static readonly Dictionary<string, int> _slotsByName = new Dictionary<string, int>();
		private static readonly Dictionary<string, Sprite> _iconByName = new Dictionary<string, Sprite>();

		private static Sprite[] TopIcons(Inventory inventory)
		{
			_slotsByName.Clear();
			_iconByName.Clear();
			var used = 0;
			foreach (var item in inventory.GetAllItems())
			{
				if (item == null || item.m_shared == null) continue;
				used++;
				var name = item.m_shared.m_name;
				_slotsByName.TryGetValue(name, out var slots);
				_slotsByName[name] = slots + 1;
				if (!_iconByName.ContainsKey(name))
				{
					var icon = IconOf(item);
					if (icon != null) _iconByName[name] = icon;
				}
			}
			if (used == 0) return null;

			string first = null, second = null;
			var firstSlots = 0; var secondSlots = 0;
			foreach (var kv in _slotsByName)
			{
				if (!_iconByName.ContainsKey(kv.Key)) continue;
				if (kv.Value > firstSlots)
				{
					second = first; secondSlots = firstSlots;
					first = kv.Key; firstSlots = kv.Value;
				}
				else if (kv.Value > secondSlots)
				{
					second = kv.Key; secondSlots = kv.Value;
				}
			}
			if (first == null) return null;
			if (second == null || firstSlots * 2 >= used) return new[] { _iconByName[first] };
			return new[] { _iconByName[first], _iconByName[second] };
		}

		private static Sprite IconOf(ItemDrop.ItemData item)
		{
			var icons = item.m_shared.m_icons;
			if (icons == null || icons.Length == 0) return null;
			var variant = item.m_variant;
			return variant >= 0 && variant < icons.Length && icons[variant] != null ? icons[variant] : icons[0];
		}

		private static readonly Color DarkRing  = new Color(0f, 0f, 0f, 0.75f);
		private static readonly Color EmptyRing = new Color(1f, 1f, 1f, 0.95f);

		private static Color ColorFor(int free, bool empty)
		{
			if (empty)     return new Color(0.25f, 0.55f, 0.95f, 0.92f);  // nothing in it at all: blue, white ring
			if (free <= 0) return new Color(0.80f, 0.20f, 0.18f, 0.92f);  // full: red
			if (free <= 3) return new Color(0.92f, 0.70f, 0.18f, 0.92f);  // nearly full: amber
			return new Color(0.28f, 0.66f, 0.30f, 0.92f);                 // room: green
		}

		private static Badge Find(Container container)
		{
			foreach (var b in _inUse)
			{
				if (b.Owner == container) return b;
			}
			return null;
		}

		private static Badge Take(Container container)
		{
			Badge badge;
			if (_pool.Count > 0)
			{
				badge = _pool[_pool.Count - 1];
				_pool.RemoveAt(_pool.Count - 1);
			}
			else
			{
				badge = Create();
			}
			badge.Owner = container;
			badge.Root.SetActive(true);
			_inUse.Add(badge);
			return badge;
		}

		// A point just above the container's collider, so the badge sits on its lid rather
		// than in its middle.  Carts and ships move, so the bounds are read fresh each scan;
		// only the collider lookup is cached.
		private static Vector3 AnchorFor(Container container)
		{
			if (!_colliders.TryGetValue(container, out var col) || col == null)
			{
				col = null;
				foreach (var candidate in container.GetComponentsInChildren<Collider>())
				{
					if (!candidate.isTrigger) { col = candidate; break; }
				}
				_colliders[container] = col;
			}
			if (col == null) return container.transform.position + Vector3.up;
			var b = col.bounds;
			return new Vector3(b.center.x, b.max.y + AboveTop, b.center.z);
		}

		private static void EnsureCreated()
		{
			if (_root != null) return;
			if (GUIManager.Instance == null || GUIManager.CustomGUIBack == null) return;

			// The canvas is rebuilt on every scene change, taking our objects with it.
			_pool.Clear();
			_inUse.Clear();
			_colliders.Clear();

			_occluders = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
			if (_circle == null) _circle = MakeCircle(64);

			_root = new GameObject("GrabMaterials_ContainerBadges", typeof(RectTransform));
			_root.transform.SetParent(GUIManager.CustomGUIBack.transform, false);
			_rootRect = _root.GetComponent<RectTransform>();
			_rootRect.anchorMin = Vector2.zero;
			_rootRect.anchorMax = Vector2.one;
			_rootRect.offsetMin = Vector2.zero;
			_rootRect.offsetMax = Vector2.zero;
			_canvas = _root.GetComponentInParent<Canvas>();
		}

		private static Badge Create()
		{
			var root = new GameObject("Badge", typeof(RectTransform));
			root.transform.SetParent(_root.transform, false);
			var rect = root.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);

			var ring = MakeImage("Ring", root.transform, DarkRing);
			ring.rectTransform.anchorMin = Vector2.zero;
			ring.rectTransform.anchorMax = Vector2.one;
			ring.rectTransform.offsetMin = Vector2.zero;
			ring.rectTransform.offsetMax = Vector2.zero;

			var fill = MakeImage("Fill", root.transform, Color.white);
			fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

			var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
			labelGo.transform.SetParent(root.transform, false);
			var labelRect = labelGo.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = Vector2.zero;
			labelRect.offsetMax = Vector2.zero;
			var label = labelGo.GetComponent<Text>();
			label.font = GUIManager.Instance.AveriaSerifBold;
			label.alignment = TextAnchor.MiddleCenter;
			label.color = Color.white;
			label.horizontalOverflow = HorizontalWrapMode.Overflow;
			label.verticalOverflow = VerticalWrapMode.Overflow;
			label.raycastTarget = false;
			var outline = labelGo.GetComponent<Outline>();
			outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
			outline.effectDistance = new Vector2(1f, -1f);

			var icons = new Image[MaxIcons];
			for (var i = 0; i < MaxIcons; i++)
			{
				var go = new GameObject("Icon" + i, typeof(RectTransform), typeof(Image));
				go.transform.SetParent(root.transform, false);
				var image = go.GetComponent<Image>();
				image.preserveAspect = true;
				image.raycastTarget = false;
				image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
				image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
				go.SetActive(false);
				icons[i] = image;
			}

			return new Badge { Root = root, Rect = rect, Ring = ring, Fill = fill, Label = label, Icons = icons };
		}

		private static Image MakeImage(string name, Transform parent, Color color)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			var image = go.GetComponent<Image>();
			image.sprite = _circle;
			image.color = color;
			image.raycastTarget = false;
			return image;
		}

		// An anti-aliased white disc; tinted per badge through Image.color.
		private static Sprite MakeCircle(int size)
		{
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			var r = size / 2f;
			var pixels = new Color[size * size];
			for (var y = 0; y < size; y++)
			{
				for (var x = 0; x < size; x++)
				{
					var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
					var a = Mathf.Clamp01(r - d + 0.5f);
					pixels[y * size + x] = new Color(1f, 1f, 1f, a);
				}
			}
			tex.SetPixels(pixels);
			tex.Apply();
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}
	}
}
