using System.Collections.Generic;
using System.IO;
using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// Photographs each finished clone -- tinted, stretched, penned -- for its Hammer icon, so the
    /// build menu shows a narrow red-striped Fast Longship and a fat green Big Busse rather than
    /// eight identical longships.  Jotunn's RenderManager does the picture: it copies the target,
    /// strips it to transforms and mesh renderers, centers it and shoots it with a near-orthographic
    /// camera.  The target must be active for that, and a live copy of a ship would run ZNetView's
    /// and Ship's Awake in the main menu, so the copy is staged inactive, stripped of every script,
    /// and only then switched on.
    ///
    /// Each icon is also written to BepInEx/config/TheGreatestShips/icons/&lt;prefab&gt;.png, which is
    /// how a recolored ship's icon can be checked without opening the Hammer.
    /// </summary>
    internal static class ShipIcons
    {
        private const int Size = 128;

        // Yaw first, then tilt about the world X axis: the bow swings 60 degrees off the camera
        // toward the right of the frame, and the deck tips 20 degrees toward the viewer.
        private static readonly Quaternion View = Quaternion.Euler(20f, 0f, 0f) * Quaternion.Euler(0f, -60f, 0f);

        internal static Sprite Render(ShipDefinition def, GameObject clone)
        {
            GameObject stage = null, copy = null;
            try
            {
                stage = new GameObject("TheGreatestShips_icon_stage");
                stage.SetActive(false);
                copy = Object.Instantiate(clone, stage.transform);
                copy.name = clone.name;

                var ship = copy.GetComponent<Ship>();
                var sail = ship != null ? ship.m_sailObject : null;
                StripToGeometry(copy);
                if (sail != null) sail.SetActive(true);

                copy.transform.SetParent(null);
                copy.transform.position = new Vector3(0f, -4000f, 0f); // out of anyone's sight
                copy.SetActive(true);

                var sprite = RenderManager.Instance.Render(new RenderManager.RenderRequest(copy)
                {
                    Width = Size, Height = Size, Rotation = View,
                    ParticleSimulationTime = -1f, // no bow splash or wake in the picture
                });
                if (sprite == null)
                {
                    Jotunn.Logger.LogWarning($"[TheGreatestShips] {def.DisplayName}: icon render returned nothing; keeping the {def.BaseName}'s icon.");
                    return null;
                }
                Save(def, sprite);
                return sprite;
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[TheGreatestShips] {def.DisplayName}: icon render failed, keeping the {def.BaseName}'s icon: {e.Message}");
                return null;
            }
            finally
            {
                if (copy  != null) Object.Destroy(copy);
                if (stage != null) Object.Destroy(stage);
            }
        }

        // Every script goes (ZNetView last: others RequireComponent it), and the rigidbody with
        // them.  LOD groups go too -- Jotunn removes them anyway -- but with the group gone every
        // LOD's renderers would draw on top of each other, so the far ones are switched off first.
        private static void StripToGeometry(GameObject root)
        {
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length < 2) continue;
                var nearest = new HashSet<Renderer>(lods[0].renderers);
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null && !nearest.Contains(r)) r.enabled = false; // a renderer LOD0 also uses stays on
            }

            // The water mask is a hull-shaped volume that only writes depth in the game, to keep
            // the sea out of the boat; the Karve's "shadow" is a blob under it.  Neither is boat.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = renderer.name.ToLowerInvariant();
                if (n.Contains("watermask") || n.Contains("shadow") || n.StartsWith("vfx") || n.Contains("splash") || n == "trail")
                    renderer.enabled = false;
            }

            var scripts = new List<MonoBehaviour>(root.GetComponentsInChildren<MonoBehaviour>(true));
            scripts.Sort((a, b) => (a is ZNetView ? 1 : 0) - (b is ZNetView ? 1 : 0));
            foreach (var script in scripts)
                if (script != null) Object.DestroyImmediate(script);
            foreach (var joint in root.GetComponentsInChildren<Joint>(true))
                Object.DestroyImmediate(joint);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(body);
        }

        private static void Save(ShipDefinition def, Sprite sprite)
        {
            try
            {
                var dir = Path.Combine(BepInEx.Paths.ConfigPath, "TheGreatestShips", "icons");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, def.PrefabName + ".png"), sprite.texture.EncodeToPNG());
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[TheGreatestShips] {def.DisplayName}: could not save its icon: {e.Message}");
            }
        }
    }
}
