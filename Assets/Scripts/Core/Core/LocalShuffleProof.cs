using System.Collections.Generic;
using UnityEngine;

public static class LocalShuffleProof
{
    public struct PlayerDeal
    {
        public uint netId;
        public string username;
        public List<CardInfo> composition;
    }

    public struct Published
    {
        public string commitment;
        public string seedCommitments;
        public string contributions;
        public string deals;
        public string reveal;
    }

    public static string Result { get; private set; }
    public static bool Checked { get; private set; }
    public static bool Passed { get; private set; }

    public static void Watching()
    {
        Passed = false;
        Result = "Watching a replay - this match was played by someone else.";
    }
    public static string Committed { get; private set; }
    public static int Unverified { get; private set; }

    private static byte[] myContribution;
    private static uint myNetId;
    private static List<CardInfo> observedOrder;
    private static List<CardInfo> startingComposition;

    public static void Remember(string commitment, byte[] contribution)
    {
        Committed = commitment;
        myContribution = contribution;
        observedOrder = null;
        startingComposition = null;
        Checked = false;
        Passed = false;
        Result = "";
    }

    public static void RememberDeal(List<CardInfo> order, uint netId, List<CardInfo> composition)
    {
        if (observedOrder != null) return;
        if (order == null || order.Count == 0) return;

        observedOrder = new List<CardInfo>(order);
        startingComposition = new List<CardInfo>(composition);
        myNetId = netId;

        Debug.Log("LocalShuffleProof: recorded my dealt order, fingerprint " + CardShuffle.Fingerprint(observedOrder));
    }

    public static void Verify(Published published, IList<PlayerDeal> others)
    {
        Checked = true;
        Unverified = 0;

        string contributions = published.contributions;

        byte[] serverSeed = CardShuffle.FromHex(published.reveal);
        byte[] committed = CardShuffle.FromHex(published.commitment);

        if (serverSeed == null || committed == null)
        {
            Fail("the server published no usable commitment or reveal");
            return;
        }

        if (!CardShuffle.CommitmentHolds(serverSeed, committed))
        {
            Fail("the revealed seed is not the one the server committed to before the match");
            return;
        }

        if (myContribution != null && !ContributionPresent(contributions))
        {
            Fail("my own seed was left out of the shuffle");
            return;
        }

        if (!SeedCommitmentsHold(published.seedCommitments, contributions)) return;

        byte[] matchSeed = MatchSeedFrom(serverSeed, contributions);
        if (matchSeed == null)
        {
            Fail("the published seed contributions could not be read");
            return;
        }

        if (observedOrder != null && startingComposition != null)
        {
            if (!DealMatches(matchSeed, myNetId, startingComposition, CardShuffle.Fingerprint(observedOrder)))
            {
                Fail("the cards I was dealt are not the ones the committed seed produces");
                return;
            }
        }
        else
        {
            Unverified++;
            Debug.LogWarning("LocalShuffleProof: this client recorded no hand of its own to re-check.");
        }

        Dictionary<uint, string> attested = ParsePairs(published.deals);
        if (attested == null)
        {
            Fail("the published dealt-order attestations could not be read");
            return;
        }

        if (others != null)
        {
            foreach (PlayerDeal other in others)
            {
                string fingerprint;
                if (!attested.TryGetValue(other.netId, out fingerprint))
                {
                    Unverified++;
                    Debug.LogWarning("LocalShuffleProof: " + Describe(other) + " attested no dealt order, so that hand is unchecked.");
                    continue;
                }

                if (!DealMatches(matchSeed, other.netId, other.composition, fingerprint))
                {
                    Fail("the cards " + Describe(other) + " was dealt are not the ones the committed seed produces");
                    return;
                }
            }
        }

        if (Unverified > 0)
            Pass("the committed seed holds and every hand it could check matches, but " + Unverified + " hand(s) went unchecked");
        else
            Pass("every hand in this match is exactly what the committed seed produces, and my seed was mixed in");
    }

