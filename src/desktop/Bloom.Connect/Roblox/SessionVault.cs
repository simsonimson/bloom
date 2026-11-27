using System.Text;
using System.Text.Json;
using Bloom.Core.Connect;
using Bloom.Platform.Windows;

namespace Bloom.Connect.Roblox;

public sealed record StoredSession(string RefreshToken, RobloxProfile? Profile, DateTimeOffset SavedAt);

/// <summary>Refresh token + last profile, encrypted with DPAPI for the current Windows user.</summary>
public sealed class SessionVault(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("bloom.roblox-session.v1");

    public StoredSession? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = DataProtection.Unprotect(File.ReadAllBytes(path), Entropy);
            return JsonSerializer.Deserialize<StoredSession>(json);
        }
        catch (Exception)
        {
            // Corrupt, from another Windows user or another machine: treat as signed out.
            return null;
        }
    }

    public void Save(StoredSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, DataProtection.Protect(JsonSerializer.SerializeToUtf8Bytes(session), Entropy));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete()
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
