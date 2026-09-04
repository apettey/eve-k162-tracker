using System.Security.Cryptography;
using System.Text;

namespace K162.Core.Sso;

/// <summary>PKCE (RFC 7636) code verifier / challenge generation for the EVE SSO native-app flow.</summary>
public static class Pkce
{
    /// <summary>Generates a 43-character base64url code verifier from 32 random bytes.</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>S256 challenge: base64url(SHA256(ascii(verifier))).</summary>
    public static string CreateChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
