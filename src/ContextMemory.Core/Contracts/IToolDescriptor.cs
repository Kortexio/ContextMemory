using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public enum ToolHandlerKind
{
    AppScoped = 0,
    SessionScoped = 1,
}

/// <summary>
/// Canonical registration for one wire-facing tool owned by an executor.
/// </summary>
public sealed record ToolRegistration(
    string WireName,
    ToolCapabilities Capabilities,
    ToolHandlerKind Kind);

/// <summary>
/// Declares wire names, capabilities, and LLM schemas for tools owned by an executor.
/// Orchestration code never hardcodes these names.
/// </summary>
public interface IToolDescriptor
{
    IEnumerable<ToolRegistration> Describe();

    /// <summary>
    /// Schemas to expose to the model when this descriptor's tools are enabled for the tenant.
    /// </summary>
    IEnumerable<OllamaTool> BuildSchemas(AppRuntimeConfig runtimeConfig);
}

/// <summary>
/// Resolved tool metadata for dispatch and gates.
/// </summary>
public sealed class ResolvedTool
{
    public required string WireName { get; init; }
    public ToolCapabilities Capabilities { get; init; }
    public ToolHandlerKind Kind { get; init; }
    public bool IsMcp { get; init; }
}

/// <summary>
/// Looks up a tool by LLM wire name against the DI registration map (plus MCP fallback).
/// </summary>
public interface IToolResolver
{
    bool TryResolve(string? wireName, AppRuntimeConfig runtimeConfig, out ResolvedTool tool);

    bool HasCapability(string? wireName, ToolCapabilities flag);
}

/// <summary>
/// Built-in tool descriptors registered in DI (schemas + capability map source).
/// </summary>
public interface IToolRegistry
{
    IReadOnlyList<IToolDescriptor> Descriptors { get; }

    IEnumerable<OllamaTool> BuildBuiltinSchemas(AppRuntimeConfig runtimeConfig);
}
