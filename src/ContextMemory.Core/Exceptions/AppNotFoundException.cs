namespace ContextMemory.Core.Exceptions;

public sealed class AppNotFoundException(string appId)
    : ContextMemoryException($"App '{appId}' not found.");
