using System.Collections.Generic;
using UnityEngine;

public static class MatchFairness
{
    private static byte[] serverSeed;
    private static byte[] commitment;
    private static byte[] matchSeed;
    private static bool settled;

    private static bool sealedSeeds;

    private static readonly Dictionary<uint, byte[]> seedCommitments = new Dictionary<uint, byte[]>();
    private static readonly Dictionary<uint, byte[]> clientSeeds = new Dictionary<uint, byte[]>();
    private static readonly Dictionary<uint, string> dealtOrders = new Dictionary<uint, string>();

    public static bool Begun { get { return serverSeed != null; } }

    public static bool Settled { get { return settled; } }

    public static bool Replaying { get; private set; }

    private static readonly Dictionary<uint, uint> replaySeats = new Dictionary<uint, uint>();

    public static bool Sealed { get { return sealedSeeds; } }

    public static int Contributors { get { return clientSeeds.Count; } }

    public static int Committed { get { return seedCommitments.Count; } }

    public static bool AllRevealed
    {
        get
        {
            foreach (uint id in seedCommitments.Keys)
                if (!clientSeeds.ContainsKey(id)) return false;

            return true;
        }
    }

    public static byte[] Entropy
    {
        get { return settled ? matchSeed : null; }
    }

    public static string CommitmentHex { get { return CardShuffle.Hex(commitment); } }

    public static string RevealHex { get { return settled ? CardShuffle.Hex(serverSeed) : ""; } }

    public static string ContributionsHex
    {
        get
        {
            List<uint> ids = Sorted(clientSeeds);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (uint id in ids)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(id).Append(':').Append(CardShuffle.Hex(clientSeeds[id]));
            }

            return sb.ToString();
        }
    }

    public static string SeedCommitmentsHex
    {
        get
        {
            List<uint> ids = Sorted(seedCommitments);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (uint id in ids)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(id).Append(':').Append(CardShuffle.Hex(seedCommitments[id]));
            }

            return sb.ToString();
        }
    }

    public static string DealtOrdersText
    {
        get
        {
            List<uint> ids = Sorted(dealtOrders);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (uint id in ids)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(id).Append(':').Append(dealtOrders[id]);
            }

            return sb.ToString();
        }
    }

    public static void Begin()
    {
        serverSeed = CardShuffle.NewSeed(CardShuffle.SeedBytes);
        commitment = CardShuffle.Commitment(serverSeed);
        matchSeed = null;
        settled = false;
        sealedSeeds = false;
        seedCommitments.Clear();
        clientSeeds.Clear();
        dealtOrders.Clear();
        Replaying = false;
        replaySeats.Clear();
        MatchRandom.Reset();

        Debug.Log("MatchFairness: committed to a shuffle before any card was dealt. commitment=" + CommitmentHex);
    }

    public static bool AddSeedCommitment(uint netId, byte[] hash)
    {
        if (sealedSeeds)
        {
            Debug.LogWarning("MatchFairness: a seed commitment from " + netId + " arrived after sealing - ignored.");
            return false;
        }

        if (hash == null || hash.Length != CardShuffle.SeedBytes) return false;
        if (seedCommitments.ContainsKey(netId)) return false;

        seedCommitments[netId] = hash;
        Debug.Log("MatchFairness: " + netId + " committed to a seed it has not yet revealed (" + seedCommitments.Count + " so far).");
        return true;
    }

    public static void SealSeeds()
    {
        if (sealedSeeds) return;

        sealedSeeds = true;
        Debug.Log("MatchFairness: sealed the seed commitments - no player can change or add one now.");
    }

    public static bool AddClientSeed(uint netId, byte[] seed)
    {
        if (settled)
        {
            Debug.LogWarning("MatchFairness: seed from " + netId + " arrived after the deal was settled - ignored.");
            return false;
        }

        if (!sealedSeeds)
        {
            Debug.LogWarning("MatchFairness: seed from " + netId + " arrived before the commitments were sealed - rejected.");
            return false;
        }

        if (seed == null || seed.Length != CardShuffle.ClientSeedBytes) return false;
        if (clientSeeds.ContainsKey(netId)) return false;

        byte[] promised;
        if (!seedCommitments.TryGetValue(netId, out promised))
        {
            Debug.LogWarning("MatchFairness: seed from " + netId + " has no matching commitment - rejected.");
            return false;
        }

        if (!CardShuffle.CommitmentHolds(seed, promised))
        {
            Debug.LogError("MatchFairness: " + netId + " revealed a seed that is not the one it committed to - rejected.");
            return false;
        }

        clientSeeds[netId] = seed;
        Debug.Log("MatchFairness: accepted a shuffle seed from " + netId + " (" + clientSeeds.Count + " so far).");
        return true;
    }

    public static bool AddDealtOrder(uint netId, string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint)) return false;

        if (dealtOrders.ContainsKey(netId))
        {
            Debug.LogWarning("MatchFairness: a second dealt-order attestation from " + netId + " was ignored.");
            return false;
        }

        dealtOrders[netId] = fingerprint;
        Debug.Log("MatchFairness: " + netId + " attested the order it was dealt.");
        return true;
    }

    public static void Settle()
    {
        if (settled) return;
        if (!Begun) Begin();

        List<uint> ids = Sorted(clientSeeds);

        byte[][] parts = new byte[ids.Count][];
        for (int i = 0; i < ids.Count; i++) parts[i] = clientSeeds[ids[i]];

        matchSeed = CardShuffle.MatchSeed(serverSeed, parts);
        settled = true;

        Debug.Log("MatchFairness: deal settled with " + ids.Count + " player contribution(s).");
    }

    public static byte[] SeedFor(uint playerNetId)
    {
        if (!settled) Settle();

        uint dealtAs = playerNetId;

        if (Replaying && replaySeats.ContainsKey(playerNetId)) dealtAs = replaySeats[playerNetId];

        return CardShuffle.PlayerSeed(matchSeed, dealtAs);
    }

    public static bool BeginReplay(string seedHex, string contributions)
    {
        Clear();

        byte[] recorded = CardShuffle.FromHex(seedHex);
        if (recorded == null)
        {
            Debug.LogError("MatchFairness: the replay carries no readable seed.");
            return false;
        }

        byte[] rebuilt = LocalShuffleProof.MatchSeedFrom(recorded, contributions);
        if (rebuilt == null)
        {
            Debug.LogError("MatchFairness: the replay's seed contributions could not be read.");
            return false;
        }

        serverSeed = recorded;
        commitment = CardShuffle.Commitment(recorded);
        matchSeed = rebuilt;
        settled = true;
        sealedSeeds = true;
        Replaying = true;
        MatchRandom.Reset();

        Debug.Log("MatchFairness: replaying a match dealt from seed " +
                  CardShuffle.Hex(recorded).Substring(0, 16) + ".");
        return true;
    }

    public static void MapReplaySeat(uint liveNetId, uint recordedNetId)
    {
        if (!Replaying) return;

        replaySeats[liveNetId] = recordedNetId;
    }

    public static void Clear()
    {
        serverSeed = null;
        commitment = null;
        matchSeed = null;
        settled = false;
        sealedSeeds = false;
        seedCommitments.Clear();
        clientSeeds.Clear();
        dealtOrders.Clear();
        Replaying = false;
        replaySeats.Clear();
        MatchRandom.Reset();
    }

    private static List<uint> Sorted<T>(Dictionary<uint, T> map)
    {
        List<uint> ids = new List<uint>(map.Keys);
        ids.Sort();
        return ids;
    }
}
