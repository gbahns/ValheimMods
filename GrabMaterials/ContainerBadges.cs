using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GrabMaterials
{
	// A round badge floating over each nearby container with its number of free slots, so
	// you can find a chest with room without opening them one by one (#45).
	//
	// Two ways to draw them, chosen by "Draw Badges In Front":
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
	// Drawn by the main camera, a world badge also goes through the game's post-processing
	// (bloom, color grading, fog, sun shafts), which is what makes it look lit.  Unless "Lit
	// World Badges" is on, the badges are instead drawn by a second camera, parented to the
	// main one and given none of its effects, that renders only the badges' layer after the
	// main camera has finished.  That camera has no scene depth of its own, so the badges use
	// a soft-particle shader, which reads a depth texture and fades a pixel out when it is
	// behind the scene.  With a steep fade that is a depth test, and the badges come out in
	// their flat colors while still hiding behind whatever is in front.
	//
	// The depth it reads has to be the main camera's, and getting it there takes two steps.
	// The game asks its camera for a depth-normals texture only, so the badges add the plain
	// depth texture to the request.  That texture is a temporary the engine hands back at the
	// end of the main camera's render, and a second camera cannot count on finding it still
	// bound (if the game's soft particles are on, the badge camera renders a depth texture of
	// its own layer and gets the same temporary, now empty).  So a command buffer on the main
	// camera copies the depth texture into one the badges own, and a command buffer on the
	// badge camera binds that copy as the depth texture just before the badges draw.  Without
	// this the badges showed through every wall and body in flat mode (reported 2026-11-13).
	//
	// The shader turns a depth value into meters with _ZBufferParams, which the engine sets
	// for the way this platform's depth buffers run (1 at the near plane on D3D and Vulkan, 0
	// there on OpenGL), and a copy made with Blit can come out upside down, since Blit decides
	// about flipping from the targets involved and a depth texture is not an ordinary one.
	// Either fault hides badges that should show and shows ones that should hide; the first
	// build did exactly that.  So once, a few frames after the copy starts, it is read back,
	// five points on it are turned into meters both ways, right side up and flipped, and rays
	// through those screen points say which of the four readings the scene agrees with.  A
	// flipped copy is blitted the other way up from then on; one that runs the other way gets
	// the badge camera matching _ZBufferParams.  The log carries the numbers either way.
	//
	// The probe also tells whether the copy is depth at all.  On this game's deferred camera
	// neither the Depth builtin nor the _CameraDepthTexture property handed Blit depth (the
	// readings ran above 1, which no depth holds, and fit the rays at 2 to 6 where real depth
	// fits at 0.3 to 0.4); the resolved depth buffer did.  So that is the first source, and a
	// copy whose readings are impossible (outside 0 to 1) moves on to the next.
	//
	// A probe that fails any other way changes nothing.  Right after login the copy and the rays
	// once disagreed by a factor of ten (the copy still held the main menu's scene; see
	// EnsureCreated), and acting on that verdict rejected every source and locked in a flipped
	// copy, which is exactly the reversed behavior.  So a bad fit with plausible readings is
	// tried again half a second later, for up to a minute, and only a passing probe settles
	// anything.  When the minute runs out the copy is kept as it is, with a warning.
	//
	// Two things make the particle shader draw the badges differently from the UI shader, and
	// both are undone so flat badges look like the overlay ones.  Its tint is a Color property,
	// which a linear-color project converts from sRGB, so the one-half tint that should cancel
	// the shader's doubling arrived as 0.21 and the colors came out at 43%: the tint is set to
	// the sRGB value that converts to one half.  And a font atlas is an alpha-only texture
	// that samples as black; the UI shader adds white to it, the particle shader multiplies
	// by it, so the number came out black.  The label is a Text whose texture, in flat mode,
	// is a copy of the atlas with every pixel's color set to white (FlatText below), refreshed
	// whenever the font rebuilds its atlas.
	//
	// "Only Visible Containers" is the one setting that means something in every mode: in
	// front it is the linecast; flat, it decides whether the depth test is done at all.  Lit
	// world badges are depth-tested by the UI shader and cannot be told otherwise.  If the
	// shader, a spare layer or a float render texture cannot be had, the badges fall back to
	// the lit look and the log says why.
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

		// A Text that, in flat mode, draws from the white copy of its font's atlas.
		private sealed class FlatText : Text
		{
			public override Texture mainTexture =>
				_badgeCamera != null && _fontCopy != null && font != null && font.material != null && font.material.mainTexture == _fontCopySource
					? _fontCopy
					: base.mainTexture;
		}

		private static Texture2D _fontCopy;        // the badge font's atlas, alpha kept, color made white
		private static Texture _fontCopySource;    // the atlas it was copied from
		private static bool _fontCopyDirty = true;
		private static bool _fontHooked;

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
		private static bool _builtLit;
		private static bool _builtOnlyVisible;
		private static Camera _badgeCamera;  // flat world mode only
		private static Material _flatMaterial;
		private static int _badgeLayer = -1;
		private static Camera _maskedCamera;   // the main camera we took _badgeLayer out of, to give it back
		private static Camera _depthCamera;    // the main camera whose depth the flat badges test against
		private static bool _addedDepthMode;   // we asked _depthCamera for a depth texture it was not making
		private static RenderTexture _depthCopy;
		private static CommandBuffer _copyDepth;   // on _depthCamera: depth texture -> _depthCopy
		private static CommandBuffer _bindDepth;   // on _badgeCamera: _depthCopy -> _CameraDepthTexture
		private static readonly int DepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
		private static readonly RenderTargetIdentifier[] DepthSources =
		{
			BuiltinRenderTextureType.ResolvedDepth,
			new RenderTargetIdentifier(DepthTextureId),
			BuiltinRenderTextureType.Depth,
		};
		private static readonly string[] DepthSourceNames = { "the resolved depth buffer", "the _CameraDepthTexture property", "the Depth builtin" };
		private static int _depthSource;          // index into DepthSources the copy is taken from
		private const float GoodFit = 1.5f;       // summed |ln(read / ray)| over the five points; real depth measured 0.3 to 0.4, color 2 to 6
		private const int ProbeAfter = 3;         // frames after setup before the first probe
		private const int ProbeRetryAfter = 30;   // frames between probes while none has passed
		private const int ProbeTries = 120;       // about a minute of retries before giving up
		private static int _probeTries;
		private static readonly int ZBufferParamsId = Shader.PropertyToID("_ZBufferParams");
		private static bool _probed;          // the copy's convention has been checked against a ray
		private static int _probeFrames;
		private static bool _otherWayRound;   // the copy runs the other way from this platform's buffers
		private static bool _flipped;         // the copy comes out upside down and is blitted back
		private const CameraEvent CopyDepthAt = CameraEvent.AfterForwardAlpha;   // the scene is complete, the effects have not run
		private const CameraEvent BindDepthAt = CameraEvent.BeforeForwardAlpha;  // right before the badges draw
		private const float FlatFade = 100f;   // soft-particle steepness: fully hidden 1 cm behind the scene
		private static readonly string[] FlatShaders =
		{
			"Legacy Shaders/Particles/Alpha Blended",
			"Particles/Alpha Blended",
			"Mobile/Particles/Alpha Blended",
		};
		private static readonly List<Badge> _pool = new List<Badge>();
		private static readonly List<Badge> _inUse = new List<Badge>();
		private static readonly List<Candidate> _candidates = new List<Candidate>();
		private static readonly Dictionary<Container, Collider> _colliders = new Dictionary<Container, Collider>();

		private static GrabMaterialsMod.GrabMaterialsMod Mod => GrabMaterialsMod.GrabMaterialsMod.Instance;
		private static BepInEx.Logging.ManualLogSource Log => GrabMaterialsMod.GrabMaterialsMod.Log;
		private static Display Mode      => Mod?.BadgeDisplay?.Value ?? Display.BadgesAndIcons;
		private static Cycle ToggleCycle => Mod?.BadgeToggleCycle?.Value ?? Cycle.ThreeWay;
		private static IconSide Side     => Mod?.BadgeIconPosition?.Value ?? IconSide.Right;
		private static float Range       => Mod?.BadgeRange?.Value ?? 15f;
		private static float Size        => Mod?.BadgeSize?.Value ?? 28f;
		private static bool  HideFull    => Mod?.BadgeHideFull?.Value ?? false;
		private static bool  OnlyVisible => Mod?.BadgeOnlyVisible?.Value ?? true;
		private static float IconRange   => Mod?.BadgeIconRange?.Value ?? 10f;
		private static bool  InFront     => Mod?.BadgeDrawInFront?.Value ?? true;
		private static bool  Lit         => Mod?.BadgeWorldLit?.Value ?? false;

		public static void Tick()
		{
			if (_root != null && (_builtInFront != InFront || _builtLit != Lit
				|| (!_builtInFront && !_builtLit && _builtOnlyVisible != OnlyVisible))) Rebuild();
			// Nothing is built until it is wanted: the main menu has a camera and Jotunn's canvas
			// too, and badges set up there took the menu's depth picture into the game.
			var show = Wanted();
			if (!show)
			{
				if (_root != null && _root.activeSelf) _root.SetActive(false);
				if (_badgeCamera != null && _badgeCamera.enabled) _badgeCamera.enabled = false;
				return;
			}
			EnsureCreated();
			if (_root == null) return;
			if (!_root.activeSelf) _root.SetActive(true);
			if (_badgeCamera != null && !_badgeCamera.enabled) _badgeCamera.enabled = true;

			var cam = Utils.GetMainCamera();
			if (cam == null) return;
			if (_badgeCamera != null) SyncBadgeCamera(cam);
			if (_badgeCamera != null) RefreshFontCopy();
			if (_copyDepth != null && !_probed && ++_probeFrames >= ProbeAfter) ProbeDepthCopy(cam);

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
			if (_badgeCamera != null) Object.Destroy(_badgeCamera.gameObject);
			if (_flatMaterial != null) Object.Destroy(_flatMaterial);
			if (_maskedCamera != null && _badgeLayer >= 0) _maskedCamera.cullingMask |= 1 << _badgeLayer;
			if (_depthCamera != null)
			{
				if (_copyDepth != null) _depthCamera.RemoveCommandBuffer(CopyDepthAt, _copyDepth);
				if (_addedDepthMode) _depthCamera.depthTextureMode &= ~DepthTextureMode.Depth;
			}
			_copyDepth?.Dispose();
			_bindDepth?.Dispose();
			if (_depthCopy != null) { _depthCopy.Release(); Object.Destroy(_depthCopy); }
			_maskedCamera = null;
			_depthCamera = null;
			_addedDepthMode = false;
			_copyDepth = null;
			_bindDepth = null;
			_depthCopy = null;
			_probed = false;
			_probeFrames = 0;
			_otherWayRound = false;
			_flipped = false;
			_depthSource = 0;
			_probeTries = 0;
			_root = null;
			_rootRect = null;
			_canvas = null;
			_badgeCamera = null;
			_flatMaterial = null;
			_badgeLayer = -1;
			_pool.Clear();
			_inUse.Clear();
			_lastScan = 0f;
		}

		// Keep the badge camera a copy of the main one, drawing only the badges' layer, after
		// it, over its finished image, with no effects and no scene depth of its own.
		private static void SyncBadgeCamera(Camera main)
		{
			_badgeCamera.CopyFrom(main);
			_badgeCamera.targetTexture = main.targetTexture;   // the game's lowered render scale draws into a texture
			_badgeCamera.cullingMask = 1 << _badgeLayer;
			_badgeCamera.clearFlags = CameraClearFlags.Depth;
			_badgeCamera.depth = main.depth + 1f;
			_badgeCamera.renderingPath = RenderingPath.Forward;   // nothing opaque to defer, and BeforeForwardAlpha must fire
			_badgeCamera.depthTextureMode = DepthTextureMode.None;
			_badgeCamera.allowHDR = false;
			_badgeCamera.allowMSAA = false;
			_badgeCamera.useOcclusionCulling = false;
			_badgeCamera.transform.localPosition = Vector3.zero;
			_badgeCamera.transform.localRotation = Quaternion.identity;
			if (_copyDepth != null)
			{
				// The game's effects set the mode as they please; keep our bit in it.
				if ((main.depthTextureMode & DepthTextureMode.Depth) == 0) main.depthTextureMode |= DepthTextureMode.Depth;
				EnsureDepthCopy(main);
			}
		}

		// A float texture the size of the main camera's picture, and the two command buffers
		// that fill it and bind it.  Re-recorded when the picture changes size.
		private static void EnsureDepthCopy(Camera main)
		{
			var w = Mathf.Max(1, main.pixelWidth);
			var h = Mathf.Max(1, main.pixelHeight);
			if (_depthCopy != null && _depthCopy.width == w && _depthCopy.height == h) return;
			if (_depthCopy != null) { _depthCopy.Release(); Object.Destroy(_depthCopy); }
			_depthCopy = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat)
			{
				name = "GrabMaterials_BadgeDepth",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
			};
			_depthCopy.Create();
			RecordCopy();
			RecordBind(main);
		}

		private static void RecordCopy()
		{
			_copyDepth.Clear();
			if (_flipped) _copyDepth.Blit(DepthSources[_depthSource], _depthCopy, new Vector2(1f, -1f), new Vector2(0f, 1f));
			else _copyDepth.Blit(DepthSources[_depthSource], _depthCopy);
		}

		private static void RecordBind(Camera main)
		{
			_bindDepth.Clear();
			_bindDepth.SetGlobalTexture(DepthTextureId, _depthCopy);
			if (_otherWayRound) _bindDepth.SetGlobalVector(ZBufferParamsId, ZBufferParams(main.nearClipPlane, main.farClipPlane, !SystemInfo.usesReversedZBuffer));
		}

		// What the engine puts in _ZBufferParams for a depth buffer that is 1 at the near
		// plane (reversed) or 0 there; LinearEyeDepth is 1 / (z * depth + w).
		private static Vector4 ZBufferParams(float near, float far, bool reversed)
		{
			if (reversed)
			{
				var x = -1f + far / near;
				return new Vector4(x, 1f, x / far, 1f / far);
			}
			var x0 = 1f - far / near;
			var y0 = far / near;
			return new Vector4(x0, y0, x0 / far, y0 / far);
		}

		private static float EyeDepth(float raw, Vector4 p) => 1f / (p.z * raw + p.w);

		// Five points on the screen: the center and part way toward each edge.
		private static readonly Vector2[] ProbePoints =
		{
			new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.8f), new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f),
		};

		// Read the copy back, turn five of its points into meters every way they could be
		// read, and ask rays through those points which way the scene agrees with.  Runs off
		// the main thread's critical path, again and again until a reading passes.
		private static void ProbeDepthCopy(Camera cam)
		{
			_probed = true;
			var copy = _depthCopy;
			var camPos = cam.transform.position;
			var forward = cam.transform.forward;
			var near = cam.nearClipPlane;
			var far = cam.farClipPlane;
			var truths = new float[ProbePoints.Length];
			for (var i = 0; i < ProbePoints.Length; i++)
			{
				var ray = cam.ViewportPointToRay(new Vector3(ProbePoints[i].x, ProbePoints[i].y, 0f));
				truths[i] = far;
				if (Physics.Raycast(ray, out var hit, far, ~0, QueryTriggerInteraction.Ignore)) truths[i] = Mathf.Max(near, Vector3.Dot(hit.point - camPos, forward));
			}
			var usual = ZBufferParams(near, far, SystemInfo.usesReversedZBuffer);
			var other = ZBufferParams(near, far, !SystemInfo.usesReversedZBuffer);
			AsyncGPUReadback.Request(copy, 0, TextureFormat.RFloat, request =>
			{
				if (copy != _depthCopy || _bindDepth == null) return;   // rebuilt meanwhile; the next setup probes again
				if (request.hasError)
				{
					Log.LogWarning("badges: the depth copy could not be read back; the badges read it as it is");
					return;
				}
				var data = request.GetData<float>();
				var w = copy.width;
				var h = copy.height;
				float Raw(int i, bool flipped)
				{
					var x = Mathf.Clamp(Mathf.RoundToInt(ProbePoints[i].x * (w - 1)), 0, w - 1);
					var v = flipped ? 1f - ProbePoints[i].y : ProbePoints[i].y;
					var y = Mathf.Clamp(Mathf.RoundToInt(v * (h - 1)), 0, h - 1);
					return data[y * w + x];
				}
				float Error(Vector4 p, bool flipped)
				{
					var sum = 0f;
					for (var i = 0; i < ProbePoints.Length; i++)
					{
						var z = Mathf.Max(EyeDepth(Raw(i, flipped), p), 0.001f);
						sum += Mathf.Abs(Mathf.Log(z / truths[i]));
					}
					return sum;
				}
				var errors = new[] { Error(usual, false), Error(usual, true), Error(other, false), Error(other, true) };
				var best = 0;
				for (var i = 1; i < errors.Length; i++) if (errors[i] < errors[best]) best = i;
				var plausible = true;   // every reading is something a depth buffer could hold
				for (var i = 0; i < ProbePoints.Length && plausible; i++)
				{
					var raw = Raw(i, false);
					if (raw < 0f || raw > 1.0001f) plausible = false;
				}
				var isDepth = plausible && errors[best] < GoodFit;
				_probeTries++;

				var sb = new System.Text.StringBuilder($"badges: the depth copy from {DepthSourceNames[_depthSource]}, at the screen center and toward the bottom, top, left and right, reads");
				for (var i = 0; i < ProbePoints.Length; i++) sb.Append($" {Raw(i, false):F4}");
				sb.Append("; rays find the scene at");
				for (var i = 0; i < ProbePoints.Length; i++) sb.Append($" {truths[i]:F1}");
				sb.Append($" m; fit (lower is better): as is {errors[0]:F2}, flipped {errors[1]:F2}, other way {errors[2]:F2}, both {errors[3]:F2}");
				var tail = $" (near {near:F2}, far {far:F0}, reversed Z {SystemInfo.usesReversedZBuffer}, {cam.actualRenderingPath}, drawn to {(cam.targetTexture != null ? "a texture" : "the screen")}, {w}x{h})";
				if (!plausible && _depthSource + 1 < DepthSources.Length)
				{
					// Not depth at all; the next source gets the same chance.
					_depthSource++;
					_probed = false;
					_probeFrames = 0;
					sb.Append($"; that is not depth, so the copy is taken from {DepthSourceNames[_depthSource]} next").Append(tail);
					Log.LogInfo(sb.ToString());
					RecordCopy();
					RecordBind(cam);
					return;
				}
				if (!isDepth && _probeTries < ProbeTries)
				{
					// The scene and the copy disagree, as they do while a login settles; look again soon.
					_probed = false;
					_probeFrames = ProbeAfter - ProbeRetryAfter;
					if (_probeTries == 1) Log.LogInfo(sb.Append("; the scene and the copy do not agree yet, so the probe runs again until they do").Append(tail).ToString());
					return;
				}
				if (!isDepth)
				{
					// A minute of disagreement; keep the copy as it is and say so.
					sb.Append("; no probe passed, so world badges may not hide behind things; the copy is read as it is").Append(tail);
					Log.LogWarning(sb.ToString());
					return;
				}
				_otherWayRound = best >= 2;
				_flipped = (best & 1) == 1;
				sb.Append("; so the copy is read ");
				sb.Append(_flipped ? "flipped" : "right side up");
				sb.Append(_otherWayRound ? " and the other way round" : " and the usual way");
				if (_probeTries > 1) sb.Append($" (probe {_probeTries})");
				sb.Append(tail);
				Log.LogInfo(sb.ToString());
				RecordCopy();
				RecordBind(cam);
			});
		}

		// Copy the badge font's atlas with its alpha and white color, so the particle shader
		// draws the number white.  The atlas is not readable, so it goes through a render
		// texture; a font rebuild marks the copy stale and it is made again.
		private static void RefreshFontCopy()
		{
			var font = GUIManager.Instance != null ? GUIManager.Instance.AveriaSerifBold : null;
			var atlas = font != null && font.material != null ? font.material.mainTexture : null;
			if (atlas == null) return;
			if (!_fontHooked)
			{
				Font.textureRebuilt += rebuilt => { if (rebuilt == font) _fontCopyDirty = true; };
				_fontHooked = true;
			}
			if (!_fontCopyDirty && _fontCopySource == atlas && _fontCopy != null && _fontCopy.width == atlas.width && _fontCopy.height == atlas.height) return;
			_fontCopyDirty = false;
			_fontCopySource = atlas;

			var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			var was = RenderTexture.active;
			Graphics.Blit(atlas, rt);
			RenderTexture.active = rt;
			if (_fontCopy == null || _fontCopy.width != atlas.width || _fontCopy.height != atlas.height)
			{
				if (_fontCopy != null) Object.Destroy(_fontCopy);
				_fontCopy = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false) { name = "GrabMaterials_BadgeFont", filterMode = atlas.filterMode };
			}
			_fontCopy.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0, false);
			RenderTexture.active = was;
			RenderTexture.ReleaseTemporary(rt);
			var pixels = _fontCopy.GetPixels32();
			for (var i = 0; i < pixels.Length; i++)
			{
				var a = pixels[i].a;
				pixels[i] = new Color32(255, 255, 255, a);
			}
			_fontCopy.SetPixels32(pixels);
			_fontCopy.Apply(false, false);
			foreach (var badge in _inUse) badge.Label.SetMaterialDirty();
			foreach (var badge in _pool) badge.Label.SetMaterialDirty();
		}

		// The shader, layer, material and camera for flat world badges.  False, with the
		// reason logged, when the game does not offer what it needs.
		private static bool SetupFlat(Camera main)
		{
			Shader shader = null;
			foreach (var candidate in Resources.FindObjectsOfTypeAll<Shader>())
			{
				if (candidate == null || !candidate.isSupported) continue;
				var rank = System.Array.IndexOf(FlatShaders, candidate.name);
				if (rank < 0) continue;
				if (shader == null || rank < System.Array.IndexOf(FlatShaders, shader.name)) shader = candidate;
			}
			if (shader == null)
			{
				Log.LogWarning("badges: no soft-particle shader is loaded (" + string.Join(", ", FlatShaders) + "); world badges take the scene's light");
				return false;
			}

			// A layer for the badges alone.  The main camera draws every layer, so one is taken
			// from it: an unnamed one, which nothing in the game sits on, so the main camera's
			// picture does not change.  Unity names none of 3, 6 and 7 and the game names few
			// of the upper ones.  Rebuild gives the layer back.
			_badgeLayer = -1;
			foreach (var layer in new[] { 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 3 })
			{
				if (!string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;
				_badgeLayer = layer;
				break;
			}
			if (_badgeLayer < 0)
			{
				Log.LogWarning("badges: every layer is named, so there is none to give the badge camera; world badges take the scene's light");
				return false;
			}
			if ((main.cullingMask & (1 << _badgeLayer)) != 0)
			{
				main.cullingMask &= ~(1 << _badgeLayer);
				_maskedCamera = main;
			}

			var onlyVisible = OnlyVisible;
			if (onlyVisible && !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat))
			{
				Log.LogWarning("badges: no float render texture for the scene depth; world badges take the scene's light");
				return false;
			}

			_flatMaterial = new Material(shader);
			if (onlyVisible) _flatMaterial.EnableKeyword("SOFTPARTICLES_ON");
			if (_flatMaterial.HasProperty("_InvFade")) _flatMaterial.SetFloat("_InvFade", FlatFade);
			// The legacy particle shaders output 2 * vertex color * tint; a half tint makes that the
			// plain color.  A Color property is converted from sRGB in a linear project, so the
			// value set is the one that converts to a half there.
			var half = QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.LinearToGammaSpace(0.5f) : 0.5f;
			if (_flatMaterial.HasProperty("_TintColor")) _flatMaterial.SetColor("_TintColor", new Color(half, half, half, 0.5f));

			var go = new GameObject("GrabMaterials_BadgeCamera", typeof(Camera));
			go.transform.SetParent(main.transform, false);
			_badgeCamera = go.GetComponent<Camera>();
			if (onlyVisible)
			{
				_depthCamera = main;
				if ((main.depthTextureMode & DepthTextureMode.Depth) == 0)
				{
					main.depthTextureMode |= DepthTextureMode.Depth;
					_addedDepthMode = true;
				}
				_copyDepth = new CommandBuffer { name = "GrabMaterials badge depth copy" };
				_bindDepth = new CommandBuffer { name = "GrabMaterials badge depth bind" };
				main.AddCommandBuffer(CopyDepthAt, _copyDepth);
				_badgeCamera.AddCommandBuffer(BindDepthAt, _bindDepth);
			}
			SyncBadgeCamera(main);
			var depth = onlyVisible
				? $"; scene depth copied from the main camera ({main.actualRenderingPath}{(_addedDepthMode ? ", depth texture added" : "")})"
				: "; Only Visible Containers is off, so nothing covers them";
			Log.LogInfo($"badges: flat world badges on layer {_badgeLayer}{(_maskedCamera != null ? ", taken from the main camera" : "")}, through {shader.name}{depth}");
			return true;
		}

		// Flat world badges live on their own layer and use the soft-particle material.
		private static void ApplyFlat(GameObject go)
		{
			if (_badgeCamera == null) return;
			go.layer = _badgeLayer;
			var graphic = go.GetComponent<Graphic>();
			if (graphic != null) graphic.material = _flatMaterial;
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

				// Drawn in the world, the depth test does this per pixel.
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
			// A scene change destroys the root and the badge camera but not the statics behind
			// them: the depth copy, the command buffers, the material, the probe's verdict.  A
			// setup built over those found a copy of the right size and recorded nothing into
			// its new command buffers, so the copy kept the old scene's last frame and every
			// badge passed the depth test, or failed it, on every login (2026-11-14).  Start clean.
			if (_flatMaterial != null || _copyDepth != null || _depthCopy != null) Rebuild();
			// The font comes from Jotunn either way, and the overlay lives on its back canvas.
			if (GUIManager.Instance == null || GUIManager.CustomGUIBack == null) return;

			// The canvas is rebuilt on every scene change, taking our objects with it.
			_pool.Clear();
			_inUse.Clear();
			_colliders.Clear();

			// What hides an overlay badge.  Characters are not in it: a badge over a chest behind
			// the player stays up in front of them, which is what "Draw Badges In Front"
			// promises; the world mode is the one that lets a body cover a badge.
			_occluders = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
			if (_circle == null) _circle = MakeCircle(64);

			_builtInFront = InFront;
			_builtLit = Lit;
			_builtOnlyVisible = OnlyVisible;
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
				var main = Utils.GetMainCamera();
				if (main == null) return;
				_root = new GameObject("GrabMaterials_ContainerBadges_World", typeof(RectTransform), typeof(Canvas));
				_canvas = _root.GetComponent<Canvas>();
				_canvas.renderMode = RenderMode.WorldSpace;
				_canvas.worldCamera = main;
				_rootRect = _root.GetComponent<RectTransform>();
				_rootRect.position = Vector3.zero;
				_rootRect.sizeDelta = Vector2.zero;
				if (!_builtLit && SetupFlat(main)) _root.layer = _badgeLayer;
			}
		}

		private static Badge Create()
		{
			var root = new GameObject("Badge", typeof(RectTransform));
			root.transform.SetParent(_root.transform, false);
			var rect = root.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);

			ApplyFlat(root);
			var ring = MakeImage("Ring", root.transform, DarkRing);
			ring.rectTransform.anchorMin = Vector2.zero;
			ring.rectTransform.anchorMax = Vector2.one;
			ring.rectTransform.offsetMin = Vector2.zero;
			ring.rectTransform.offsetMax = Vector2.zero;

			var fill = MakeImage("Fill", root.transform, Color.white);
			fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

			var labelGo = new GameObject("Label", typeof(RectTransform), typeof(FlatText), typeof(Outline));
			labelGo.transform.SetParent(root.transform, false);
			ApplyFlat(labelGo);
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
				ApplyFlat(go);
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
			ApplyFlat(go);
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
