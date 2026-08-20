public interface IMatchBotHost
{
    string ServerKey { get; }

    void BotLog(string message);

    void BotFailed(string reason);

    bool BotVerify(string publicKeyHex, string message, string signatureHex);
}
