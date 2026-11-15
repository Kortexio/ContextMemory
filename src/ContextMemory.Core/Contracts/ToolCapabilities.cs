namespace ContextMemory.Core.Contracts;

/// <summary>
/// Behavioral flags for a registered tool. Gates (archive, evidence, duplicate, sandbox) use these — not wire-name lists.
/// </summary>
[Flags]
public enum ToolCapabilities
{
    None = 0,
    Discovery = 1 << 0,
    Sandbox = 1 << 1,
    QueryFocused = 1 << 2,
    Mutating = 1 << 3,
    Http = 1 << 4,
    Browser = 1 << 5,
    Vision = 1 << 6,
    Document = 1 << 7,
    Canvas = 1 << 8,
}
