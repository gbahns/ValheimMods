namespace TheObituaries
{
    /// <summary>
    /// One obituary as it travels between clients: who died, who (if anyone) did it, and the
    /// line with the names left as {v} and {k} so every client can color them its own way.
    /// The victim's own game fills this in (pronouns included); everyone else only unpacks
    /// and shows it.
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

        /// <summary>
        /// The line for the screen: the whole thing in the line color, the names in theirs,
        /// and optionally scaled (the chat window; the center message is big already).
        /// </summary>
        public string Rich(bool sized)
        {
            string line = Fill(
                TheObituariesMod.Colored(TheObituariesMod.Bold(Victim), TheObituariesMod.VictimColor),
                TheObituariesMod.Colored(TheObituariesMod.Bold(Killer), TheObituariesMod.KillerColor));
            line = TheObituariesMod.Colored(line, TheObituariesMod.LineColor);
            if (sized)
            {
                int pct = TheObituariesMod.TextSize.Value;
                if (pct != 100) line = "<size=" + pct + "%>" + line + "</size>";
            }
            return line;
        }

        private string Fill(string victim, string killer)
        {
            string t = Template ?? "";
            return t.Replace("{v}", victim ?? "").Replace("{k}", killer ?? "");
        }
    }
}
