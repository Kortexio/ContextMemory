using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Prompts;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests.Harness;

public sealed class HarnessModeOffTests
{
    [Fact]
    public void ParseHarnessMode_accepts_off_aliases()
    {
        Assert.Equal(ModelHarnessMode.Off, LlmCapabilitiesResolver.ParseHarnessMode("off"));
        Assert.Equal(ModelHarnessMode.Off, LlmCapabilitiesResolver.ParseHarnessMode("none"));
        Assert.Equal(ModelHarnessMode.Off, LlmCapabilitiesResolver.ParseHarnessMode("disabled"));
    }

    [Fact]
    public void Profile_From_off_disables_recovery_finish_gate_and_continue()
    {
        var config = new AppRuntimeConfig
        {
            AppId = "bench",
            LlmModel = "qwen3.6-35b",
            Agentic = new AgenticConfig
            {
                Enabled = true,
                HarnessMode = "off",
                PromptProfile = "qwen"
            }
        };

        var profile = ModelHarnessProfile.From(config);
        Assert.Equal(ModelHarnessMode.Off, profile.Mode);
        Assert.Equal(0, profile.MaxRefunds);
        Assert.Equal(0, profile.MaxContinueExtensions);
        Assert.Equal(0, profile.MaxFinishNudges);
        Assert.False(profile.EnableEvidenceFinishGate);
        Assert.Empty(profile.FamilyIds);

        var caps = LlmCapabilitiesResolver.From(config);
        Assert.Equal(ModelHarnessMode.Off, caps.HarnessMode);
        Assert.False(caps.EnableProseToolCallPromotion);
        // Adversarial: Off keeps the hard Qwen client-side wire without lifts.
        Assert.True(caps.PreferClientSideToolParsing);
    }
}
