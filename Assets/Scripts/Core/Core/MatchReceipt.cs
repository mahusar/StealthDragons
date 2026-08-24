using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public class MatchReceipt
{
    public const string Tag = "XSTR|1";
    public const int MaxNameLength = 32;

    public struct Seat
    {
        public uint netId;
        public bool bot;
        public string publicKeyHex;
        public string username;
    }

    public struct Deck
    {
        public uint netId;
        public string cards;
        public string commitment;
    }

    public string server = "";
    public string match = "";
    public long started;
    public long ended;
    public string seed = "";
    public string mix = "";
    public string result = "";
    public string reason = "";
    public string stake = "free";
    public string replay = "";

    public List<Seat> seats = new List<Seat>();

    public List<Deck> decks = new List<Deck>();

    public string Canonical()
    {
        List<string> lines = new List<string>
        {
            Tag,
            "server=" + Clean(server),
            "match=" + Clean(match),
            "started=" + started,
            "ended=" + ended,
            "seed=" + Clean(seed),
            "mix=" + Clean(mix),
            "result=" + Clean(result),
            "reason=" + Clean(reason),
            "stake=" + Clean(stake),
            "replay=" + Clean(replay)
        };

        foreach (Seat seat in seats)
            lines.Add("player=" + seat.netId
                      + ":" + (seat.bot ? "bot" : "human")
                      + ":" + Clean(seat.publicKeyHex)
                      + ":" + CleanName(seat.username));

        foreach (Deck deck in decks)
        {
            if (string.IsNullOrEmpty(deck.commitment)) continue;

            lines.Add("deck=" + deck.netId
                      + ":" + Clean(deck.cards)
                      + ":" + Clean(deck.commitment));
        }

        lines.Sort(StringComparer.Ordinal);

        return string.Join("\n", lines.ToArray());
    }

    public byte[] Digest()
    {
        using (SHA256 sha = SHA256.Create())
            return sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical()));
    }

    public string DigestHex()
    {
        return CardShuffle.Hex(Digest());
    }

    public bool SignedBy(Seat seat, string signatureHex)
    {
        return PlayerIdentity.Verify(seat.publicKeyHex, Digest(), signatureHex);
    }

    public bool FullySigned(IDictionary<string, string> signatures)
    {
        if (signatures == null || seats.Count == 0) return false;

        foreach (Seat seat in seats)
        {
            string signature;
            if (!signatures.TryGetValue(seat.publicKeyHex, out signature)) return false;
            if (!SignedBy(seat, signature)) return false;
        }

        return true;
    }

    public bool Contested
    {
        get
        {
            if (seats.Count < 2) return false;

            foreach (Seat seat in seats)
                if (seat.bot) return false;

            return true;
        }
    }

    public static MatchReceipt Parse(string canonical)
    {
        if (string.IsNullOrEmpty(canonical)) return null;

        MatchReceipt receipt = new MatchReceipt();
        bool tagged = false;

        foreach (string raw in canonical.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line == Tag) { tagged = true; continue; }

            int split = line.IndexOf('=');
            if (split <= 0) return null;

            string key = line.Substring(0, split);
            string value = line.Substring(split + 1);

            switch (key)
            {
                case "server": receipt.server = value; break;
                case "match": receipt.match = value; break;
                case "started": if (!long.TryParse(value, out receipt.started)) return null; break;
                case "ended": if (!long.TryParse(value, out receipt.ended)) return null; break;
                case "seed": receipt.seed = value; break;
                case "mix": receipt.mix = value; break;
                case "result": receipt.result = value; break;
                case "reason": receipt.reason = value; break;
                case "stake": receipt.stake = value; break;
                case "replay": receipt.replay = value; break;
                case "deck":
                    Deck deck;
                    if (!ParseDeckLine(value, out deck)) return null;
                    receipt.decks.Add(deck);
                    break;

                case "player":
                    Seat seat;
                    if (!ParseSeat(value, out seat)) return null;
                    receipt.seats.Add(seat);
                    break;
                default: return null;
            }
        }

        return tagged ? receipt : null;
    }

    private static bool ParseDeckLine(string value, out Deck deck)
    {
        deck = new Deck();

        string[] bits = value.Split(new[] { ':' }, 3);
        if (bits.Length != 3) return false;

        if (!uint.TryParse(bits[0], out deck.netId)) return false;

        deck.cards = bits[1];
        deck.commitment = bits[2];

        return deck.commitment.Length > 0;
    }

    public string CommitmentFor(uint netId)
    {
        foreach (Deck deck in decks)
            if (deck.netId == netId) return deck.commitment;

        return "";
    }

    private static bool ParseSeat(string value, out Seat seat)
    {
        seat = new Seat();

        string[] bits = value.Split(new[] { ':' }, 4);
        if (bits.Length != 4) return false;

        if (!uint.TryParse(bits[0], out seat.netId)) return false;
        if (bits[1] != "human" && bits[1] != "bot") return false;

        seat.bot = bits[1] == "bot";
        seat.publicKeyHex = bits[2];
        seat.username = bits[3];

        return seat.publicKeyHex.Length == PlayerIdentity.PublicKeyBytes * 2;
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        StringBuilder sb = new StringBuilder(value.Length);

        foreach (char c in value)
            if (c != '\n' && c != '\r' && !char.IsControl(c)) sb.Append(c);

        return sb.ToString();
    }

    private static string CleanName(string value)
    {
        string cleaned = Clean(value).Replace(':', ' ').Trim();

        if (cleaned.Length > MaxNameLength) cleaned = cleaned.Substring(0, MaxNameLength);

        return cleaned;
    }
}
