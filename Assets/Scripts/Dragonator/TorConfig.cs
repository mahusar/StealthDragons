using Mirror;

public static class TorConfig
{
    public const string OnionAddressKey = "TorServerAddress";
    public const int MatchmakerPort = 5555;
    public const int GamePort = 7780;

    public const string DefaultSocksHost = "127.0.0.1";
    public const int DefaultSocksPort = 9050;

    private static string socksHostOverride;
    private static int socksPortOverride;

    private static TorTelepathyTransport ActiveTorTransport => Transport.active as TorTelepathyTransport;

    public static string SocksHost
    {
        get
        {
            if (!string.IsNullOrEmpty(socksHostOverride)) return socksHostOverride;
            TorTelepathyTransport transport = ActiveTorTransport;
            return transport != null ? transport.socksHost : DefaultSocksHost;
        }
    }

    public static int SocksPort
    {
        get
        {
            if (socksPortOverride > 0) return socksPortOverride;
            TorTelepathyTransport transport = ActiveTorTransport;
            return transport != null ? transport.socksPort : DefaultSocksPort;
        }
    }

    public static void SetSocksProxy(string host, int port)
    {
        socksHostOverride = host;
        socksPortOverride = port;
        UnityEngine.Debug.Log($"[TorConfig] SOCKS proxy overridden to {SocksHost}:{SocksPort}");
    }

    public static void ClearSocksProxyOverride()
    {
        socksHostOverride = null;
        socksPortOverride = 0;
        UnityEngine.Debug.Log($"[TorConfig] SOCKS proxy override cleared, using {SocksHost}:{SocksPort}");
    }

    public static string GetSavedOnionAddress()
    {
        return UnityEngine.PlayerPrefs.GetString(OnionAddressKey, "");
    }

    public static void SaveOnionAddress(string address)
    {
        UnityEngine.PlayerPrefs.SetString(OnionAddressKey, address);
        UnityEngine.PlayerPrefs.Save();
    }
}
