public interface IMatchBot
{
    string Name { get; }

    int Seats { get; }

    int Waiting { get; }

    void Attach(IMatchBotHost host);

    bool SeatBot(int seat);

    string SeatName(int seat);

    string SeatKey(int seat);

    void Request(int seat, int token, string state);

    void RequestSignature(int seat, int token, string digestHex);

    string Poll(int seat, int token);

    void Cancel(int seat, int token);

    void MatchEnded(int seat, string result);

    void Shutdown();
}
