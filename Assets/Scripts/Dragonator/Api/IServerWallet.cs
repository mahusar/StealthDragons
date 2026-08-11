public interface IServerWallet
{
    string Name { get; }

    string Needs { get; }

    bool Required { get; }

    void UseFree();

    bool Check(out string problem);
}
