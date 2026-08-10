using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticGibberishGuardrail
{
    public static bool TryGetRejectionFeedback(
        string finalAnswer,
        string configJson,
        AppRuntimeConfig runtimeConfig,
        out string feedback)
    {
        feedback = string.Empty;
        if (string.IsNullOrWhiteSpace(finalAnswer))
            return false;

        var text = finalAnswer.Trim();
        if (text.Length < 24)
            return false;

        var letterCount = text.Count(char.IsLetter);
        var spaceCount = text.Count(char.IsWhiteSpace);
        var letterRatio = letterCount / (double)text.Length;

        var maxRun = 1;
        var run = 1;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == text[i - 1])
            {
                run++;
                if (run > maxRun)
                    maxRun = run;
            }
            else
            {
                run = 1;
            }
        }

        var looksGibberish =
            (text.Length >= 40 && spaceCount == 0 && letterRatio > 0.5)
            || maxRun >= 12
            || (text.Length >= 80 && letterRatio < 0.25);

        if (!looksGibberish)
            return false;

        feedback = AgenticGuardrailConfigReader.GetFeedback(configJson, runtimeConfig.DefaultLanguage)
            ?? TenantLocale.Select(
                runtimeConfig.DefaultLanguage,
                "Rejected: answer looks like gibberish.",
                "Rejeitado: a resposta parece sem sentido.");
        return true;
    }
}
