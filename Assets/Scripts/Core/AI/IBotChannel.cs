public interface IBotChannel
{
    string Name { get; }

    string Key { get; }

    void Request(int token, string state);

    void RequestSignature(int token, string digestHex);

    string Poll(int token);

    void Cancel(int token);

    void Close(string result);
}
