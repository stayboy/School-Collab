using System.Security.Cryptography;
using System.Text;

namespace SchoolCollab.Students.Core.Caching;

/// <summary>
/// Deterministic short hashes for cache keys that would otherwise be too long
/// (e.g. a sorted CSV of many GUIDs). Mirrors <c>Settings.Core</c>'s helper.
/// </summary>
internal static class CacheKeyHelper
{
    internal static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
