namespace TheObituaries
{
    /// <summary>
    /// One obituary as it travels between clients: who died, who (if anyone) did it, and the
    /// line with the names left as {v} and {k} so every client can color them its own way.
    /// The victim's own game fills this in; everyone else only unpacks and shows it.
    /// </summary>
    internal sealed class Notice
    {
        private const byte Version = 1;

        public string Victim = "";
        public string Killer = "";          // "a 2-star Troll", "Fluffy the Wolf", "Marco", or "" for no killer
        public bool   KillerIsPlayer;
        public ZDOID  KillerId = ZDOID.None;   // so the killer's own client can say "You fragged"
        public string Template = "{v} died";

        public ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Version);
            pkg.Write(Victim ?? "");
            pkg.Write(Killer ?? "");
            pkg.Write(KillerIsPlayer);
            pkg.Write(KillerId);
            pkg.Write(Template ?? "");
            return pkg;
        }

        public static Notice Unpack(ZPackage pkg)
        {
            if (pkg == null) return null;
            byte version = pkg.ReadByte();
            if (version != Version) return null;   // a newer mod on the other end; skip rather than misread
            return new Notice
            {
                Victim         = pkg.ReadString(),
                Killer         = pkg.ReadString(),
                KillerIsPlayer = pkg.ReadBool(),
                KillerId       = pkg.ReadZDOID(),
                Template       = pkg.ReadString(),
            };
        }

        /// <summary>The line with plain names, for the log.</summary>
        public string Plain() => Fill(Victim, Killer);

        /// <summary>The line with the names colored per config, for the screen.</summary>
        public string Rich() => Fill(
            TheObituariesMod.Colored(Victim, TheObituariesMod.VictimColor),
            TheObituariesMod.Colored(Killer, TheObituariesMod.KillerColor));

        private string Fill(string victim, string killer)
        {
            string t = Template ?? "";
            return t.Replace("{v}", victim ?? "").Replace("{k}", killer ?? "");
        }
    }
}
