using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using Mirror.BouncyCastle.Crypto.Parameters;
using Mirror.BouncyCastle.Crypto.Signers;

public class PlayerIdentity
{
    public const int SeedBytes = 32;
    public const int PublicKeyBytes = 32;
    public const int SignatureBytes = 64;

    private static PlayerIdentity mine;

    private readonly Ed25519PrivateKeyParameters key;

    public byte[] PublicKey { get; private set; }

    public string PublicKeyHex { get; private set; }

    private PlayerIdentity(byte[] seed)
    {
        key = new Ed25519PrivateKeyParameters(seed, 0);
        PublicKey = key.GeneratePublicKey().GetEncoded();
        PublicKeyHex = CardShuffle.Hex(PublicKey);
    }

    public static PlayerIdentity Mine
    {
        get
        {
            if (mine == null) mine = LoadOrCreate(MinePath());

            return mine;
        }
    }

    public static string MinePath()
    {
        string fromFlag = Flag("-identity");
        if (!string.IsNullOrEmpty(fromFlag)) return fromFlag;

        try
        {
            string fromEnv = Environment.GetEnvironmentVariable("DRAGONATOR_IDENTITY");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
        }
        catch (Exception)
        {
        }

        return Path.Combine(Application.persistentDataPath, "identity.key");
    }

    private static string Flag(string name)
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        catch (Exception)
        {
        }

        return "";
    }

    public static PlayerIdentity LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                byte[] stored = File.ReadAllBytes(path);
                if (stored.Length == SeedBytes) return new PlayerIdentity(stored);

                Debug.LogWarning($"PlayerIdentity: {path} is {stored.Length} bytes, not {SeedBytes} - keeping it aside rather than reusing it.");
                SetAside(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PlayerIdentity: could not read {path} ({e.GetType().Name}) - a new identity will be created.");
        }

        byte[] seed = new byte[SeedBytes];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(seed);

        PlayerIdentity created = new PlayerIdentity(seed);

        try
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllBytes(path, seed);
            Debug.Log($"PlayerIdentity: created a new identity {created.PublicKeyHex}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"PlayerIdentity: could not save the new identity to {path} ({e.GetType().Name}) - it will not survive a restart.");
        }

        return created;
    }

    private static void SetAside(string path)
    {
        string kept = path + ".unusable";

        try
        {
            if (File.Exists(kept)) File.Delete(kept);
            File.Move(path, kept);
            Debug.LogWarning($"PlayerIdentity: the previous file was moved to {kept}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"PlayerIdentity: {path} could not be moved aside ({e.GetType().Name}) - it will be overwritten.");
        }
    }

    public byte[] Sign(byte[] message)
    {
        if (message == null) return null;

        Ed25519Signer signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(message, 0, message.Length);

        return signer.GenerateSignature();
    }

    public string SignHex(byte[] message)
    {
        return CardShuffle.Hex(Sign(message));
    }

    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey == null || publicKey.Length != PublicKeyBytes) return false;
        if (message == null) return false;
        if (signature == null || signature.Length != SignatureBytes) return false;

        try
        {
            Ed25519PublicKeyParameters pub = new Ed25519PublicKeyParameters(publicKey, 0);

            Ed25519Signer signer = new Ed25519Signer();
            signer.Init(false, pub);
            signer.BlockUpdate(message, 0, message.Length);

            return signer.VerifySignature(signature);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PlayerIdentity: a signature could not be checked ({e.GetType().Name}).");
            return false;
        }
    }

    public static bool Verify(string publicKeyHex, byte[] message, string signatureHex)
    {
        return Verify(CardShuffle.FromHex(publicKeyHex), message, CardShuffle.FromHex(signatureHex));
    }
}
