using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class DeckSecret
{
    public const int NonceBytes = 16;

    public static byte[] NewNonce()
    {
        return CardShuffle.NewSeed(NonceBytes);
    }

    public static string CommitmentHex(byte[] nonce, string wire)
    {
        if (nonce == null || string.IsNullOrEmpty(wire)) return "";

        byte[] list = Encoding.UTF8.GetBytes(wire);
        byte[] input = new byte[nonce.Length + list.Length];

        System.Buffer.BlockCopy(nonce, 0, input, 0, nonce.Length);
        System.Buffer.BlockCopy(list, 0, input, nonce.Length, list.Length);

        using (SHA256 sha = SHA256.Create())
            return CardShuffle.Hex(sha.ComputeHash(input));
    }

    public static bool Holds(string commitmentHex, string nonceHex, string wire)
    {
        if (string.IsNullOrEmpty(commitmentHex) || string.IsNullOrEmpty(nonceHex)) return false;

        byte[] nonce = CardShuffle.FromHex(nonceHex);
        if (nonce == null) return false;

        string again = CommitmentHex(nonce, wire);

        return again.Length > 0 && string.Equals(again, commitmentHex, System.StringComparison.OrdinalIgnoreCase);
    }
}
