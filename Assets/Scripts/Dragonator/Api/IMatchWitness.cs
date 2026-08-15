public interface IMatchWitness
{
    void Attach(IMatchWitnessHost host);
    void Record(string receipt, string signatures, bool fullySigned);
    string Lookup(string digest);
    void Tick();
    void Shutdown();
}
