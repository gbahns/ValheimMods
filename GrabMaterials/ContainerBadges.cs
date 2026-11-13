using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace GrabMaterials
{
	// A round badge floating over each nearby container with its number of free slots, so
	// you can find a chest with room without opening them one by one (#45).
	//
	// Two ways to draw them, chosen by "Draw Container Badges In Front":
	//
	// In front (the default): ordinary screen-space UI on Jotunn's back canvas, placed every
	// frame by projecting a point just above each container into the screen.  That keeps them
	// behind every panel, lets them scale with distance, and costs nothing per container
	// beyond a matrix multiply.  Nothing in the world can cover one, so which containers get a
	// badge is decided five times a second: in range, in front of the camera, and (by default)
	// not hidden behind a wall, which one linecast per candidate answers.
	//
	// In the world: the same UI on a world-space canvas, each badge turned to face the camera
	// and scaled so it keeps its pixel size.  UI/Default depth-tests in world space, so the
	// depth buffer covers a badge with whatever stands in front of it, pixel by pixel: the
	// player's own body, a wall, a boar.  No linecast is needed, and none is done.
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
		private static bool _builtInFront;   // which mode _root was built for
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
		private static bool  InFront     => Mod?.BadgeDrawInFront?.Value ?? true;

		public static void Tick()
		{
			if (_root != null && _builtInFront != InFront) Rebuild();
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

		// Throw the badges away; the next Tick builds them again for the current mode.
		public static void Rebuild()
		{
			if (_root != null) Object.Destroy(_root);
			_root = null;
			_rootRect = null;
			_canvas = null;
			_pool.Clear();
			_inUse.Clear();
			_lastScan = 0f;
		}

		// The toggle key and `/badges`: step Display to the next state in the configured cycle.
		public static void CycleDisplay()
		{
			var setting = Mod?.BadgeDisplay;
			if (setting == null) return;
			var states = States(ToggleCycle);
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

		// Toggle Cycle decides which states exist; Display is only the remembered position.  So a
		// Display the cycle never visits is snapped to its nearest member, on load and whenever
		// either setting changes: a two-way cycle without icons never shows icons, and one with
		// icons never shows a bare badge.
		public static void ConformDisplay()
		{
			var setting = Mod?.BadgeDisplay;
			if (setting == null) return;
			var states = States(ToggleCycle);
			if (System.Array.IndexOf(states, setting.Value) >= 0) return;
			setting.Value = setting.Value == Display.Off ? Display.Off : states[states.Length - 1];
			Refresh();
		}

		private static Display[] States(Cycle cycle)
		{
			switch (cycle)
			{
				case Cycle.OffOrBadges:         return new[] { Display.Off, Display.Badges };
				case Cycle.OffOrBadgesAndIcons: return new[] { Display.Off, Display.BadgesAndIcons };
				default:                        return new[] { Display.Off, Display.Badges, Display.BadgesAndIcons };
			}
		}

		private static bool Wanted()
		{
			if (Mode == Display.Off) return false;
			var player = Player.m_localPlayer;
			var hud = Hud.instance;
			if (player == null || hud == null) return false;
			if (player.IsDead() || player.IsTeleporting() || player.InCutscene()) return false;
			if (Hud.IsUserHidden()) return false;
			if (Minimap.IsOpen()) return false;
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

				// Drawn in the world, the depth buffer does this per pixel.
				if (InFront && OnlyVisible && Physics.Linecast(eye, anchor, out var hit, _occluders, QueryTriggerInteraction.Ignore))
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

			var inFront = InFront;
			var canvasCam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceCamera ? _canvas.worldCamera : null;
			// In the world, one canvas unit is one overlay pixel at the overlay's scale, so the
			// badge keeps the size the overlay would give it at that distance.
			var overlayCanvas = GUIManager.CustomGUIBack != null ? GUIManager.CustomGUIBack.GetComponent<Canvas>() : null;
			var uiScale = overlayCanvas != null ? overlayCanvas.scaleFactor : 1f;
			var pixelAngle = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
			var camPos = cam.transform.position;
			var camForward = cam.transform.forward;
			foreach (var c in _candidates)
			{
				var badge = Find(c.Container) ?? Take(c.Container);

				// Nearer badges are a little bigger; the clamp keeps them readable at the edge of range.
				var scale = Mathf.Clamp(8f / Mathf.Max(c.Distance, 0.1f), 0.6f, 1.25f);
				var diameter = Size * scale;

				if (inFront)
				{
					var sp = cam.WorldToScreenPoint(c.Anchor);
					if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, new Vector2(sp.x, sp.y), canvasCam, out var local)) continue;
					badge.Rect.anchoredPosition = local;
				}
				else
				{
					var depth = Vector3.Dot(c.Anchor - camPos, camForward);
					if (depth <= 0.1f)
					{
						if (badge.Root.activeSelf) badge.Root.SetActive(false);
						continue;
					}
					if (!badge.Root.activeSelf) badge.Root.SetActive(true);
					var worldPerPixel = depth * pixelAngle * uiScale;
					// Face the camera, and lean out of the lid toward it by the disc's radius so
					// the disc does not cut into the container it marks.
					var toCam = (camPos - c.Anchor).normalized;
					badge.Rect.position = c.Anchor + toCam * (diameter * 0.5f * worldPerPixel + 0.05f);
					badge.Rect.rotation = cam.transform.rotation;
					badge.Rect.localScale = Vector3.one * worldPerPixel;
				}
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
			// The font comes from Jotunn either way, and the overlay lives on its back canvas.
			if (GUIManager.Instance == null || GUIManager.CustomGUIBack == null) return;

			// The canvas is rebuilt on every scene change, taking our objects with it.
			_pool.Clear();
			_inUse.Clear();
			_colliders.Clear();

			// What hides an overlay badge.  Characters are not in it: a badge over a chest behind
			// the player stays up in front of them, which is what "Draw Container Badges In
			// Front" promises; the world mode is the one that lets a body cover a badge.
			_occluders = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
			if (_circle == null) _circle = MakeCircle(64);

			_builtInFront = InFront;
			if (_builtInFront)
			{
				_root = new GameObject("GrabMaterials_ContainerBadges", typeof(RectTransform));
				_root.transform.SetParent(GUIManager.CustomGUIBack.transform, false);
				_rootRect = _root.GetComponent<RectTransform>();
				_rootRect.anchorMin = Vector2.zero;
				_rootRect.anchorMax = Vector2.one;
				_rootRect.offsetMin = Vector2.zero;
				_rootRect.offsetMax = Vector2.zero;
				_canvas = _root.GetComponentInParent<Canvas>();
			}
			else
			{
				// A world-space canvas at the origin with no size of its own; each badge is
				// positioned, turned and scaled in world units by Place.  It sits in the scene,
				// so a scene change destroys it and the next Tick builds it again.
				_root = new GameObject("GrabMaterials_ContainerBadges_World", typeof(RectTransform), typeof(Canvas));
				_canvas = _root.GetComponent<Canvas>();
				_canvas.renderMode = RenderMode.WorldSpace;
				_canvas.worldCamera = Utils.GetMainCamera();
				_rootRect = _root.GetComponent<RectTransform>();
				_rootRect.position = Vector3.zero;
				_rootRect.sizeDelta = Vector2.zero;
			}
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
