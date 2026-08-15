using System;
using System.Collections.Generic;
using System.Security.Cryptography;

public static class CardShuffle
{
    public const int SeedBytes = 32;
    public const int ClientSeedBytes = 16;

    public static byte[] NewSeed(int length)
    {
        byte[] seed = new byte[length];

        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            rng.GetBytes(seed);

        return seed;
    }

    public static byte[] Commitment(byte[] serverSeed)
    {
        return Sha256(serverSeed);
    }

    public static bool CommitmentHolds(byte[] serverSeed, byte[] commitment)
    {
        if (serverSeed == null || commitment == null) return false;

        return Same(Sha256(serverSeed), commitment);
    }

    public static byte[] MatchSeed(byte[] serverSeed, params byte[][] clientSeeds)
    {
        byte[][] parts = new byte[clientSeeds.Length + 1][];
        parts[0] = serverSeed;

        for (int i = 0; i < clientSeeds.Length; i++) parts[i + 1] = clientSeeds[i];

        return Sha256(Join(parts));
    }

    public static byte[] PlayerSeed(byte[] matchSeed, uint playerNetId)
    {
        return Sha256(Join(matchSeed, BigEndian(playerNetId)));
    }

    public static void Shuffle(List<CardInfo> cards, byte[] seed)
    {
        if (cards == null || cards.Count < 2) return;

        ulong state = StateFrom(seed);

        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = (int)Below(ref state, (uint)(i + 1));

            CardInfo held = cards[i];
            cards[i] = cards[j];
            cards[j] = held;
        }
    }

    public static string Fingerprint(List<CardInfo> cards)
    {
        if (cards == null) return "";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        for (int i = 0; i < cards.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(cards[i].cardID);
        }

        return Hex(Sha256(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string Hex(byte[] bytes)
    {
        if (bytes == null) return "";

        char[] chars = new char[bytes.Length * 2];

        for (int i = 0; i < bytes.Length; i++)
        {
            int value = bytes[i];
            chars[i * 2] = Digit(value >> 4);
            chars[i * 2 + 1] = Digit(value & 0xF);
        }

        return new string(chars);
    }

    public static byte[] FromHex(string text)
    {
        if (string.IsNullOrEmpty(text) || (text.Length & 1) != 0) return null;

        byte[] bytes = new byte[text.Length / 2];

        for (int i = 0; i < bytes.Length; i++)
        {
            int high = Value(text[i * 2]);
            int low = Value(text[i * 2 + 1]);

            if (high < 0 || low < 0) return null;

            bytes[i] = (byte)((high << 4) | low);
        }

        return bytes;
    }

    public static bool Same(byte[] left, byte[] right)
    {
        if (left == null || right == null) return false;
        if (left.Length != right.Length) return false;

        int difference = 0;
        for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];

        return difference == 0;
    }

    private static byte[] Sha256(byte[] data)
    {
        using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data);
    }

    private static byte[] Join(params byte[][] parts)
    {
        int total = 0;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null) total += parts[i].Length;

        byte[] joined = new byte[total];

        int at = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;

            Buffer.BlockCopy(parts[i], 0, joined, at, parts[i].Length);
            at += parts[i].Length;
        }

        return joined;
    }

    private static byte[] BigEndian(uint value)
    {
        return new byte[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        };
    }

    private static ulong StateFrom(byte[] seed)
    {
        ulong state = 0;

        if (seed != null)
            for (int i = 0; i < 8 && i < seed.Length; i++) state = (state << 8) | seed[i];

        return state == 0 ? 0x9E3779B97F4A7C15UL : state;
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;

        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

        return z ^ (z >> 31);
    }

    private static uint Below(ref ulong state, uint bound)
    {
        uint threshold = (uint)((0x100000000UL - bound) % bound);

        while (true)
        {
            uint drawn = (uint)Next(ref state);
            if (drawn >= threshold) return drawn % bound;
        }
    }

    private static char Digit(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }

    private static int Value(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;

        return -1;
    }
}
