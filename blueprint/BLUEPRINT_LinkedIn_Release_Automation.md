# BLUEPRINT — Divulgação automática de releases no LinkedIn (ContextMemory / Kortexio)

**Target editor:** Cursor.ai
**Runtime:** .NET 9 console tool + GitHub Actions
**Author URN:** perfil pessoal (`w_member_social`) — troca para organização (`w_organization_social`) na secção 9 se quiseres postar pela página do Kortexio.

> **Como usar este blueprint no Cursor:** abre o repositório no Cursor, cria um ficheiro `AGENT_TASK.md` com o conteúdo da secção 1, e depois vai colando cada secção como prompt sequencial no chat do Cursor (Cmd+L). Cada secção é auto-contida e diz ao agente exatamente que ficheiro criar. Executa e valida secção a secção — não peças tudo de uma vez.

---

## 0. Fatos verificados (contexto para o agente, não gerar código aqui)

- Endpoint único: `POST https://api.linkedin.com/rest/posts`. Mesmo endpoint para perfil e página; só muda o `author` URN e o scope.
- Scope pessoal: `w_member_social`. Scope página: `w_organization_social` (exige ser admin da página + app verificado).
- Headers obrigatórios: `LinkedIn-Version: YYYYMM`, `X-Restli-Protocol-Version: 2.0.0`, `Authorization: Bearer <token>`, `Content-Type: application/json`.
- Tokens: access token expira em ~60 dias; refresh token dura ~365 dias.
- Rate limit: ~100 posts/dia por membro autenticado.
- Não há edição via API — para corrigir, apaga e recria.
- Não suporta: artigos long-form, newsletters, PDF carousels, polls. Suporta: texto, imagem, vídeo, multi-imagem, link share.
- A resposta devolve o URN do post no header `x-restli-id`.

**Payload mínimo de um post de texto (perfil pessoal):**
```json
{
  "author": "urn:li:person:{PERSON_ID}",
  "commentary": "texto do post",
  "visibility": "PUBLIC",
  "distribution": {
    "feedDistribution": "MAIN_FEED",
    "targetEntities": [],
    "thirdPartyDistributionChannels": []
  },
  "lifecycleState": "PUBLISHED",
  "isReshareDisabledByAuthor": false
}
```

---

## 1. AGENT_TASK.md (cola isto como primeiro contexto no Cursor)

```
# Task: LinkedIn release-announcer para ContextMemory

Constrói uma ferramenta .NET 9 (console) chamada `LinkedInAnnouncer` que:
1. Lê o corpo de uma GitHub Release (via env vars ou args).
2. Formata um post de LinkedIn a partir das release notes.
3. Publica no perfil do autor via LinkedIn Posts API (/rest/posts).
4. É invocada por um GitHub Action no evento `release: published`.

Também constrói:
- Um utilitário one-shot de OAuth (`GetToken`) que corre localmente para obter
  o primeiro access+refresh token.
- Um passo de refresh de token automático no próprio announcer, para não expirar.

Stack: .NET 9, System.Net.Http, System.Text.Json. Sem libs de terceiros exceto
as do BCL. Segue Conventional Commits. Segredos vêm de env vars, nunca hardcoded.
```

---

## 2. Estrutura de projeto a criar

```
tools/linkedin-announcer/
├── LinkedInAnnouncer.csproj
├── Program.cs                 # entrypoint: modo "post" e modo "get-token"
├── LinkedInClient.cs          # wrapper HTTP do Posts API
├── OAuthHelper.cs             # authorization-code flow + refresh
├── PostFormatter.cs           # release notes -> texto do post
├── Models.cs                  # DTOs (TokenResponse, PostPayload, etc.)
└── README.md                  # instruções de setup
.github/workflows/announce-linkedin.yml
```

---

## 3. Prompt Cursor — criar o .csproj

```
Cria tools/linkedin-announcer/LinkedInAnnouncer.csproj como uma app console
.NET 9, nullable enabled, implicit usings enabled, sem dependências NuGet
externas. RootNamespace LinkedInAnnouncer.
```

Resultado esperado:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>LinkedInAnnouncer</RootNamespace>
  </PropertyGroup>
</Project>
```

---

## 4. Prompt Cursor — Models.cs

```
Cria Models.cs com records para:
- TokenResponse: access_token, expires_in, refresh_token, refresh_token_expires_in,
  scope, token_type (usa JsonPropertyName para mapear snake_case).
- Distribution: FeedDistribution ("MAIN_FEED"), TargetEntities (array vazio),
  ThirdPartyDistributionChannels (array vazio).
- PostPayload: Author, Commentary, Visibility ("PUBLIC"), Distribution,
  LifecycleState ("PUBLISHED"), IsReshareDisabledByAuthor (false).
Usa System.Text.Json com JsonPropertyName em camelCase conforme a API do LinkedIn.
```

---

## 5. Prompt Cursor — OAuthHelper.cs (obter e renovar token)

```
Cria OAuthHelper.cs com:

1. Método estático GetAuthorizationUrl(clientId, redirectUri, scopes):
   constrói https://www.linkedin.com/oauth/v2/authorization com
   response_type=code, client_id, redirect_uri, scope (space-separated),
   e um state aleatório.

2. Método async ExchangeCodeForToken(httpClient, code, clientId, clientSecret,
   redirectUri): POST para https://www.linkedin.com/oauth/v2/accessToken
   com grant_type=authorization_code, form-urlencoded. Devolve TokenResponse.

3. Método async RefreshToken(httpClient, refreshToken, clientId, clientSecret):
   POST para o mesmo endpoint com grant_type=refresh_token. Devolve TokenResponse.

4. Método async GetPersonUrn(httpClient, accessToken): GET
   https://api.linkedin.com/v2/userinfo (OpenID) com Bearer token,
   lê o campo "sub" e devolve "urn:li:person:{sub}".

Todos os endpoints OAuth usam form-urlencoded, não JSON.
```

---

## 6. Prompt Cursor — LinkedInClient.cs

```
Cria LinkedInClient.cs com um método async PublishPost(httpClient, accessToken,
authorUrn, commentary):
- POST para https://api.linkedin.com/rest/posts
- Headers: Authorization Bearer, LinkedIn-Version com o mês atual em formato
  YYYYMM (DateTime.UtcNow.ToString("yyyyMM")), X-Restli-Protocol-Version 2.0.0,
  Content-Type application/json.
