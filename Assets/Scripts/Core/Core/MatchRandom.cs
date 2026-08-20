using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class MatchRandom
{
    private static readonly byte[] Label = Encoding.ASCII.GetBytes("draw");

    private static int drawn;

    public static int Drawn { get { return drawn; } }

    public static void Reset()
    {
        if (drawn > 0)
            Debug.Log("MatchRandom: closing a match that made " + drawn + " seeded draw(s).");

        drawn = 0;
    }

    public static int Below(int bound)
    {
        int index = drawn++;

        if (bound <= 1) return 0;

        uint limit = (uint)bound;
        uint threshold = (uint)((0x100000000UL - limit) % limit);

        for (int attempt = 0; attempt < 32; attempt++)
        {
            uint candidate = Word(index, attempt);
            if (candidate >= threshold) return (int)(candidate % limit);
        }

        return (int)(Word(index, 32) % limit);
    }

    private static uint Word(int index, int attempt)
    {
        byte[] seed = MatchFairness.Entropy;

        if (seed == null)
        {
            Debug.LogError("MatchRandom: a draw was asked for before the deal was settled, so this match " +
                           "has no sealed seed to draw from and the result cannot be replayed.");
            return 0;
        }

        byte[] input = new byte[seed.Length + Label.Length + 8];

        Buffer.BlockCopy(seed, 0, input, 0, seed.Length);
        Buffer.BlockCopy(Label, 0, input, seed.Length, Label.Length);

        int at = seed.Length + Label.Length;
        WriteBigEndian(input, at, index);
        WriteBigEndian(input, at + 4, attempt);

        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(input);

            return ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
        }
    }

    private static void WriteBigEndian(byte[] into, int at, int value)
    {
        into[at] = (byte)(value >> 24);
        into[at + 1] = (byte)(value >> 16);
        into[at + 2] = (byte)(value >> 8);
        into[at + 3] = (byte)value;
    }
}
