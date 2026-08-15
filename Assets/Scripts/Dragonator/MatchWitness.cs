using System;
using UnityEngine;

public static class MatchWitness
{
    private class Host : IMatchWitnessHost
    {
        public void WitnessLog(string message)
        {
            Debug.Log("[Witness] " + message);
        }

        public void WitnessFailed(string reason)
        {
            Debug.LogError("[Witness] " + reason);
        }
    }

    private static readonly Host host = new Host();

    private static IMatchWitness witness;
    private static bool attached;
    private static bool broken;

    public static bool Installed
    {
        get
        {
            EnsureAttached();
            return witness != null && !broken;
        }
    }

    public static void EnsureAttached()
    {
        if (attached) return;
        attached = true;

        witness = AddonLoader.Witness;

        if (witness == null)
        {
            Debug.Log("[Witness] No match witness installed - receipts are not kept or anchored.");
            return;
        }

        try
        {
            witness.Attach(host);
        }
        catch (Exception e)
        {
            broken = true;
            Debug.LogError($"[Witness] The match witness could not start ({e.GetType().Name}: {e.Message}). " +
                           "Matches continue; nothing is recorded.");
        }
    }

    public static void Record(string receipt, string signatures, bool fullySigned)
    {
        if (!Installed) return;

        try
        {
            witness.Record(receipt, signatures, fullySigned);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Witness] The match witness threw while recording ({e.GetType().Name}: {e.Message}). " +
                           "The match result and any payout are unaffected.");
        }
    }

    public static string Lookup(string digest)
    {
        if (!Installed || string.IsNullOrEmpty(digest)) return "";

        try
        {
            return witness.Lookup(digest) ?? "";
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Witness] A receipt lookup failed ({e.GetType().Name}).");
            return "";
        }
    }

    public static void Tick()
    {
        if (!Installed) return;

        try
        {
            witness.Tick();
        }
        catch (Exception e)
        {
            broken = true;
            Debug.LogError($"[Witness] The match witness threw during Tick ({e.GetType().Name}: {e.Message}). " +
                           "It is now switched off for this run.");
        }
    }

    public static void Shutdown()
    {
        if (witness == null || broken) return;

        try
        {
            witness.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Witness] The match witness threw while shutting down ({e.GetType().Name}: {e.Message}).");
        }
    }
}
