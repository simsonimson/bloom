using System.Security.Cryptography;
using System.Text;

namespace Bloom.Connect.Roblox;

/// <summary>PKCE (RFC 7636) with S256, required by Roblox for public clients.</summary>
public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create() => FromVerifier(Base64Url(RandomNumberGenerator.GetBytes(32)));

    public static Pkce FromVerifier(string verifier) =>
        new(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));

    public static string RandomState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
