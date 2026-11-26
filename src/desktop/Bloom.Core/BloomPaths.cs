namespace Bloom.Core;

/// <summary>
/// File system locations. Everything lives under %LOCALAPPDATA%\bloom (per user, no admin rights).
/// The Velopack install itself sits in %LOCALAPPDATA%\bloom\current.
/// </summary>
public sealed class BloomPaths
{
    public BloomPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bloom");
    }

    public string Root { get; }
    public string Data => Path.Combine(Root, "data");
    public string Logs => Path.Combine(Root, "logs");
    public string Database => Path.Combine(Data, "bloom.db");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
    }
}
