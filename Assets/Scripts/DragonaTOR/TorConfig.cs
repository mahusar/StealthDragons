public static class TorConfig
{
    public const string OnionAddressKey = "TorServerAddress";
    public const int MatchmakerPort = 5555;
    public const int GamePort = 7780;

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