    private static bool SeedCommitmentsHold(string seedCommitments, string contributions)
    {
        Dictionary<uint, string> promised = ParsePairs(seedCommitments);
        if (promised == null)
        {
            Fail("the published seed commitments could not be read");
            return false;
        }

        Dictionary<uint, string> revealed = ParsePairs(contributions);
        if (revealed == null)
        {
            Fail("the published seed contributions could not be read");
            return false;
        }

        foreach (KeyValuePair<uint, string> entry in promised)
        {
            string revealedHex;
            if (!revealed.TryGetValue(entry.Key, out revealedHex))
            {
                Fail("a player committed a seed that the server then left out of the shuffle");
                return false;
            }

            byte[] seed = CardShuffle.FromHex(revealedHex);
            byte[] hash = CardShuffle.FromHex(entry.Value);

            if (seed == null || hash == null || !CardShuffle.CommitmentHolds(seed, hash))
            {
                Fail("a seed in the shuffle is not the one that player committed to");
                return false;
            }
        }

        return true;
    }

    private static bool DealMatches(byte[] matchSeed, uint netId, List<CardInfo> composition, string fingerprint)
    {
        if (composition == null || composition.Count == 0) return false;

        List<CardInfo> rebuilt = new List<CardInfo>(composition);
        CardShuffle.Shuffle(rebuilt, CardShuffle.PlayerSeed(matchSeed, netId));

        return CardShuffle.Fingerprint(rebuilt) == fingerprint;
    }

    public static Dictionary<uint, string> ParsePairs(string text)
    {
        Dictionary<uint, string> byId = new Dictionary<uint, string>();

        if (string.IsNullOrEmpty(text)) return byId;

        foreach (string part in text.Split(';'))
        {
            if (part.Length == 0) continue;

            string[] bits = part.Split(':');
            if (bits.Length != 2) return null;

            uint id;
            if (!uint.TryParse(bits[0], out id)) return null;
            if (bits[1].Length == 0) return null;

            if (!byId.ContainsKey(id)) byId[id] = bits[1];
        }

        return byId;
    }

    private static string Describe(PlayerDeal other)
    {
        return string.IsNullOrEmpty(other.username) ? "netId " + other.netId : other.username;
    }

    public static byte[] MatchSeedFrom(byte[] serverSeed, string contributions)
    {
        List<uint> ids = new List<uint>();
        Dictionary<uint, byte[]> byId = new Dictionary<uint, byte[]>();

        if (!string.IsNullOrEmpty(contributions))
        {
            foreach (string part in contributions.Split(';'))
            {
                if (part.Length == 0) continue;

                string[] bits = part.Split(':');
                if (bits.Length != 2) return null;

                uint id;
                if (!uint.TryParse(bits[0], out id)) return null;

                byte[] seed = CardShuffle.FromHex(bits[1]);
                if (seed == null) return null;

                if (byId.ContainsKey(id)) continue;

                ids.Add(id);
                byId[id] = seed;
            }
        }

        ids.Sort();

        byte[][] parts = new byte[ids.Count][];
        for (int i = 0; i < ids.Count; i++) parts[i] = byId[ids[i]];

        return CardShuffle.MatchSeed(serverSeed, parts);
    }

    public static bool ContributionPresent(string contributions)
    {
        if (myContribution == null) return true;
        if (string.IsNullOrEmpty(contributions)) return false;

        return contributions.Contains(CardShuffle.Hex(myContribution));
    }

    private static void Pass(string reason)
    {
        Passed = true;
        Result = "Shuffle verified - " + reason + ".";
        Debug.Log("LocalShuffleProof: " + Result);
    }

    private static void Fail(string reason)
    {
        Passed = false;
        Result = "SHUFFLE FAILED VERIFICATION - " + reason + ".";
        Debug.LogError("LocalShuffleProof: " + Result);
    }
}
