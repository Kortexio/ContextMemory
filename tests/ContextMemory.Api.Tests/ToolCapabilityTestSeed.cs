using System.Runtime.CompilerServices;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Tests;

/// <summary>
/// Seeds <see cref="ToolCapabilityIndex"/> for unit tests that do not resolve <see cref="IToolResolver"/> via DI.
/// Full host tests get the real map when <c>ToolResolver</c> is constructed.
/// </summary>
internal static class ToolCapabilityTestSeed
{
    [ModuleInitializer]
    internal static void Init() => PublishBaseline();

    internal static void PublishBaseline()
    {
        ToolCapabilityIndex.Publish(new Dictionary<string, ToolCapabilities>(StringComparer.OrdinalIgnoreCase)
        {
            [AgenticToolRegistry.WikiSearchToolName] = ToolCapabilities.QueryFocused,
            [AgenticToolRegistry.WikiFetchToolName] = ToolCapabilities.QueryFocused,
            [AgenticToolRegistry.WikiGrepToolName] = ToolCapabilities.QueryFocused,
            [AgenticToolRegistry.ShellExecuteToolName] = ToolCapabilities.Sandbox | ToolCapabilities.Mutating,
            [AgenticToolRegistry.PythonExecuteToolName] = ToolCapabilities.Sandbox | ToolCapabilities.Mutating,
            [AgenticToolRegistry.NodeExecuteToolName] = ToolCapabilities.Sandbox | ToolCapabilities.Mutating,
            [AgenticToolRegistry.ContainerExecuteToolName] = ToolCapabilities.Sandbox | ToolCapabilities.Mutating,
            [SessionDiscoveryTools.ArtifactRead] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.ArtifactTail] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.SkillRead] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.SkillSearch] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.RuleRead] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.RuleSearch] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.ToolDescribe] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.ToolSearch] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.SessionLogSearch] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.DelegateTask] = ToolCapabilities.Discovery,
            [SessionDiscoveryTools.TodoWrite] = ToolCapabilities.Discovery | ToolCapabilities.Mutating,
            [AgenticHttpTools.FetchUrl] = ToolCapabilities.Http,
            [AgenticHttpTools.HttpRequest] = ToolCapabilities.Http,
            [AgenticHttpTools.WebSearch] = ToolCapabilities.Http | ToolCapabilities.QueryFocused,
        });
    }
}
