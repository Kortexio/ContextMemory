using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;
using ContextMemory.Core.Session;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Engine;

public sealed class SystemPromptBuilder : ISystemPromptBuilder
{
    private readonly ITelemetryCollector _telemetry;
    private readonly ContextMemoryOptions _options;

    public SystemPromptBuilder(ITelemetryCollector telemetry, IOptions<ContextMemoryOptions> options)
    {
        _telemetry = telemetry;
        _options = options.Value;
    }

    public string Build(
        string appId,
        AppRuntimeConfig config,
        SessionSnapshot snapshot,
        string? userQuery,
        string? webContextMarkdown = null)
    {
        var lang = config.DefaultLanguage;
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(config.BasePersona))
            parts.Add(config.BasePersona.Trim());
        if (!string.IsNullOrWhiteSpace(config.BusinessRules))
            parts.Add(TenantLocale.Select(lang, "## Business rules\n", "## Regras de negócio\n") + config.BusinessRules.Trim());
        if (!string.IsNullOrWhiteSpace(config.FormatRules))
            parts.Add(TenantLocale.Select(lang, "## Format\n", "## Formato\n") + config.FormatRules.Trim());

        var budget = SessionWikiSettings.ResolveMaxWikiContextChars(config, _options);
        var compiled = SessionWikiCompiler.Compile(snapshot, userQuery, budget);
        _telemetry.RecordWikiContext(
            appId,
            compiled.CharCount,
            compiled.IncludedPages,
            compiled.TotalPages,
            compiled.Truncated);

        var hasWebContext = !string.IsNullOrWhiteSpace(webContextMarkdown);
        parts.Add(
            TenantLocale.Select(
                lang,
                "## Compiled session memory (wiki)\n"
                + "The wiki holds compiled knowledge (facts, decisions) — it may be outdated. "
                + "Tag wiki facts with `[wiki]`. "
                + (hasWebContext
                    ? "For freshness or the period the user asked about, **ignore** the wiki and use only the «Web search» section. "
                    : string.Empty)
                + "Recent dialogue is in the user/assistant messages in this request — "
                + "do not assume missing history just because the index has no pages yet.\n\n"
                + compiled.Content,
                "## Memória compilada desta sessão (wiki)\n"
                + "A wiki contém conhecimento compilado (factos, decisões) — pode estar desactualizada. "
                + "Etiqueta factos da wiki com `[wiki]`. "
                + (hasWebContext
                    ? "Para actualidade ou o período pedido pelo utilizador, **ignora** a wiki e usa só a secção «Pesquisa web». "
                    : string.Empty)
                + "O diálogo recente está nas mensagens user/assistant deste pedido — "
                + "não concluas ausência de histórico só porque o índice ainda não tem páginas.\n\n"
                + compiled.Content));

        if (hasWebContext)
        {
            parts.Add(webContextMarkdown!.Trim());
            parts.Add(
                TenantLocale.Select(
                    lang,
                    "## Source priority\n"
                    + "1. **Freshness / requested period** → web search excerpts only.\n"
                    + "2. **Historical context or session topics** → wiki `[wiki]`, if relevant and **after** answering the freshness question.\n"
                    + "3. If the web does not cover the requested period, say so assertively — **do not** fill gaps with wiki history.",
                    "## Prioridade de fontes\n"
                    + "1. **Actualidade / período pedido** → só excertos da «Pesquisa web».\n"
                    + "2. **Contexto histórico ou temas da sessão** → wiki `[wiki]`, se relevante e **depois** de responder à pergunta de actualidade.\n"
                    + "3. Se a web não cobrir o período pedido, responde isso de forma assertiva — **não** preenchas com histórico da wiki."));
        }

        if (snapshot.Messages.Count > 0)
        {
            parts.Add(
                TenantLocale.Select(
                    lang,
                    $"_Recent history in this session: {snapshot.Messages.Count} message(s) included below this system prompt._",
                    $"_Histórico recente nesta sessão: {snapshot.Messages.Count} mensagem(ns) incluída(s) abaixo deste system prompt._"));
        }

        return string.Join("\n\n", parts);
    }
}
