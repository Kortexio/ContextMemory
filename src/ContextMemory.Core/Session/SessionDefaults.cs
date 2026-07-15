namespace ContextMemory.Core.Session;

public static class SessionDefaults
{
    public const string DefaultSchema = """
        # Session wiki schema

        Maintains one markdown wiki per chat session (LLM Wiki / Karpathy pattern).

        ## Structure
        - `index.md` — page catalog with links and one-line summaries
        - `log.md` — append-only turn chronology
        - `pages/*.md` — compiled knowledge (decisions, facts, procedures, context)

        ## Rules
        - Update the wiki after each user/assistant exchange
        - Generalize reusable facts; remove transient personal references
        - Do not duplicate pages — merge into the same file when the topic already exists
        - Useful assistant answers may become new pages
        - Use the tenant `DefaultLanguage` unless instructed otherwise
        - Web-sourced facts must include `> Source: [title](url) | retrieved: YYYY-MM-DD` at the top of the page or next to the fact
        - Pages updated from web search must have `> Last verified: YYYY-MM-DD | Sources: ...`
        - Do not invent facts beyond the web excerpts provided in the turn
        """;

    public const string EmptyIndex = """
        # Session index

        _No pages yet. Knowledge from this conversation will appear here._
        """;

    public const string EmptyLog = """
        # Session log

        _Turn chronology._
        """;
}
