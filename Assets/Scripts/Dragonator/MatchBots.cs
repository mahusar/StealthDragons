using System;
using UnityEngine;

public static class MatchBots
{
    private class Host : IMatchBotHost
    {
        public string ServerKey
        {
            get { return serverKey; }
        }

        public void BotLog(string message)
        {
            Debug.Log("[Bots] " + message);
        }

        public void BotFailed(string reason)
        {
            Debug.LogWarning("[Bots] " + reason);
        }

        public bool BotVerify(string publicKeyHex, string message, string signatureHex)
        {
            if (string.IsNullOrEmpty(publicKeyHex) || message == null) return false;

            try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(message);
                return PlayerIdentity.Verify(publicKeyHex, bytes, signatureHex);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bots] A bot signature could not be checked ({e.GetType().Name}).");
                return false;
            }
        }
    }

    private class Channel : IBotChannel
    {
        private readonly IMatchBot addon;
        private readonly int seat;
        private readonly string name;
        private readonly string key;

        private bool closed;

        public Channel(IMatchBot addon, int seat, string name, string key)
        {
            this.addon = addon;
            this.seat = seat;
            this.name = name;
            this.key = key;
        }

        public string Name
        {
            get { return name; }
        }

        public string Key
        {
            get { return key; }
        }

        public void Request(int token, string state)
        {
            addon.Request(seat, token, state);
        }

        public void RequestSignature(int token, string digestHex)
        {
            addon.RequestSignature(seat, token, digestHex);
        }

        public string Poll(int token)
        {
            return addon.Poll(seat, token);
        }

        public void Cancel(int token)
        {
            addon.Cancel(seat, token);
        }

        public void Close(string result)
        {
            if (closed) return;
            closed = true;

            try
            {
                addon.MatchEnded(seat, result ?? "");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bots] The provider threw closing seat {seat + 1} " +
                                 $"({e.GetType().Name}: {e.Message}).");
            }
        }
    }

    private static readonly Host host = new Host();

    private static IMatchBot bot;
    private static bool attached;
    private static bool broken;
    private static string serverKey = "";

    public static bool Installed
    {
        get
        {
            EnsureAttached();
            return bot != null && !broken;
        }
    }

    public static void EnsureAttached()
    {
        if (attached) return;
        attached = true;

        bot = AddonLoader.Bots;

        if (bot == null)
        {
            Debug.Log("[Bots] No match bot provider installed - only the built-in practice bot can take a seat.");
            return;
        }

        try
        {
            serverKey = PlayerIdentity.Mine.PublicKeyHex;
        }
        catch (Exception e)
        {
            serverKey = "";
            Debug.LogWarning($"[Bots] This server has no identity key to offer bots ({e.GetType().Name}).");
        }

        try
        {
            bot.Attach(host);
        }
        catch (Exception e)
        {
            broken = true;
            Debug.LogError($"[Bots] The match bot provider could not start ({e.GetType().Name}: {e.Message}). " +
                           "Matches continue; no bot seat is offered.");
        }
    }

    public static int Seats
    {
        get
        {
            if (!Installed) return 0;

            try
            {
                int count = bot.Seats;
                return count < 0 ? 0 : count;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bots] The provider threw asking how many seats it drives " +
                                 $"({e.GetType().Name}). Treating it as none.");
                return 0;
            }
        }
    }

    public static int Waiting
    {
        get
        {
            if (!Installed) return 0;

            try
            {
                int count = bot.Waiting;
                return count < 0 ? 0 : count;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bots] The provider threw asking how many bots are waiting " +
                                 $"({e.GetType().Name}). Treating it as none.");
                return 0;
            }
        }
    }

    public static string SeatName(int seat)
    {
        string fallback = "bot " + (seat + 1);

        if (!Installed) return fallback;

        try
        {
            string given = bot.SeatName(seat);
            return string.IsNullOrEmpty(given) ? fallback : given;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static string SeatKey(int seat)
    {
        if (!Installed) return "";

        try
        {
            return bot.SeatKey(seat) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static IBotChannel Open(int seat)
    {
        if (!Installed) return null;

        try
        {
            if (!bot.SeatBot(seat)) return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Bots] The provider threw filling seat {seat + 1} " +
                             $"({e.GetType().Name}: {e.Message}). That seat is not driven.");
            return null;
        }

        return new Channel(bot, seat, SeatName(seat), SeatKey(seat));
    }

    public static void Shutdown()
    {
        if (bot == null || broken) return;

        try
        {
            bot.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Bots] The match bot provider threw while shutting down " +
                           $"({e.GetType().Name}: {e.Message}).");
        }
    }
}
