using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OneClickLaunch
{
    /// <summary>
    /// The main-menu side: builds the Continue buttons, records every launch, and drives the
    /// vanilla start path when a button is clicked. Everything past "set these fields and call
    /// OnWorldStart / JoinServer" is vanilla's own code, including its version, backup, cloud
    /// and login dialogs.
    /// </summary>
    internal static class MenuPatches
    {
        // Private FejdStartup state the vanilla start path reads. Reached through Harmony's field
        // accessors, which work at runtime where the publicized assemblies do not (Mono enforces
        // access on the real assembly and throws FieldAccessException).
        private static readonly AccessTools.FieldRef<FejdStartup, World>               WorldRef            = AccessTools.FieldRefAccess<FejdStartup, World>("m_world");
        private static readonly AccessTools.FieldRef<FejdStartup, List<World>>         WorldsRef           = AccessTools.FieldRefAccess<FejdStartup, List<World>>("m_worlds");
        private static readonly AccessTools.FieldRef<FejdStartup, List<PlayerProfile>> ProfilesRef         = AccessTools.FieldRefAccess<FejdStartup, List<PlayerProfile>>("m_profiles");
        private static readonly AccessTools.FieldRef<FejdStartup, int>                 ProfileIndexRef     = AccessTools.FieldRefAccess<FejdStartup, int>("m_profileIndex");
        private static readonly AccessTools.FieldRef<FejdStartup, bool>                StartingWorldRef    = AccessTools.FieldRefAccess<FejdStartup, bool>("m_startingWorld");
        private static readonly AccessTools.FieldRef<FejdStartup, ServerJoinData>      JoinServerRef       = AccessTools.FieldRefAccess<FejdStartup, ServerJoinData>("m_joinServer");
        private static readonly AccessTools.FieldRef<FejdStartup, ServerJoinData>      QueuedJoinServerRef = AccessTools.FieldRefAccess<FejdStartup, ServerJoinData>("m_queuedJoinServer");
        private static readonly AccessTools.FieldRef<FejdStartup, Button[]>            MenuButtonsRef      = AccessTools.FieldRefAccess<FejdStartup, Button[]>("m_menuButtons");

        // FejdStartup.ServerPassword is the -password command-line value; ZNet submits it on the
        // server's behalf the moment the server asks for one. Its setter is private.
        private static readonly System.Reflection.MethodInfo ServerPasswordSetter = AccessTools.PropertySetter(typeof(FejdStartup), "ServerPassword");

        private static readonly List<Button> s_buttons = new List<Button>();

        // True from the moment a launch is on its way to the main scene until the menu comes back.
        private static bool s_launching;

        // The server entry of the launch in progress, so a password typed into ZNet's dialog
        // can be attached to it.
        private static string s_pendingServerKey;

        // The entry whose stored password this mod submitted itself, and what ServerPassword
        // held before, so it can be put back and so a rejected password can be forgotten.
        private static string s_autoPasswordKey;
        private static string s_previousServerPassword;
        private static bool   s_serverPasswordOverridden;
        private static ZNet.ConnectionStatus s_statusOnReturn;

        // The vanilla Start button's "different world" question was answered yes.
        private static bool s_worldConfirmed;

        // ------------------------------------------------------------------ menu build ----

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static class FejdStartup_Start_Patch
        {
            // Before vanilla's Start reads and clears the connection error, note why we are back.
            private static void Prefix()
            {
                s_statusOnReturn = ZNet.GetConnectionStatus();
            }

            private static void Postfix(FejdStartup __instance)
            {
                s_launching = false;
                s_pendingServerKey = null;
                RestoreServerPassword();
                if (s_autoPasswordKey != null)
                {
                    if (s_statusOnReturn == ZNet.ConnectionStatus.ErrorPassword)
                    {
                        // The password we entered for the player was rejected; forget it so the
                        // next click asks instead of failing the same way again.
                        History.SetPassword(s_autoPasswordKey, null);
                        OneClickLaunchMod.Log.LogInfo("The remembered server password was rejected and has been forgotten.");
                    }
                    s_autoPasswordKey = null;
                }
                s_worldConfirmed = false;

                // Back at the menu is when a setting edited while playing (button count, labels,
                // margin) should take effect, not at the next game start.
                try
                {
                    OneClickLaunchMod.Instance?.Config.Reload();
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogWarning($"Could not reload the config: {ex.Message}");
                }

                if (!s_watchingSettings && OneClickLaunchMod.Instance != null)
                {
                    s_watchingSettings = true;
                    OneClickLaunchMod.Instance.Config.SettingChanged += OnSettingChanged;
                }

                try
                {
                    BuildButtons(__instance);
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogError($"Could not add the Continue buttons: {ex}");
                }
                try
                {
                    MenuLift.Schedule(__instance);
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogWarning($"Could not lift the menu: {ex}");
                }
            }
        }

        private static Button   s_template;
        private static bool     s_watchingSettings;
        private static Button[] s_vanillaMenuButtons;
        private static GameObject s_divider;

        private static void BuildButtons(FejdStartup fs)
        {
            s_buttons.Clear();
            s_divider = null;
            if (fs.m_menuList == null) return;

            // The template must be a vanilla button, never one of ours: after a rebuild the old
            // ones are still in the list until the end of the frame, marked for destruction,
            // and a clone made from one of those is destroyed along with it.
            Button[] existing = fs.m_menuList.GetComponentsInChildren<Button>(true);
            s_template = existing.FirstOrDefault(b => !b.name.StartsWith("OneClickLaunch_", StringComparison.Ordinal));
            if (s_template == null)
            {
                OneClickLaunchMod.Log.LogWarning("The main menu has no buttons to copy; no Continue buttons added.");
                return;
            }
            s_vanillaMenuButtons = MenuButtonsRef(fs) ?? new Button[0];

            var entries = History.Entries.Take(OneClickLaunchMod.ButtonCount.Value).ToList();
            if (entries.Count == 0) return;

            Color color = ButtonColor();
            int index = 0;
            foreach (Entry entry in entries)
            {
                Entry captured = entry;
                Button button = MakeButton(s_template, "OneClickLaunch_" + index, captured.Label(), () => Launch(fs, captured), color);
                button.transform.SetSiblingIndex(index++);
                button.gameObject.AddComponent<RightClickRelay>().OnRightClick = () => HistoryPanel.Toggle(fs, s_template);
                s_buttons.Add(button);
            }
            if (OneClickLaunchMod.MoreButton.Value)
            {
                Button more = MakeButton(s_template, "OneClickLaunch_More", "More...", () => HistoryPanel.Toggle(fs, s_template), color);
                more.transform.SetSiblingIndex(index++);
                s_buttons.Add(more);
            }
            if (OneClickLaunchMod.Divider.Value)
                s_divider = MakeDivider(s_template, index++, color);

            // A controller starts on the first entry of m_menuButtons; make that the most recent
            // game, so a gamepad is one press from playing too.
            MenuButtonsRef(fs) = s_buttons.Concat(s_vanillaMenuButtons).ToArray();

            OneClickLaunchMod.Log.LogInfo($"Added {s_buttons.Count} Continue button(s) to the main menu.");
        }

        /// <summary>A setting changed, from the file or an in-game config manager: rebuild to match.</summary>
        private static void OnSettingChanged(object sender, BepInEx.Configuration.SettingChangedEventArgs e)
        {
            FejdStartup fs = FejdStartup.instance;
            if (fs == null || fs.m_menuList == null || s_template == null) return;
            try
            {
                Rebuild(fs);
                HistoryPanel.Refresh();
                OneClickLaunchMod.Log.LogInfo($"Applied the changed setting {e.ChangedSetting.Definition.Key}.");
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogWarning($"Could not apply the changed setting: {ex}");
            }
        }

        /// <summary>Tear the buttons down and build them again, after the history changed.</summary>
        internal static void Rebuild(FejdStartup fs)
        {
            foreach (Button b in s_buttons)
                if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            if (s_divider != null) UnityEngine.Object.Destroy(s_divider);
            if (s_vanillaMenuButtons != null) MenuButtonsRef(fs) = s_vanillaMenuButtons;
            BuildButtons(fs);
            MenuLift.Reapply(fs);
        }

        /// <summary>
        /// A copy of a vanilla menu button with our text and click. The template's click is wired
        /// in the prefab, and RemoveAllListeners leaves such persistent listeners alone, so they
        /// are switched off one by one; otherwise every copy would also open vanilla's character
        /// screen on top of its own job.
        /// </summary>
        internal static Button MakeButton(Button template, string name, string text, UnityAction onClick, Color? color)
        {
            Button button = UnityEngine.Object.Instantiate(template, template.transform.parent);
            button.name = name;
            button.gameObject.SetActive(true);
            button.interactable = true;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                // As a rich-text tag rather than the label's color property: the menu button's
                // hover animation and the game's ButtonTextColor script both rewrite that
                // property every frame, and a tag is out of their reach.
                label.text = text;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                if (color.HasValue)
                {
                    var tint = button.gameObject.AddComponent<RestingTint>();
                    tint.Label = label;
                    tint.Plain = text;
                    tint.Colored = $"<color=#{ColorUtility.ToHtmlStringRGBA(color.Value)}>{text}</color>";
                    tint.Apply(hovered: false);
                }
            }

            Button.ButtonClickedEvent click = button.onClick;
            for (int p = 0; p < click.GetPersistentEventCount(); p++)
                click.SetPersistentListenerState(p, UnityEventCallState.Off);
            click.RemoveAllListeners();
            click.AddListener(onClick);

            return button;
        }

        internal static Color ButtonColor()
        {
            string text = OneClickLaunchMod.ButtonColorSetting.Value?.Trim();
            if (!string.IsNullOrEmpty(text) && ColorUtility.TryParseHtmlString(text, out Color c)) return c;
            return Color.white;
        }

        /// <summary>A thin line between our buttons and vanilla's, in the same list.</summary>
        private static GameObject MakeDivider(Button template, int index, Color color)
        {
            var go = new GameObject("OneClickLaunch_Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(template.transform.parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, 0.45f);
            image.raycastTarget = false;
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 2f;
            element.flexibleHeight = 0f;
            var rect = go.transform as RectTransform;
            var templateRect = template.transform as RectTransform;
            rect.sizeDelta = new Vector2(templateRect != null ? templateRect.rect.width * 0.8f : 200f, 2f);
            go.transform.SetSiblingIndex(index);
            return go;
        }

        // --------------------------------------------------------------------- launch ----

        internal static void Launch(FejdStartup fs, Entry entry)
        {
            if (s_launching || UnifiedPopup.IsVisible()) return;
            HistoryPanel.Close();
            try
            {
                if (!SelectCharacter(fs, entry)) return;
                if (entry.IsWorld) StartWorld(fs, entry);
                else JoinServer(fs, entry);
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogError($"Continue failed: {ex}");
                Warn("Continue failed", "Something went wrong; see the log. The regular Start button still works.");
            }
        }

        /// <summary>Makes the entry's character the selected one, exactly as clicking it in the character screen would.</summary>
        private static bool SelectCharacter(FejdStartup fs, Entry entry)
        {
            ref List<PlayerProfile> profiles = ref ProfilesRef(fs);
            if (profiles == null) profiles = SaveSystem.GetAllPlayerProfiles();

            int index = -1;
            for (int i = 0; i < profiles.Count && index < 0; i++)
                if (profiles[i].GetFilename() == entry.profileFile && (int)profiles[i].m_fileSource == entry.profileSource) index = i;
            // Moved between cloud and local since: the name is what matters.
            for (int i = 0; i < profiles.Count && index < 0; i++)
                if (profiles[i].GetFilename() == entry.profileFile) index = i;
            if (index < 0)
            {
                Warn("Character not found", $"{entry.profileName} ({entry.profileFile}) is no longer in the character list.");
                return false;
            }

            PlayerProfile profile = profiles[index];
            ProfileIndexRef(fs) = index;
            PlatformPrefs.SetString("profile", profile.GetFilename());
            Game.SetProfile(profile.GetFilename(), profile.m_fileSource);
            return true;
        }

        private static void StartWorld(FejdStartup fs, Entry entry)
        {
            List<World> worlds = SaveSystem.GetWorldList();
            WorldsRef(fs) = worlds;
            World world = worlds.FirstOrDefault(w => w.m_name == entry.worldName && (int)w.m_fileSource == entry.worldSource)
                       ?? worlds.FirstOrDefault(w => w.m_name == entry.worldName);
            if (world == null)
            {
                Warn("World not found", $"The world {entry.worldName} is no longer in the world list.");
                return;
            }

            WorldRef(fs) = world;
            // The hosting options the world was last started with. WithoutNotify: the open-server
            // toggle's listener runs vanilla's privilege checks and can pop a dialog of its own.
            fs.m_publicServerToggle.SetIsOnWithoutNotify(entry.publicServer);
            fs.m_openServerToggle.SetIsOnWithoutNotify(entry.openServer);
            fs.m_crossplayServerToggle.SetIsOnWithoutNotify(entry.crossplay);
            SetHostPassword(fs, entry.openServer && entry.password != null ? entry.password : "");

            // Vanilla from here: the version, backup, cloud and login dialogs, then the fade and
            // the load. If a dialog takes over instead of starting, the menu stays usable.
            fs.OnWorldStart();
        }

        private static void JoinServer(FejdStartup fs, Entry entry)
        {
            ServerJoinData data = BuildJoinData(entry);
            if (!data.IsValid)
            {
                Warn("Server not found", $"The saved address for {entry.Label()} could not be read.");
                return;
            }

            JoinServerRef(fs) = data;
            QueuedJoinServerRef(fs) = ServerJoinData.None;

            if (OneClickLaunchMod.RememberPasswords.Value && !string.IsNullOrEmpty(entry.password))
            {
                s_previousServerPassword = FejdStartup.ServerPassword;
                s_serverPasswordOverridden = true;
                ServerPasswordSetter.Invoke(null, new object[] { entry.password });
                s_autoPasswordKey = entry.Key;
            }

            // Vanilla from here: privilege and version checks, host lookup, then the fade.
            fs.JoinServer();
        }

        private static ServerJoinData BuildJoinData(Entry entry)
        {
            switch (entry.serverType)
            {
                case Entry.ServerDedicated:
                    return new ServerJoinData(new ServerJoinDataDedicated(entry.serverAddress));
                case Entry.ServerSteam:
                    return ulong.TryParse(entry.serverAddress, out ulong steamId)
                        ? SteamJoinData(steamId)
                        : ServerJoinData.None;
                case Entry.ServerPlayFab:
                    return new ServerJoinData(new ServerJoinDataPlayFabUser(entry.serverAddress));
                default:
                    return ServerJoinData.None;
            }
        }

        // --------------------------------------------------------------------- record ----

        [HarmonyPatch(typeof(FejdStartup), "TransitionToMainScene")]
        private static class FejdStartup_TransitionToMainScene_Patch
        {
            // Every way into a game ends here: the vanilla Start buttons, a Steam invite, a
            // command-line join and this mod's own buttons.
            private static void Prefix(FejdStartup __instance)
            {
                s_launching = true;
                foreach (Button b in s_buttons)
                    if (b != null) b.interactable = false;
                try
                {
                    Record(__instance);
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogWarning($"Could not remember this launch: {ex}");
                }
            }
        }

        private static void Record(FejdStartup fs)
        {
            List<PlayerProfile> profiles = ProfilesRef(fs);
            int index = ProfileIndexRef(fs);
            if (profiles == null || index < 0 || index >= profiles.Count) return;
            PlayerProfile profile = profiles[index];

            var entry = new Entry
            {
                profileFile   = profile.GetFilename(),
                profileSource = (int)profile.m_fileSource,
                profileName   = profile.GetName(),
            };
            bool remember = OneClickLaunchMod.RememberPasswords.Value;

            if (StartingWorldRef(fs))
            {
                World world = WorldRef(fs);
                if (world == null) return;
                entry.kind         = Entry.KindWorld;
                entry.worldName    = world.m_name;
                entry.worldSource  = (int)world.m_fileSource;
                entry.openServer   = fs.m_openServerToggle.isOn;
                entry.publicServer = fs.m_publicServerToggle.isOn;
                entry.crossplay    = fs.m_crossplayServerToggle.isOn;
                string password    = HostPassword(fs);
                entry.password     = remember && entry.openServer && !string.IsNullOrEmpty(password) ? password : null;
                s_pendingServerKey = null;
                History.Record(entry, carryPassword: false);
            }
            else
            {
                ServerJoinData join = JoinServerRef(fs);
                if (!join.IsValid) return;
                entry.kind = Entry.KindServer;
                switch (join.m_type)
                {
                    case ServerJoinDataType.Dedicated:
                        entry.serverType    = Entry.ServerDedicated;
                        entry.serverAddress = join.Dedicated.m_host + ":" + join.Dedicated.m_port;
                        break;
                    case ServerJoinDataType.SteamUser:
                        entry.serverType    = Entry.ServerSteam;
                        entry.serverAddress = SteamHostId(join);
                        break;
                    case ServerJoinDataType.PlayFabUser:
                        entry.serverType    = Entry.ServerPlayFab;
                        entry.serverAddress = join.PlayFabUser.m_remotePlayerId;
                        break;
                    default:
                        return;
                }
                if (string.IsNullOrEmpty(entry.serverAddress)) return;
                entry.serverName = ServerName(join);
                // The password comes later, typed into ZNet's dialog once the server asks.
                s_pendingServerKey = entry.Key;
                History.Record(entry, carryPassword: true);
            }
            OneClickLaunchMod.Log.LogInfo($"Remembered: {entry.Label()}");
        }

        private static ServerJoinData SteamJoinData(ulong steamId)
        {
            // ServerJoinDataSteamUser has a ulong constructor, but naming the type in a call
            // makes the compiler resolve its CSteamID overload too, which lives in the
            // Steamworks assembly this project does not reference. Reflection sidesteps that.
            object steamUser = AccessTools.Constructor(typeof(ServerJoinDataSteamUser), new[] { typeof(ulong) })
                ?.Invoke(new object[] { steamId });
            if (steamUser == null) return ServerJoinData.None;
            object join = AccessTools.Constructor(typeof(ServerJoinData), new[] { typeof(ServerJoinDataSteamUser) })
                ?.Invoke(new[] { steamUser });
            return join is ServerJoinData data ? data : ServerJoinData.None;
        }

        private static string SteamHostId(ServerJoinData join)
        {
            // CSteamID lives in the Steamworks assembly, which this project does not reference.
            // Its ToString is the raw 64-bit id, and ServerJoinDataSteamUser takes that back as a
            // ulong, so the id is carried as text and never as the type.
            object steamUser = AccessTools.Field(typeof(ServerJoinData), "m_steamUser")?.GetValue(join);
            if (steamUser == null) return null;
            object id = AccessTools.Field(steamUser.GetType(), "m_joinUserID")?.GetValue(steamUser);
            return id?.ToString();
        }

        private static string ServerName(ServerJoinData join)
        {
            try
            {
                ServerMatchmakingData data = MultiBackendMatchmaking.GetServerMatchmakingData(join);
                if (data.IsValid && !string.IsNullOrEmpty(data.m_serverName)) return data.m_serverName;
            }
            catch (Exception ex)
            {
                OneClickLaunchMod.Log.LogDebug($"No server name for {join}: {ex.Message}");
            }
            return null;
        }

        // ------------------------------------------------------------------ passwords ----

        [HarmonyPatch(typeof(ZNet), "OnPasswordEntered")]
        private static class ZNet_OnPasswordEntered_Patch
        {
            private static void Postfix(string pwd)
            {
                if (!OneClickLaunchMod.RememberPasswords.Value || s_pendingServerKey == null || string.IsNullOrEmpty(pwd)) return;
                History.SetPassword(s_pendingServerKey, pwd);
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_ClientHandshake")]
        private static class ZNet_RPC_ClientHandshake_Patch
        {
            // The handshake is where vanilla submits FejdStartup.ServerPassword. Once it has
            // done so, put the property back so our password does not leak into a later join.
            private static void Postfix()
            {
                RestoreServerPassword();
            }
        }

        private static void RestoreServerPassword()
        {
            if (!s_serverPasswordOverridden) return;
            s_serverPasswordOverridden = false;
            ServerPasswordSetter.Invoke(null, new object[] { s_previousServerPassword });
            s_previousServerPassword = null;
        }

        // ------------------------------------------------------ wrong-world question ----

        [HarmonyPatch(typeof(FejdStartup), "OnWorldStart")]
        private static class FejdStartup_OnWorldStart_Patch
        {
            // The vanilla Start button, about to load a character into a world that character
            // has never been in: ask first. A Continue button never trips this, since its pair
            // is in the history by definition.
            private static bool Prefix(FejdStartup __instance)
            {
                if (!OneClickLaunchMod.WarnOnUnknownWorld.Value) return true;
                if (s_worldConfirmed)
                {
                    s_worldConfirmed = false;
                    return true;
                }
                try
                {
                    if (StartingWorldRef(__instance)) return true;
                    World world = WorldRef(__instance);
                    List<PlayerProfile> profiles = ProfilesRef(__instance);
                    int index = ProfileIndexRef(__instance);
                    if (world == null || profiles == null || index < 0 || index >= profiles.Count) return true;
                    PlayerProfile profile = profiles[index];

                    // The game's own record of where this character has been: m_playerStats[0] is
                    // the lifetime total, keyed by world name.
                    Dictionary<string, float> known = profile.m_playerStats?[0]?.m_knownWorlds;
                    if (known == null || known.Count == 0) return true;  // a new character: anywhere is fine
                    if (known.ContainsKey(world.m_name)) return true;
                    if (History.HasPlayed(profile.GetFilename(), world.m_name)) return true;

                    string theirs = string.Join(", ", known.OrderByDescending(kv => kv.Value).Take(5).Select(kv => kv.Key));
                    UnifiedPopup.Push(new YesNoPopup(
                        "Different world",
                        $"{profile.GetName()} has never been in {world.m_name}.\nTheir worlds: {theirs}.\n\nStart anyway?",
                        () =>
                        {
                            UnifiedPopup.Pop();
                            s_worldConfirmed = true;
                            __instance.OnWorldStart();
                        },
                        UnifiedPopup.Pop,
                        localizeText: false));
                    return false;
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogWarning($"World check skipped: {ex}");
                    return true;
                }
            }
        }

        // -------------------------------------------------------------------- helpers ----

        // m_serverPassword is a GuiInputField (a TMP_InputField subclass) from gui_framework.dll,
        // another assembly this project does not reference; the field is read by name instead.
        private static readonly System.Reflection.FieldInfo ServerPasswordField = AccessTools.Field(typeof(FejdStartup), "m_serverPassword");

        private static string HostPassword(FejdStartup fs)
        {
            return (ServerPasswordField?.GetValue(fs) as TMP_InputField)?.text ?? "";
        }

        private static void SetHostPassword(FejdStartup fs, string password)
        {
            if (ServerPasswordField?.GetValue(fs) is TMP_InputField field) field.text = password;
        }

        private static void Warn(string header, string text)
        {
            UnifiedPopup.Push(new WarningPopup(header, text, UnifiedPopup.Pop, localizeText: false));
        }
    }
}
