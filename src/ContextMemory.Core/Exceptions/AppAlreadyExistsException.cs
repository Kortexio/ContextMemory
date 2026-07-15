namespace ContextMemory.Core.Exceptions;

public sealed class AppAlreadyExistsException(string appId)
    : ContextMemoryException($"App '{appId}' already exists.");
