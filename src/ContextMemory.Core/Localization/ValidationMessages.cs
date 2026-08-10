using ContextMemory.Core.Models;

namespace ContextMemory.Core.Localization;

public static class ValidationMessages
{
    public static string EmptyFinalAnswer(AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            "The final answer is empty. Provide a complete response to the user.",
            "A resposta final está vazia. Fornece uma resposta completa ao utilizador.");

    public static string TooShort(int minLength, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"The answer is too short (minimum {minLength} characters). Add more relevant detail.",
            $"A resposta é demasiado curta (mínimo {minLength} caracteres). Desenvolve a resposta com mais detalhe relevante.");

    public static string BlockedContent(string pattern, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"The answer contains content blocked by the tenant guardrail ('{pattern}'). Rephrase without it.",
            $"A resposta contém conteúdo bloqueado pelo guardrail do tenant ('{pattern}'). Reformula sem esse conteúdo.");

    public static string ToolsFailedExitCode(string toolList, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"One or more tools finished with exit code != 0 ({toolList}). Fix the issue or explain the error clearly.",
            $"Uma ou mais tools terminaram com exit code != 0 ({toolList}). Corrige o problema ou explica claramente o erro ao utilizador.");

    public static string ToolsFailedNotMentioned(string toolList, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"One or more tools failed ({toolList}) but the answer does not mention the error. Explain what went wrong and what the user can do.",
            $"Uma ou mais tools falharam ({toolList}) mas a resposta não menciona o erro. Explica o que correu mal e o que o utilizador pode fazer.");

    public static string PatternMismatch(string pattern, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"The final answer does not match the tenant's expected pattern (`{pattern}`). Adjust the content to meet the agreed format.",
            $"A resposta final não corresponde ao padrão esperado do tenant (`{pattern}`). Ajusta o conteúdo para cumprir o formato acordado.");

    public static string ConfirmationRequired(string keyword, AppRuntimeConfig config) =>
        TenantLocale.Select(
            config.DefaultLanguage,
            $"The action related to '{keyword}' requires human confirmation. Ask for explicit user confirmation before proceeding.",
            $"A ação relacionada com '{keyword}' requer confirmação humana. Pede confirmação explícita ao utilizador antes de prosseguir.");

    public static string FabricatedSandboxLimitation(string feedback, AppRuntimeConfig config) =>
        string.IsNullOrWhiteSpace(feedback)
            ? TenantLocale.Select(
                config.DefaultLanguage,
                "Rejected fabricated sandbox limitation. Call real tools instead.",
                "Rejeitada limitação inventada do sandbox. Chama as tools reais.")
            : feedback;

    public static string UrlDescribedWithoutFetch(string feedback, AppRuntimeConfig config) =>
        string.IsNullOrWhiteSpace(feedback)
            ? TenantLocale.Select(
                config.DefaultLanguage,
                "Rejected: website described without fetching it. Call tools first.",
                "Rejeitado: site descrito sem o ir buscar. Chama tools primeiro.")
            : feedback;

    public static string LiveDataWithoutEvidence(string feedback, AppRuntimeConfig config) =>
        string.IsNullOrWhiteSpace(feedback)
            ? TenantLocale.Select(
                config.DefaultLanguage,
                "Rejected: live-data answer without MCP/wiki evidence. Emit tool_calls; do not invent.",
                "Rejeitado: resposta de dados live sem evidência MCP/wiki. Emite tool_calls; não inventes.")
            : feedback;

    public static string ToolIntentNarration(string feedback, AppRuntimeConfig config) =>
        string.IsNullOrWhiteSpace(feedback)
            ? TenantLocale.Select(
                config.DefaultLanguage,
                "Rejected: narrated tool intent instead of emitting tool_calls. Call tools now.",
                "Rejeitado: narraste a intenção de usar tools em vez de emitir tool_calls. Chama as tools agora.")
            : feedback;
}
