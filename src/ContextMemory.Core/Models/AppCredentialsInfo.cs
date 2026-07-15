namespace ContextMemory.Core.Models;

public sealed class AppCredentialsInfo
{
    public required string AppId { get; init; }
    public required string ApiKey { get; init; }
    public required string Source { get; init; }
    public bool RotationPersists { get; init; }
}
