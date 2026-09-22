using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;

namespace OneClickLaunch
{
    /// <summary>
    /// Keeps remembered passwords out of plain sight. On Windows they are encrypted with DPAPI
    /// (ProtectedData, CurrentUser scope), so the history file holds ciphertext that only the
    /// same Windows account on the same machine can turn back into the password: a copied file,
    /// a backup or a shared profile carries nothing usable. Elsewhere (Linux, Steam Deck) the
    /// game's Mono falls back to its own managed protection keyed by a per-user key store, which
    /// is still tied to that user; and if neither is available the password is stored plain and
    /// the log says so once.
    ///
    /// ProtectedData lives in System.Security.dll, which the game ships but this project does
    /// not reference: it is reached by reflection so a missing assembly degrades instead of
    /// failing the whole plugin.
    ///
    /// Stored form: "dpapi:" + base64 for a protected value, "plain:" + text for an unprotected
    /// one. A value with neither prefix is from the first builds, which stored the bare text;
    /// it is read as plain and re-saved protected.
    /// </summary>
    internal static class PasswordVault
    {
        private const string ProtectedPrefix = "dpapi:";
        private const string PlainPrefix     = "plain:";

        // Ties the ciphertext to this mod: the same account could not decrypt it through some
        // other program's ProtectedData call without also knowing this.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DeathMonger.OneClickLaunch.password");

        private static bool s_probed;
        private static MethodInfo s_protect;
        private static MethodInfo s_unprotect;
        private static object s_currentUserScope;

        /// <summary>Set when a value stored without protection was read, so the file can be rewritten protected.</summary>
        internal static bool SawUnprotected { get; private set; }

        internal static bool Available
        {
            get
            {
                Probe();
                return s_protect != null;
            }
        }

        private static void Probe()
        {
            if (s_probed) return;
            s_probed = true;
            try
            {
                // A simple-name Type.GetType only searches assemblies already loaded; nothing in
                // the game loads System.Security before us, so load it, by name first and then
                // straight from the game's Managed folder.
                Assembly asm = null;
                try { asm = Assembly.Load("System.Security"); } catch (Exception) { }
                if (asm == null)
                {
                    string path = Path.Combine(Paths.ManagedPath, "System.Security.dll");
                    if (File.Exists(path)) asm = Assembly.LoadFrom(path);
                }
                Type data  = asm?.GetType("System.Security.Cryptography.ProtectedData", throwOnError: false);
                Type scope = asm?.GetType("System.Security.Cryptography.DataProtectionScope", throwOnError: false);
                if (data == null || scope == null)
                {
                    OneClickLaunchMod.Log.LogWarning("System.Security's ProtectedData is not available; remembered passwords are stored as plain text.");
                    return;
                }
                s_currentUserScope = Enum.Parse(scope, "CurrentUser");
                s_protect   = data.GetMethod("Protect",   new[] { typeof(byte[]), typeof(byte[]), scope });
                s_unprotect = data.GetMethod("Unprotect", new[] { typeof(byte[]), typeof(byte[]), scope });
                if (s_protect == null || s_unprotect == null)
                {
                    s_protect = s_unprotect = null;
                    OneClickLaunchMod.Log.LogWarning("ProtectedData has an unexpected shape; remembered passwords are stored as plain text.");
                }
            }
            catch (Exception ex)
            {
                s_protect = s_unprotect = null;
                OneClickLaunchMod.Log.LogWarning($"ProtectedData could not be loaded ({ex.Message}); remembered passwords are stored as plain text.");
            }
        }

        /// <summary>The form a password is written to the history file in.</summary>
        internal static string Protect(string password)
        {
            if (password == null) return null;
            if (Available)
            {
                try
                {
                    var cipher = (byte[])s_protect.Invoke(null, new[] { Encoding.UTF8.GetBytes(password), Entropy, s_currentUserScope });
                    return ProtectedPrefix + Convert.ToBase64String(cipher);
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogWarning($"Could not protect a password ({Unwrap(ex).Message}); storing it as plain text.");
                }
            }
            return PlainPrefix + password;
        }

        /// <summary>The password behind a stored value, or null when it cannot be recovered (another account or machine).</summary>
        internal static string Unprotect(string stored)
        {
            if (stored == null) return null;
            if (stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                if (!Available) return null;
                try
                {
                    byte[] cipher = Convert.FromBase64String(stored.Substring(ProtectedPrefix.Length));
                    var plain = (byte[])s_unprotect.Invoke(null, new[] { cipher, Entropy, s_currentUserScope });
                    return Encoding.UTF8.GetString(plain);
                }
                catch (Exception ex)
                {
                    OneClickLaunchMod.Log.LogInfo($"A remembered password could not be read ({Unwrap(ex).Message}); it was stored by another account or machine and will be asked for again.");
                    return null;
                }
            }
            SawUnprotected = true;
            return stored.StartsWith(PlainPrefix, StringComparison.Ordinal) ? stored.Substring(PlainPrefix.Length) : stored;
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
        }
    }
}
