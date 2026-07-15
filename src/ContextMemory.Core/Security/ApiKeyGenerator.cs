using System.Security.Cryptography;

namespace ContextMemory.Core.Security;

public static class ApiKeyGenerator
{
    public static string CreateLiveKey() =>
        $"cm_live_{RandomNumberGenerator.GetHexString(24, lowercase: true)}";
}
