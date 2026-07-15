namespace ContextMemory.Core.Localization;

/// <summary>
/// LLM-facing prompt templates (wiki maintainer, freshness classifier, agent judge).
/// English is the default; Portuguese variants apply when <c>language</c> is pt-*.
/// </summary>
public static class LlmPrompts
{
    public static string FreshnessClassifier(string? language) =>
        TenantLocale.Select(
            language,
            """
            Decide whether the user question requires a web search for current facts not covered by the session wiki.

            WIKI PAGES (titles):
            {wikiPages}

            QUESTION:
            {userQuery}

            Respond ONLY with valid JSON:
            {{
              "needs_web_search": true or false,
              "search_query": "search-engine-optimized query in English or the user's language, or null"
            }}

            Rules:
            - true if it asks for news, recent legislation, current state, fact-checking after 2024, or real-time data
            - false if it is consolidated history, opinion, or answerable from wiki/conversation history alone
            - search_query must be short and specific when needs_web_search is true
            """,
            """
            Decide se a pergunta do utilizador requer pesquisa na web para factos actuais não cobertos pela wiki da sessão.

            PÁGINAS DA WIKI (títulos):
            {wikiPages}

            PERGUNTA:
            {userQuery}

            Responde APENAS com JSON válido:
            {{
              "needs_web_search": true ou false,
              "search_query": "query optimizada para motor de busca em inglês ou português, ou null"
            }}

            Regras:
            - true se pede notícias, legislação recente, estado actual, validação de factos após 2024, ou dados em tempo real
            - false se é história consolidada, opinião, ou responde só com a wiki/histórico da conversa
            - search_query deve ser curta e específica quando needs_web_search é true
            """);

    public static string WikiMaintainer(string? language) =>
        TenantLocale.Select(
            language,
            """
            Update the markdown wiki for this chat session based on the latest exchange.

            WIKI SCHEMA:
            {schema}

            CURRENT INDEX:
            {index}

            CURRENT PAGES:
            {pages}

            LATEST EXCHANGE:
            [USER]: {userMessage}
            [ASSISTANT]: {assistantMessage}
            {webSearchSection}

            Respond ONLY with valid JSON (no markdown wrapper, no extra text):
            {{
              "log_entry": "## [YYYY-MM-DD HH:mm] turn | short summary",
              "index_md": "full updated index.md content or null if unchanged",
              "pages": [
                {{ "path": "pages/name.md", "content": "full page markdown" }}
              ]
            }}

            Rules:
            - Always include log_entry summarizing the turn
            - If there is no new reusable knowledge, pages may be []
            - Every index_md entry MUST have a matching page in pages[] (same path) in the same JSON
            - Never reference files in index_md without full content in pages[]
            - index_md only when the index changes; use normalized paths like pages/topic_name.md
            - Maximum 3 pages per turn
            - If web results exist, merge verifiable facts into thematic pages with URL and retrieval date
            - Language: {language}
            """,
            """
            Actualiza a wiki markdown desta sessão de chat com base na última troca.

            SCHEMA DA WIKI:
            {schema}

            ÍNDICE ACTUAL:
            {index}

            PÁGINAS ACTUAIS:
            {pages}

            ÚLTIMA TROCA:
            [USER]: {userMessage}
            [ASSISTANT]: {assistantMessage}
            {webSearchSection}

            Responde APENAS com JSON válido (sem markdown, sem texto extra):
            {{
              "log_entry": "## [YYYY-MM-DD HH:mm] turno | resumo curto",
              "index_md": "conteúdo completo actualizado do index.md ou null se inalterado",
              "pages": [
                {{ "path": "pages/nome.md", "content": "markdown completo da página" }}
              ]
            }}

            Regras:
            - Sempre inclui log_entry com resumo do turno
            - Se não houver conhecimento novo reutilizável, pages pode ser []
            - Cada entrada em index_md DEVE ter página correspondente em pages[] no mesmo JSON (mesmo path)
            - Nunca referencies ficheiros em index_md sem incluir o content completo em pages[]
            - index_md só quando o índice mudar; use paths normalizados tipo pages/nome_tema.md
            - Máximo 3 páginas por turno
            - Se houver resultados web, faz merge de factos verificáveis nas páginas temáticas com URL e data de consulta
            - Língua: {language}
            """);

    public static string WikiCompactor(string? language) =>
        TenantLocale.Select(
            language,
            """
            Compact the markdown wiki for this chat session — merge redundant knowledge.

            CURRENT INDEX:
            {index}

            CURRENT PAGES:
            {pages}

            Respond ONLY with valid JSON (no markdown wrapper, no extra text):
            {{
              "log_entry": "## [YYYY-MM-DD HH:mm] compaction | summary N→M pages",
              "index_md": "full updated index.md content",
              "pages": [
                {{ "path": "pages/name.md", "content": "full merged page markdown" }}
              ],
              "delete_pages": ["pages/old.md"]
            }}

            Rules:
            - Merge pages about the same topic
            - Create pages/summary-general.md with stable decisions and facts when useful
            - delete_pages lists files in pages/ that became obsolete after the merge
            - Keep the wiki concise and free of duplication
            - Language: {language}
            """,
            """
            Compacta a wiki markdown desta sessão de chat — funde conhecimento redundante.

            ÍNDICE ACTUAL:
            {index}

            PÁGINAS ACTUAIS:
            {pages}

            Responde APENAS com JSON válido (sem markdown, sem texto extra):
            {{
              "log_entry": "## [YYYY-MM-DD HH:mm] compactação | resumo N→M páginas",
              "index_md": "conteúdo completo actualizado do index.md",
              "pages": [
                {{ "path": "pages/nome.md", "content": "markdown completo da página fundida" }}
              ],
              "delete_pages": ["pages/antiga.md"]
            }}

            Regras:
            - Funde páginas sobre o mesmo tópico
            - Cria pages/resumo-geral.md com decisões e factos estáveis quando útil
            - delete_pages lista ficheiros em pages/ que ficaram obsoletos após o merge
            - Mantém a wiki concisa e sem duplicação
            - Língua: {language}
            """);

    public static string NoneLabel(string? language) =>
        TenantLocale.Select(language, "(none)", "(nenhuma)");

    public static string EmptyLabel(string? language) =>
        TenantLocale.Select(language, "(empty)", "(vazio)");

    public static string InsufficientBudgetPages(int count, IReadOnlyList<string> keys, string? language) =>
        TenantLocale.Select(
            language,
            $"(insufficient budget for pages — {count} on disk: {string.Join(", ", keys)})",
            $"(orçamento insuficiente para páginas — {count} em disco: {string.Join(", ", keys)})");

    public static string NoPagesInBudget(IReadOnlyList<string> keys, string? language) =>
        TenantLocale.Select(
            language,
            $"(no pages included in budget — on-disk list: {string.Join(", ", keys)})",
            $"(nenhuma página incluída no orçamento — lista em disco: {string.Join(", ", keys)})");

    public static string WikiMaintainerInvalidJson(string userSnippet, string? language) =>
        TenantLocale.Select(
            language,
            $"turn | (invalid JSON — user: {userSnippet})",
            $"turno | (JSON inválido — user: {userSnippet})");

    public static string WikiMaintainerCompileFailed(string? language) =>
        TenantLocale.Select(
            language,
            "turn | (failed to compile wiki)",
            "turno | (falha ao compilar wiki)");

    public static string WikiLogDefaultSuffix(string? language) =>
        TenantLocale.Select(language, "turn logged", "turno registado");
}
