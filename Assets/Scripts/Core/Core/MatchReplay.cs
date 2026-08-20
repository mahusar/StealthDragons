using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public class MatchReplay
{
    public const string Tag = "XSTP|1";
    public const int MaxNameLength = 32;
    public const string Hero = "hero";

    private static readonly char[] NewLine = { (char)10 };

    private const char Return = (char)13;

    public struct Seat
    {
        public int index;
        public uint netId;
        public string username;
        public string publicKeyHex;
    }

    public string match = "";
    public string seed = "";
    public string mix = "";
    public string result = "";
    public string reason = "";

    public readonly List<Seat> seats = new List<Seat>();

    private readonly List<string> lines = new List<string>();
    private readonly Dictionary<uint, int> seatByPlayer = new Dictionary<uint, int>();
    private readonly Dictionary<uint, string> stableByCard = new Dictionary<uint, string>();
    private readonly Dictionary<string, uint> cardByStable = new Dictionary<string, uint>();
    private readonly List<int> playedPerSeat = new List<int>();

    public int Moves { get; private set; }

    public bool Sealed { get; private set; }

    public int SeatOf(uint playerNetId)
    {
        int seat;
        if (seatByPlayer.TryGetValue(playerNetId, out seat)) return seat;

        seat = seatByPlayer.Count;
        seatByPlayer[playerNetId] = seat;

        while (playedPerSeat.Count <= seat) playedPerSeat.Add(0);

        return seat;
    }

    public bool TrySeatAt(int index, out Seat found)
    {
        foreach (Seat seat in seats)
        {
            if (seat.index != index) continue;

            found = seat;
            return true;
        }

        found = new Seat();
        return false;
    }

    public string StableOf(uint cardNetId)
    {
        string stable;
        return stableByCard.TryGetValue(cardNetId, out stable) ? stable : "";
    }

    public uint LiveOf(string stable)
    {
        uint netId;
        return cardByStable.TryGetValue(stable, out netId) ? netId : 0;
    }

    public void RecordTurn(int turnCount, uint playerNetId)
    {
        if (Sealed) return;

        lines.Add("t=" + turnCount + ":" + SeatOf(playerNetId));
    }

    public void RecordPlay(uint playerNetId, int handIndex, string cardId, uint cardNetId)
    {
        if (Sealed) return;

        int seat = SeatOf(playerNetId);
        string stable = seat + ":" + playedPerSeat[seat];
        playedPerSeat[seat] = playedPerSeat[seat] + 1;

        if (cardNetId != 0)
        {
            stableByCard[cardNetId] = stable;
            cardByStable[stable] = cardNetId;
        }

        lines.Add("m=play " + handIndex + " " + Clean(cardId));
        Moves++;
    }

    public void RecordAttack(uint playerNetId, uint attackerNetId, uint targetNetId, bool targetIsPlayer)
    {
        if (Sealed) return;

        SeatOf(playerNetId);

        string attacker = StableOf(attackerNetId);
        if (attacker.Length == 0) attacker = "?";

        string target = targetIsPlayer ? Hero + ":" + SeatOf(targetNetId) : StableOf(targetNetId);
        if (target.Length == 0) target = "?";

        lines.Add("m=attack " + attacker + " " + target);
        Moves++;
    }

    public void RecordEnd(uint playerNetId)
    {
        if (Sealed) return;

        SeatOf(playerNetId);
        lines.Add("m=end");
        Moves++;
    }

    public void RecordCheck(int turnCount, string state)
    {
        if (Sealed) return;

        lines.Add("k=" + turnCount + ":" + ShortHash(state));
    }

    public void Seal()
    {
        Sealed = true;
    }

    public string Canonical()
    {
        StringBuilder sb = new StringBuilder();

        sb.Append(Tag).Append('\n');
        sb.Append("match=").Append(Clean(match)).Append('\n');
        sb.Append("seed=").Append(Clean(seed)).Append('\n');
        sb.Append("mix=").Append(Clean(mix)).Append('\n');

        foreach (Seat seat in seats)
            sb.Append("seat=").Append(seat.index)
              .Append(':').Append(seat.netId)
              .Append(':').Append(Clean(seat.publicKeyHex))
              .Append(':').Append(CleanName(seat.username))
              .Append('\n');

        foreach (string line in lines) sb.Append(line).Append('\n');

        sb.Append("result=").Append(Clean(result)).Append('\n');
        sb.Append("reason=").Append(Clean(reason));

        return sb.ToString();
    }

    public string BodyDigestHex()
    {
        StringBuilder sb = new StringBuilder();

        foreach (string line in lines) sb.Append(line).Append((char)10);

        using (SHA256 sha = SHA256.Create())
            return CardShuffle.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
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

    public struct Move
    {
        public int turn;
        public int seat;
        public string verb;
        public int handIndex;
        public string cardId;
        public string attacker;
        public string target;
    }

    public readonly List<Move> playback = new List<Move>();

    public readonly Dictionary<int, string> checks = new Dictionary<int, string>();

    public static MatchReplay Parse(string canonical)
    {
        if (string.IsNullOrEmpty(canonical)) return null;

        MatchReplay replay = new MatchReplay();
        bool tagged = false;
        int turn = 0;
        int seat = 0;

        foreach (string raw in canonical.Split(NewLine))
        {
            string line = raw.TrimEnd(Return);
            if (line.Length == 0) continue;

            if (line == Tag) { tagged = true; continue; }

            int split = line.IndexOf('=');
            if (split <= 0) return null;

            string key = line.Substring(0, split);
            string value = line.Substring(split + 1);

            switch (key)
            {
                case "match": replay.match = value; break;
                case "seed": replay.seed = value; break;
                case "mix": replay.mix = value; break;
                case "result": replay.result = value; break;
                case "reason": replay.reason = value; break;

                case "seat":
                    Seat parsed;
                    if (!ParseSeat(value, out parsed)) return null;
                    replay.seats.Add(parsed);
                    break;

                case "t":
                    if (!ParseTurn(value, out turn, out seat)) return null;
                    replay.lines.Add(line);
                    break;

                case "k":
                    int checkTurn;
                    string digest;
                    if (!ParseCheck(value, out checkTurn, out digest)) return null;
                    replay.checks[checkTurn] = digest;
                    replay.lines.Add(line);
                    break;

                case "m":
                    Move move;
                    if (!ParseMove(value, turn, seat, out move)) return null;
                    replay.playback.Add(move);
                    replay.lines.Add(line);
                    break;

                default: return null;
            }
        }

        return tagged ? replay : null;
    }

    private static bool ParseSeat(string value, out Seat seat)
    {
        seat = new Seat();

        string[] bits = value.Split(new[] { ':' }, 4);
        if (bits.Length != 4) return false;

        if (!int.TryParse(bits[0], out seat.index)) return false;
        if (!uint.TryParse(bits[1], out seat.netId)) return false;

        seat.publicKeyHex = bits[2];
        seat.username = bits[3];

        return true;
    }

    private static bool ParseTurn(string value, out int turn, out int seat)
    {
        turn = 0;
        seat = 0;

        string[] bits = value.Split(':');
        if (bits.Length != 2) return false;

        return int.TryParse(bits[0], out turn) && int.TryParse(bits[1], out seat);
    }

    private static bool ParseCheck(string value, out int turn, out string digest)
    {
        turn = 0;
        digest = "";

        string[] bits = value.Split(':');
        if (bits.Length != 2) return false;

        digest = bits[1];
        return int.TryParse(bits[0], out turn);
    }

    private static bool ParseMove(string value, int turn, int seat, out Move move)
    {
        move = new Move { turn = turn, seat = seat, handIndex = -1, cardId = "", attacker = "", target = "" };

        string[] bits = value.Split(' ');
        if (bits.Length == 0) return false;

        move.verb = bits[0];

        if (move.verb == "end") return bits.Length == 1;

        if (move.verb == "play")
        {
            if (bits.Length < 2 || !int.TryParse(bits[1], out move.handIndex)) return false;
            if (bits.Length > 2) move.cardId = bits[2];
            return true;
        }

        if (move.verb == "attack")
        {
            if (bits.Length != 3) return false;
            move.attacker = bits[1];
            move.target = bits[2];
            return true;
        }

        return false;
    }

    private static string ShortHash(string text)
    {
        using (SHA256 sha = SHA256.Create())
            return CardShuffle.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Substring(0, 16);
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
