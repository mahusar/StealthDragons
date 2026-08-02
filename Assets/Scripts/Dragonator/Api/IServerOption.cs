public interface IServerOption
{
    string Key { get; }
    string Label { get; }
    string PromptText { get; }
    string DescribeCurrent();
    void ApplyDefault();
    bool TryApply(string input, out string error);
    string ToWire();
}
