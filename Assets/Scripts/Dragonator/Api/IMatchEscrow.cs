public interface IMatchEscrow
{
    void Attach(IMatchEscrowHost host);
    void BeginMatch(string matchId, int[] connectionIds, string[] playerNames);
    void SubmitPayoutAddress(string matchId, int connectionId, string payoutAddress);
    void PlayerLeft(string matchId, int connectionId);
    void Settle(string matchId, int winnerConnectionId);
    void Void(string matchId, string reason);
    void Tick();
    void Shutdown();
}
