using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static class ReplayList
{
    public const int Most = 100;

    public struct Entry
    {
        public string digest;
        public DateTime when;
        public int turns;
        public int moves;
        public string winner;
        public string loser;
        public string cards;
    }

    public enum Standing
    {
        Plays,
        Unverified,
        Outdated,
    }

    public static List<Entry> Newest()
    {
        List<Entry> found = new List<Entry>();
        string folder = MatchReplayStore.FolderPath();

        string[] files;

        try
        {
            if (!Directory.Exists(folder)) return found;
            files = Directory.GetFiles(folder, "*.txt");
        }
        catch (Exception e)
        {
            Debug.LogWarning("ReplayList: the replay folder could not be read (" + e.GetType().Name + ").");
            return found;
        }

        List<FileInfo> newest = new List<FileInfo>();
        foreach (string path in files) newest.Add(new FileInfo(path));

        newest.Sort(delegate (FileInfo left, FileInfo right)
        {
            return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
        });

        foreach (FileInfo file in newest)
        {
            if (found.Count >= Most) break;

            Entry entry;
            if (Read(file, out entry)) found.Add(entry);
        }

        return found;
    }

    public static string Describe()
    {
        return Describe(Newest(), 0);
    }

    public static string Describe(List<Entry> entries, int highlight)
    {
        if (entries == null) entries = Newest();
        StringBuilder sb = new StringBuilder();

        if (entries.Count == 0)
        {
            sb.Append("No matches saved yet.\n\n");
            sb.Append("Every match you finish is saved here, and you can watch any of them back.");
            return sb.ToString();
        }

        sb.Append("<size=125%>Your matches</size>\n");

        int total = Count();
        if (total > entries.Count)
            sb.Append("showing the newest ").Append(entries.Count)
              .Append(" of ").Append(total).Append("\n");
        sb.Append("Click a match to load it, or type its number on the right.\n\n");

        string mine = Fingerprint();
        int outdated = 0;
        int unverified = 0;

        foreach (Entry counted in entries)
        {
            Standing standing = StandingOf(counted, mine);

            if (standing == Standing.Outdated) outdated++;
            else if (standing == Standing.Unverified) unverified++;
        }

        if (outdated > 0)
            sb.Append("<color=#FF6B5A>").Append(outdated)
              .Append(outdated == 1 ? " match was" : " matches were")
              .Append(" played with different cards and will not replay.</color>\n");

        if (unverified > 0)
            sb.Append("<color=#FFB340>").Append(unverified)
              .Append(unverified == 1 ? " match predates card stamping, so it may not replay."
                                      : " matches predate card stamping, so they may not replay.")
              .Append("</color>\n");

        if (outdated > 0 || unverified > 0) sb.Append('\n');

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];

            bool lit = (i + 1) == highlight;

            sb.Append("<link=\"").Append(i + 1).Append("\">");
            if (lit) sb.Append("<mark=#ffffff22>");

            sb.Append("<size=115%>").Append(i + 1).Append("</size>   ")
              .Append(entry.winner).Append(" beat ").Append(entry.loser).Append('\n');

            sb.Append("      ").Append(entry.turns).Append(" turns, ")
              .Append(entry.moves).Append(" moves   ")
              .Append(entry.when.ToLocalTime().ToString("d MMM HH:mm")).Append('\n');

            sb.Append("      <size=85%>").Append(entry.digest.Substring(0, 16)).Append("...</size>")
              .Append(Tag(StandingOf(entry, mine)))
              .Append(lit ? "</mark>" : "").Append("</link>\n\n");
        }

        return sb.ToString();
    }

    private static string knownFingerprint = "";

    public static string Fingerprint()
    {
        if (knownFingerprint.Length > 0) return knownFingerprint;

        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;

        if (manager == null) manager = UnityEngine.Object.FindAnyObjectByType<XSTDragonNetworkManager>();

        GameObject prefab = manager != null ? manager.playerPrefab : null;
        Deck deck = prefab != null ? prefab.GetComponent<Deck>() : null;

        if (deck == null)
        {
            Debug.LogWarning("ReplayList: no deck to fingerprint, so no match can be called playable.");
            return "";
        }

        knownFingerprint = CardFingerprint.Of(deck.StartingComposition());

        return knownFingerprint;
    }

    public static Standing StandingOf(Entry entry, string mine)
    {
        if (string.IsNullOrEmpty(entry.cards)) return Standing.Unverified;
        if (mine.Length == 0) return Standing.Unverified;

        return entry.cards == mine ? Standing.Plays : Standing.Outdated;
    }

    public static int Count()
    {
        try
        {
            string folder = MatchReplayStore.FolderPath();
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.txt").Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string Tag(Standing standing)
    {
        if (standing == Standing.Outdated)
            return "   <size=85%><color=#FF6B5A>OUTDATED</color></size>";

        if (standing == Standing.Unverified)
            return "   <size=85%><color=#FFB340>UNVERIFIED</color></size>";

        return "";
    }

    public static string DigestFor(int pick)
    {
        List<Entry> entries = Newest();

        if (pick < 1 || pick > entries.Count) return "";

        return entries[pick - 1].digest;
    }

    private static bool Read(FileInfo file, out Entry entry)
    {
        entry = new Entry();

        try
        {
            string digest = Path.GetFileNameWithoutExtension(file.Name);
            MatchReplay replay = MatchReplay.Parse(File.ReadAllText(file.FullName));

            if (replay == null || replay.seats.Count < 2) return false;

            entry.digest = digest;
            entry.when = file.LastWriteTimeUtc;
            entry.moves = replay.playback.Count;

            int last = 0;
            foreach (MatchReplay.Move move in replay.playback)
                if (move.turn > last) last = move.turn;

            entry.turns = last;

            MatchReplay.Seat first, second;
            replay.TrySeatAt(0, out first);
            replay.TrySeatAt(1, out second);

            bool firstWon = first.publicKeyHex == replay.result;

            entry.winner = Named(firstWon ? first : second);
            entry.loser = Named(firstWon ? second : first);
            entry.cards = replay.cards;

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Named(MatchReplay.Seat seat)
    {
        return string.IsNullOrEmpty(seat.username) ? "a seat" : seat.username;
    }
}
