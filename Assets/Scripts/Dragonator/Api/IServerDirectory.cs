using System.Collections.Generic;

public interface IServerDirectory
{
    string Name { get; }

    string Status { get; }

    List<string> Listings { get; }
}
