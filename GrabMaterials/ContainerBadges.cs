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
	// Toggled with the "Container Badges" key or `/badges`; the on/off state is written back
	// to the "Enabled" setting so it persists.
	public static class ContainerBadges
	{
		private const float ScanInterval = 0.2f;
		private const float RingWidth = 2f;
		private const float AboveTop = 0.15f;   // meters above the container's collider

		private sealed class Badge
		{
			public GameObject Root;
			public RectTransform Rect;
			public Image Ring;
			public Image Fill;
			public Text Label;
			public Container Owner;
		}

		private sealed class Candidate
		{
			public Container Container;
			public Vector3 Anchor;
			public int Free;
			public float Distance;
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
		private static bool  Enabled     => Mod?.ShowContainerBadges?.Value ?? true;
		private static float Range       => Mod?.BadgeRange?.Value ?? 15f;
		private static float Size        => Mod?.BadgeSize?.Value ?? 28f;
		private static bool  HideFull    => Mod?.BadgeHideFull?.Value ?? false;
		private static bool  OnlyVisible => Mod?.BadgeOnlyVisible?.Value ?? true;

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

		private static bool Wanted()
		{
			if (!Enabled) return false;
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

				_candidates.Add(new Candidate
				{
					Container = container,
					Anchor = anchor,
					Free = free,
					Distance = Mathf.Sqrt(distSq),
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
				badge.Fill.color = ColorFor(c.Free);
			}
		}

		private static Color ColorFor(int free)
		{
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

			var ring = MakeImage("Ring", root.transform, new Color(0f, 0f, 0f, 0.75f));
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

			return new Badge { Root = root, Rect = rect, Ring = ring, Fill = fill, Label = label };
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
