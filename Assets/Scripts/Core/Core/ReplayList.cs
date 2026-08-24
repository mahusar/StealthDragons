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
        public Dictionary<string, string> decks;
        public bool decksProven;
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
              .Append(entry.decksProven ? " <color=#7FD98A><size=85%>decks proven</size></color>" : "")
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
        if (entry.decks != null && entry.decks.Count > 0)
        {
            foreach (string wire in entry.decks.Keys)
            {
                List<CardInfo> composition;
                string unusable;

                if (!Decklist.Parse(wire, out composition, out unusable)) return Standing.Outdated;

                if (CardFingerprint.Of(composition) != entry.decks[wire]) return Standing.Outdated;
            }

            return Standing.Plays;
        }

        if (string.IsNullOrEmpty(entry.cards)) return Standing.Unverified;
        if (mine.Length == 0) return Standing.Unverified;

        return entry.cards == mine ? Standing.Plays : Standing.Outdated;
    }

    public const string OutdatedFolder = "replays-outdated";

    public static string OutdatedPath()
    {
        return Path.Combine(Application.persistentDataPath, OutdatedFolder);
    }

    public static int OutdatedCount()
    {
        List<string> stale;

        return Stale(out stale) ? stale.Count : 0;
    }

    public static int Prune(out string trouble)
    {
        trouble = "";

        List<string> stale;
        if (!Stale(out stale)) return 0;
        if (stale.Count == 0) return 0;

        string into = OutdatedPath();
        int moved = 0;

        try
        {
            Directory.CreateDirectory(into);
        }
        catch (Exception e)
        {
            trouble = "the folder for old matches could not be made (" + e.GetType().Name + ")";
            return 0;
        }

        foreach (string path in stale)
        {
            try
            {
                string landing = Path.Combine(into, Path.GetFileName(path));

                if (File.Exists(landing)) File.Delete(landing);

                File.Move(path, landing);
                moved++;
            }
            catch (Exception e)
            {
                trouble = Path.GetFileName(path) + " could not be moved (" + e.GetType().Name + ")";
            }
        }

        if (moved > 0)
        {
            knownFingerprint = "";
            Debug.Log("ReplayList: moved " + moved + " outdated match(es) to " + into + ".");
        }

        return moved;
    }

    private static bool Stale(out List<string> stale)
    {
        stale = new List<string>();

        string mine = Fingerprint();
        if (mine.Length == 0) return false;

        string folder = MatchReplayStore.FolderPath();

        try
        {
            if (!Directory.Exists(folder)) return false;

            foreach (string path in Directory.GetFiles(folder, "*.txt"))
            {
                string cards = CardsOf(path);

                if (cards.Length == 0) continue;
                if (cards == mine) continue;

                stale.Add(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ReplayList: the replay folder could not be scanned (" + e.GetType().Name + ").");
            return false;
        }

        return true;
    }

    private static string CardsOf(string path)
    {
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("cards=", StringComparison.Ordinal)) return line.Substring(6).Trim();
                if (line.StartsWith("t=", StringComparison.Ordinal)) return "";
            }
        }
        catch (Exception)
        {
        }

        return "";
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
            entry.decks = new Dictionary<string, string>();

            int proven = 0;

            foreach (MatchReplay.Seat carried in replay.seats)
            {
                if (string.IsNullOrEmpty(carried.decklist) || string.IsNullOrEmpty(carried.cards)) continue;

                entry.decks[carried.decklist] = carried.cards;

                if (MatchReplay.DeckProven(carried)) proven++;
            }

            entry.decksProven = proven > 0 && proven == replay.seats.Count;

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
