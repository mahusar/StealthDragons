public interface IMatchEscrowHost
{
    int ServerPort { get; }

    void PromptForPayoutAddress(int connectionId, string amount, int confirmations);
    void ShowDepositAddress(int connectionId, string depositAddress, string amount);
    void SetPlayerStatus(int connectionId, string status);
    void Message(int connectionId, bool success, string text);
    void SetFundingDeadline(double seconds);
    void EscrowReady(string matchId);
    void EscrowVoided(string matchId, string reason);
    void SettlementSent(int connectionId, string kind, string txid);
    void Log(string text);
    void Warn(string text);
    void Error(string text);
}
