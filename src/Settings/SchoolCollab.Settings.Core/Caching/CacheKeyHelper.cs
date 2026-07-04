using System.Security.Cryptography;
using System.Text;

namespace SchoolCollab.Settings.Core.Caching;

internal static class CacheKeyHelper
{
    internal static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
