using System;

namespace TheGreatestMap
{
    /// <summary>
    /// Registering a network call so that it says what went wrong.
    ///
    /// <para><b>Why this exists.</b> The game does catch an exception thrown out of a routed call
    /// -- ZRpc.HandlePackage wraps the whole thing -- and reports it with ZLog.Log, which is
    /// info-level on Unity's own channel. On the dedicated server that channel reaches no file
    /// anybody reads: LogOutput.log holds not one Unity line and valheim_server.log is a few
    /// hundred bytes of startup. So a handler could throw on every call, forever, and look
    /// exactly like a handler that was never called.</para>
    ///
    /// <para>That is not a hypothetical. The repair asked the server what stood at each of 344
    /// places and the server answered nothing, logged nothing and appeared not to have been
    /// asked. It had been asked every time; reading a private field threw FieldAccessException
    /// before the handler's first statement, and nothing anywhere wrote that down. Finding it
    /// took a patch on the game's own dispatcher to prove the call was even arriving.</para>
    ///
    /// <para>A handler registered through here cannot fail quietly: whatever it throws is logged
    /// with its stack, named by the call it came in on. Registration itself is guarded too,
    /// because the game stores handlers with Dictionary.Add, which throws on a duplicate -- so
    /// one name registered twice would otherwise take every later registration in that method
    /// down with it, silently.</para>
    /// </summary>
    internal static class Rpc
    {
        internal static void Register(ZRoutedRpc rpc, string name, Action<long> f)
        {
            Add(name, () => rpc.Register(name, (long sender) => Run(name, () => f(sender))));
        }

        internal static void Register<T>(ZRoutedRpc rpc, string name, Action<long, T> f)
        {
            Add(name, () => rpc.Register<T>(name, (sender, a) => Run(name, () => f(sender, a))));
        }

        internal static void Register<T, U>(ZRoutedRpc rpc, string name, Action<long, T, U> f)
        {
            Add(name, () => rpc.Register<T, U>(name, (sender, a, b) => Run(name, () => f(sender, a, b))));
        }

        /// <summary>Run a handler, and let nothing it throws go unsaid.</summary>
        private static void Run(string name, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogError($"[TheGreatestMap] {name} failed: {e}");
            }
        }

        /// <summary>Install a handler, saying so if it cannot be installed.</summary>
        private static void Add(string name, Action register)
        {
            try
            {
                register();
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogError(
                    $"[TheGreatestMap] Could not register {name}, so nothing will answer it: {e.Message}");
            }
        }
    }
}