- Body: serializa um PostPayload com o authorUrn e commentary.
- Se a resposta não for 2xx, lança exceção com o status e o corpo da resposta.
- Se for sucesso, lê o header "x-restli-id" e devolve-o como URN do post.
Faz log do URN publicado.
```

---

## 7. Prompt Cursor — PostFormatter.cs

```
Cria PostFormatter.cs com um método FormatReleasePost(tagName, releaseBody, repoUrl):
- Limita o corpo final a ~2800 caracteres (limite prático de post do LinkedIn é 3000).
- Estrutura:
    "ContextMemory {tagName} is out. 🚀"
    linha em branco
    "What changed & why it matters:"
    linha em branco
    {releaseBody resumido — remove markdown headings ###, converte "- " em "• "}
    linha em branco
    "Full notes: {repoUrl}/releases/tag/{tagName}"
    linha em branco
    "#dotnet #opensource #AI #LLM #agents #MCP"
- Se o corpo exceder o limite, trunca e adiciona "…" antes das hashtags.
Escreve o método puro (sem I/O) para ser testável.
```

---

## 8. Prompt Cursor — Program.cs (entrypoint com dois modos)

```
Cria Program.cs com dois modos, selecionados pelo primeiro argumento:

MODO "get-token" (corre localmente, uma vez):
- Lê LINKEDIN_CLIENT_ID, LINKEDIN_CLIENT_SECRET das env vars.
- redirect_uri = "http://localhost:8000/callback".
- Imprime a URL de autorização (OAuthHelper.GetAuthorizationUrl) com scopes
  "openid profile w_member_social".
- Abre um HttpListener em localhost:8000, espera o callback, extrai o "code".
- Troca por token (ExchangeCodeForToken), obtém o person URN (GetPersonUrn).
- Imprime no stdout: LINKEDIN_ACCESS_TOKEN, LINKEDIN_REFRESH_TOKEN,
  LINKEDIN_PERSON_URN — com instrução para guardar como GitHub Secrets.

MODO "post" (corre no CI):
- Lê env vars: LINKEDIN_REFRESH_TOKEN, LINKEDIN_CLIENT_ID,
  LINKEDIN_CLIENT_SECRET, LINKEDIN_PERSON_URN, RELEASE_TAG, RELEASE_BODY,
  REPO_URL.
- Chama RefreshToken para obter um access token fresco (nunca depende do
  access token guardado, que pode ter expirado).
- Formata o post (PostFormatter).
- Publica (LinkedInClient.PublishPost).
- Se DRY_RUN=true, só imprime o texto formatado e não publica.
- Exit code 0 em sucesso, 1 em falha, com mensagem clara.

Usa um único HttpClient partilhado. Trata exceções no topo.
```

---

## 9. (Opcional) Modo página do Kortexio

Para postar pela página em vez do perfil, no Cursor pede:

```
Adiciona suporte a página de organização:
- Nova env var LINKEDIN_ORG_URN (formato urn:li:organization:{id}).
- Se LINKEDIN_ORG_URN estiver definida, usa-a como author em vez do person URN.
- O scope no get-token passa a incluir "w_organization_social".
- Aviso: w_organization_social exige app verificado e o produto
  "Community Management API" aprovado no portal — pode ter review manual.
```

---

## 10. GitHub Action — .github/workflows/announce-linkedin.yml

```
Cria .github/workflows/announce-linkedin.yml:
- Trigger: release published.
- Job em ubuntu-latest com actions/checkout e actions/setup-dotnet (9.0.x).
- Step "post": dotnet run --project tools/linkedin-announcer -- post
  com env vars mapeadas dos secrets e do github.event.release:
    LINKEDIN_CLIENT_ID: secrets.LINKEDIN_CLIENT_ID
    LINKEDIN_CLIENT_SECRET: secrets.LINKEDIN_CLIENT_SECRET
    LINKEDIN_REFRESH_TOKEN: secrets.LINKEDIN_REFRESH_TOKEN
    LINKEDIN_PERSON_URN: secrets.LINKEDIN_PERSON_URN
    RELEASE_TAG: github.event.release.tag_name
    RELEASE_BODY: github.event.release.body
    REPO_URL: github.event.repository.html_url
- Adiciona um input workflow_dispatch com DRY_RUN default "true" para testar
  sem publicar.
```

Esboço de referência:
```yaml
name: announce-linkedin
on:
  release:
    types: [published]
  workflow_dispatch:
    inputs:
      dry_run:
        default: "true"
jobs:
  post:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - name: Publish announcement
        env:
          LINKEDIN_CLIENT_ID: ${{ secrets.LINKEDIN_CLIENT_ID }}
          LINKEDIN_CLIENT_SECRET: ${{ secrets.LINKEDIN_CLIENT_SECRET }}
          LINKEDIN_REFRESH_TOKEN: ${{ secrets.LINKEDIN_REFRESH_TOKEN }}
          LINKEDIN_PERSON_URN: ${{ secrets.LINKEDIN_PERSON_URN }}
          RELEASE_TAG: ${{ github.event.release.tag_name }}
          RELEASE_BODY: ${{ github.event.release.body }}
          REPO_URL: ${{ github.event.repository.html_url }}
          DRY_RUN: ${{ github.event.inputs.dry_run || 'false' }}
        run: dotnet run --project tools/linkedin-announcer -- post
```

---

## 11. Setup manual (uma vez) — fazer no browser/portal

1. **Criar o app**: developer.linkedin.com → Create app → vincula a uma Company Page (usa a do Kortexio; se não existir, cria uma placeholder — precisas de ser admin).
2. **Verificar o app**: separador Settings → botão de verificação → aprova pela página.
3. **Adicionar produtos**: separador Products →
   - "Sign In with LinkedIn using OpenID Connect"
   - "Share on LinkedIn"
   (ambos self-serve, ativam sem review para uso pessoal)
4. **Auth tab**: adiciona `http://localhost:8000/callback` aos Authorized redirect URLs. Confirma que os scopes `openid`, `profile`, `w_member_social` aparecem.
5. **Copia** Client ID e Client Secret.
6. **Corre o get-token localmente**:
   ```bash
   export LINKEDIN_CLIENT_ID=xxx
   export LINKEDIN_CLIENT_SECRET=yyy
   dotnet run --project tools/linkedin-announcer -- get-token
   ```
   Abre a URL impressa, autoriza, e o tool imprime os 3 valores.
7. **Guarda como GitHub Secrets** (Settings → Secrets and variables → Actions):
   `LINKEDIN_CLIENT_ID`, `LINKEDIN_CLIENT_SECRET`, `LINKEDIN_REFRESH_TOKEN`,
   `LINKEDIN_PERSON_URN`.

---

## 12. Testar antes de publicar a sério

1. **Dry run local**:
   ```bash
   export DRY_RUN=true
   export RELEASE_TAG=v0.0.0-test
   export RELEASE_BODY="- feat: teste de formatação\n- fix: outro item"
   export REPO_URL=https://github.com/teu-user/contextmemory
   # + as env vars de token
   dotnet run --project tools/linkedin-announcer -- post
   ```
   Deves ver o texto formatado, sem publicar.
2. **Dry run no CI**: Actions → announce-linkedin → Run workflow → dry_run=true.
3. **Publicação real**: cria uma release de teste (podes apagar o post depois manualmente no LinkedIn).

---

## 13. Manutenção e gotchas

- **Refresh token expira em 365 dias.** O announcer usa o refresh token a cada release, o que renova o *access* token mas NÃO estende o refresh. Marca no calendário: re-correr `get-token` uma vez por ano. (Opcional: um cron mensal que faz refresh e reescreve o secret via API do GitHub mantém tudo vivo — pede ao Cursor se quiseres esse extra.)
- **Sem edição via API**: se o post sair com erro, apaga manualmente e re-dispara o workflow (workflow_dispatch).
- **429 = rate limit**: improvável com releases esporádicas, mas o cliente deve tratar e falhar com mensagem clara.
- **403 Forbidden**: quase sempre scope errado (`w_member_social` vs `w_organization_social`) ou author URN != utilizador do token.
- **Markdown**: o LinkedIn não renderiza markdown. O PostFormatter já limpa `###` e converte bullets — confirma que não passam `**` nem links markdown `[]()`.

---

## 14. Ordem de execução recomendada

1. Secções 3→8 no Cursor (constrói o tool).
2. `dotnet build` local — corrige o que o compilador apontar.
3. Secção 11 (setup do app no portal).
4. `get-token` local → guarda secrets.
5. Dry run local (secção 12.1).
6. Secção 10 (workflow) + dry run no CI (12.2).
7. Release de teste real (12.3).
8. Ligar ao teu fluxo de release-please existente (o evento `release: published` já cobre releases criadas pelo release-please).
