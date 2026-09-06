namespace ContextMemory.Core.Agentic;

/// <summary>Heuristic classification of secret-like key names.</summary>
public sealed class SecretClassifier
{
    public SecretClassification Classify(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return SecretClassification.Public;

        var k = key.Trim().ToLowerInvariant();

        if (ContainsAny(k, "password", "passwd", "secret", "token", "api_key", "apikey", "private_key",
                "privatekey", "client_secret", "access_key", "auth_key", "bearer"))
            return SecretClassification.Secret;

        if (ContainsAny(k, "credential", "certificate", "cert", "ssh", "pgp", "keystore"))
            return SecretClassification.Restricted;

        if (ContainsAny(k, "ssn", "iban", "nif", "cpf", "pii", "email", "phone", "dob", "confidential"))
            return SecretClassification.Confidential;

        if (ContainsAny(k, "internal", "tenant", "org", "employee", "userid", "user_id"))
            return SecretClassification.Internal;

        return SecretClassification.Public;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
