# Blueprint 01 — ContextMemory Next Generation

## 1. Propósito

Este blueprint define a evolução do **ContextMemory** para se tornar um runtime agentivo generalista e, ao mesmo tempo, uma fundação de primeira classe para coding agents.

A direção estratégica é preservar o ContextMemory como **aplicação/serviço independente**, consumível por IDE, CLI, CI/CD, automações e agentes externos, em vez de incorporar o seu código diretamente no fork do Code - OSS.

O objetivo não é transformar o ContextMemory em um editor. O objetivo é transformá-lo em um **Agent Runtime + Context/Memory Platform**, capaz de operar com diferentes interfaces e diferentes modelos.

A arquitetura deve continuar favorecendo:
- memória persistente e auditável;
- descoberta dinâmica de contexto;
- agent loop com ferramentas;
- MCP;
- skills e regras;
- guardrails e HITL;
- sandbox e execução isolada;
- subagents;
- artifacts;
- abstração de modelos;
- operação local, self-hosted e cloud;
- integração especializada com coding agents.

---

## 2. Contexto e princípios de arquitetura

O ContextMemory já oferece uma base significativa: gateway OpenAI-compatible, session wiki, Global Wiki, rolling summaries, dynamic discovery, agentic loop, sandbox, MCP, skills, guardrails, HITL, artifacts, browser e subagents.

A documentação atual descreve uma arquitetura na qual o cliente continua usando uma API compatível com OpenAI, enquanto o gateway autentica o tenant, compõe memória de sessão, executa o loop agentivo e aplica skills, guardrails, validators e HITL. O projeto também mantém runtime separado para sandbox e MCP. 

O trabalho futuro deve seguir cinco princípios:

### 2.1 Separação de responsabilidades
ContextMemory não deve conhecer detalhes da UI de uma IDE. A IDE deve consumir capacidades do runtime por contratos estáveis.

### 2.2 Contexto sob demanda
O sistema deve preferir descoberta tardia e recuperação seletiva a “prompt stuffing”. O próprio projeto já adota esse princípio com wiki_search, wiki_grep, lazy tools, lazy skills, artifacts e compaction.

### 2.3 Memória legível
A memória deve continuar sendo auditável, editável e observável por humanos. Markdown/wiki continua sendo uma vantagem de produto, não um detalhe de implementação.

### 2.4 Modelo como dependência substituível
Nenhum componente do runtime deve ficar acoplado a um fornecedor específico. O runtime deve suportar modelos cloud, OpenAI-compatible, self-hosted e locais.

### 2.5 Segurança como parte do runtime
Filesystem, shell, network, browser e MCP são capacidades privilegiadas. Policies, approvals, sandbox e audit devem existir no núcleo do runtime.

---

## 3. Visão de produto

### ContextMemory deve evoluir para:

**Agent Runtime**
- loop agentivo;
- planejamento;
- execução de ferramentas;
- observação;
- validação;
- retry;
- compaction;
- subagents.

**Context Engine**
- working context;
- session context;
- project context;
- code context;
- retrieved context;
- artifact references.

**Memory Platform**
- session memory;
- project memory;
- temporal memory;
- long-term memories;
- summaries;
- decisions;
- learned facts;
- audit/history.

**Tool Platform**
- built-in tools;
- MCP;
- browser;
- sandbox;
- HTTP;
- filesystem;
- integrations.

**Policy Platform**
- rules;
- skills;
- hooks;
- guardrails;
- HITL;
- permission policies;
- network egress;
- secret handling.

---

# 4. Estado atual e diagnóstico

## 4.1 Componentes existentes a preservar

- OpenAI-compatible `/v1`
- tenant/app model
- session wiki
- Global Wiki
- rolling summary
- temporal revisions
- dynamic discovery
- FTS/lexical retrieval
- artifacts
- lazy tool schemas
- lazy skills
- rules
- hooks
- validation modes
- sandbox-runtime
- ACA dynamic sessions
- MCP runtime
- browser tools
- subagents
- HITL
- Admin/Playground
- SDK e tooling existentes

O projeto já documenta esses recursos como parte do mesmo pipeline de retrieval + agent loop.

## 4.2 Principais lacunas

As lacunas prioritárias para suportar uma IDE de nível Cursor são:

1. **Code Intelligence Layer**
2. **Project Brain**
3. **Memória de longo prazo mais estruturada**
4. **Context orchestration especializada em software**
5. **Agent lifecycle/state machine**
6. **First-class multi-agent orchestration**
7. **Model routing/policy**
8. **Observability/evaluation**
9. **IDE integration protocol**
10. **Security policy model mais granular**

---

# 5. Arquitetura-alvo

```text
Clients
 ├── Open Source AI IDE
 ├── CLI
 ├── CI/CD
 ├── Automation
 ├── Web App
 └── External Agent Client
           |
           v
     Agent API / Session API
           |
           v
   +-----------------------+
   |     ContextMemory     |
   |                       |
   | Agent Runtime         |
   | Context Engine        |
   | Memory Engine         |
   | Tool Runtime          |
   | Policy Engine         |
   | Artifact Service      |
   | Subagent Orchestrator |
   +-----------+-----------+
               |
      +--------+---------+
      |        |         |
      v        v         v
   Memory     MCP      Sandbox
      |        |         |
      v        v         v
  Wiki/DB   External   Execution
           Systems
               |
               v
          Model Gateway
               |
      +--------+---------+
      |        |         |
    Cloud    Local    Self-hosted
```

---

# 6. Nova camada: Code Memory

A maior evolução arquitetural recomendada é introduzir **Code Memory** sem substituir a memória wiki existente.

Code Memory deve representar o conhecimento estrutural sobre o repositório.

## 6.1 Entidades

- repository
- workspace
- file
- language
- module
- package
- symbol
- class
- method
- function
- interface
- type
- variable
- import
- export
- reference
- implementation
- call relationship
- inheritance relationship
- test
- diagnostic
- branch
- commit
- change
- pull request
- architecture decision

## 6.2 Relações

O sistema deve ser capaz de responder perguntas como:

- onde este símbolo é definido;
- quem chama este método;
- quem implementa esta interface;
- quais testes cobrem esta funcionalidade;
- quais módulos dependem deste pacote;
- quais arquivos mudaram neste branch;
- qual commit alterou determinado comportamento;
- qual PR introduziu determinada alteração.

## 6.3 Fontes de informação

Code Memory pode consumir:

- filesystem;
- AST;
- LSP;
- build metadata;
- tests;
- compiler diagnostics;
- Git;
- PR metadata;
- ADRs;
- project wiki;
- agent sessions.

---

# 7. Dois índices complementares

Não consolidar todo o conhecimento em uma única camada de embeddings.

## 7.1 Code Intelligence Index

Responsável por:
- sintaxe;
- AST;
- símbolos;
- referências;
- imports;
- chamadas;
- tipos;
- diagnostics;
- semantic search;
- lexical search.

## 7.2 Project Memory Index

Responsável por:
- decisões;
- documentação;
- regras;
- business knowledge;
- ADRs;
- agent discoveries;
- session knowledge;
- operational knowledge.

O Agent pode consultar os dois em uma mesma tarefa.

---

# 8. Context Orchestrator

Criar um componente explicitamente responsável por decidir:

- que contexto entra no prompt inicial;
- o que deve ser descoberto por tool;
- o que deve ser resumido;
- o que deve virar artifact;
- quando deve ocorrer compaction;
- quando buscar memória;
- quando buscar código;
- quando delegar para subagent.

## 8.1 Tipos de contexto

### Working context
Informação necessária para a tarefa imediata.

### Session context
O que já ocorreu durante a sessão.

### Project context
Conhecimento persistente do projeto.

### Code context
Estrutura e relações do código.

### External context
MCP, APIs, documentação, issues, tickets e serviços.

### Historical context
Git e memória temporal.

---

# 9. Evolução da memória

## 9.1 Working Memory

Deve conter:
- objetivo atual;
- plano;
- ferramentas recentemente utilizadas;
- últimos resultados relevantes;
- estado de validação;
- blockers.

## 9.2 Session Memory

Deve persistir:
- descobertas;
- decisões;
- arquivos importantes;
- hipóteses;
- tentativas falhadas;
- resultados;
- artifacts.

## 9.3 Project Memory

Deve armazenar:
- arquitetura;
- convenções;
- decisões;
- regras;
- padrões;
- business knowledge;
- histórico útil.

## 9.4 Long-term Agent Memory

Adicionar memória explícita de aprendizados reutilizáveis:
- preferências estáveis do projeto;
- soluções conhecidas;
- padrões de incidentes;
- integrações;
- procedimentos;
- pitfalls.

Essa memória deve exigir políticas claras para evitar armazenar segredos, dados pessoais ou fatos irrelevantes.

---

# 10. Context compression

A compressão existente deve evoluir para um mecanismo consciente de importância.

Cada item deve poder ser classificado como:
- crítico;
- importante;
- recuperável;
- descartável.

A compaction deve preservar:
- objetivo;
- decisões;
- estado;
- arquivos;
- riscos;
- resultados;
- blockers;
- referências para artifacts.

O resumo não deve tentar substituir integralmente os dados originais. Ele deve preservar ponte de recuperação.

---

# 11. Artifacts como memória operacional

Artifacts devem continuar sendo utilizados para:
- logs;
- resultados de testes;
- outputs de terminal;
- respostas MCP extensas;
- screenshots;
- relatórios;
- patches;
- traces.

O contexto principal deve receber apenas:
- preview;
- tipo;
- tamanho;
- origem;
- artifactId;
- resumo.

O Agent deve recuperar detalhes sob demanda.

---

# 12. Agent Runtime vNext

## 12.1 Estado do Agent

O lifecycle deve ter estados explícitos:

1. Created
2. Planning
3. Executing
4. WaitingForTool
5. WaitingForHuman
6. Observing
7. Validating
8. Recovering
9. Delegating
10. Compacting
11. Completed
12. Failed
13. Cancelled

## 12.2 Cada turn deve registrar

- input;
- context snapshot;
- model;
- tool selection;
- tool request;
- tool result;
- policy decision;
- human approval;
- validation;
- token usage;
- latency;
- outcome.

---

# 13. Subagents vNext

Evoluir o modelo atual de delegate_task para:

- hierarquia configurável;
- limite de profundidade;
- orçamento;
- modelos especializados;
- tools diferentes;
- filesystem isolado;
- worktree isolado;
- memória compartilhada controlada;
- resultados assíncronos;
- artifacts;
- cancelamento;
- observabilidade.

Tipos previstos:

- researcher;
- coder;
- tester;
- reviewer;
- debugger;
- documentation agent;
- security reviewer;
- migration agent.

O agent principal deve receber apenas o resultado necessário, e não todo o histórico bruto do subagent.

---

# 14. Skills e Rules vNext

Skills devem se tornar uma unidade de conhecimento operacional.

Cada skill deve possuir:
- identidade;
- descrição curta;
- escopo;
- gatilhos;
- instruções;
- referências;
- scripts opcionais;
- tools permitidos;
- modelo recomendado opcional.

As skills devem continuar carregando progressivamente.

Rules devem controlar:
- estilo;
- arquitetura;
- políticas;
- convenções;
- restrições;
- prioridades.

---

# 15. MCP vNext

O runtime deve tratar MCP como uma camada de ferramentas externa de primeira classe.

Requisitos:
- stdio;
- HTTP;
- OAuth;
- credenciais por tenant;
- catálogo;
- tool discovery;
- lazy schema;
- capability discovery;
- allow/deny;
- audit;
- rate limiting;
- timeouts;
- circuit breaker;
- health check.

Também deve existir uma distinção clara entre:
- MCP inbound: clientes falando com ContextMemory;
- MCP outbound: Agent usando servidores MCP.

---

# 16. Security Model vNext

Criar três políticas independentes.

## Context Policy
Determina o que pode entrar no contexto.

## Capability Policy
Determina que ferramentas o Agent pode usar.

## Execution Policy
Determina em que ambiente e sob quais condições a ferramenta pode executar.

Exemplo:

```text
Arquivo confidencial
→ fora do contexto

Git push
→ capability permitida

Git push em branch protegida
→ execução requer HITL
```

---

# 17. Secret management

Nunca tratar secrets como simples variáveis disponíveis ao Agent.

Criar classificação:

- public;
- internal;
- confidential;
- secret;
- restricted.

Tools devem receber apenas os secrets estritamente necessários.

O Agent nunca deve poder usar uma ferramenta para descobrir o catálogo de secrets.

---

# 18. Network egress

Egress deve ser política de runtime:

- deny all;
- allowlist;
- unrestricted.

O modo default deve ser restritivo para sandboxes.

Toda saída de rede deve registrar:
- host;
- porta;
- ferramenta;
- sessão;
- tenant;
- resultado.

---

# 19. Model Layer vNext

Criar uma abstração de modelo com:
- chat;
- tool calling;
- vision;
- streaming;
- embeddings;
- structured output;
- reasoning metadata quando disponível.

Adicionar Model Routing por:
- tarefa;
- custo;
- latência;
- capacidade;
- tenant policy;
- disponibilidade.

Exemplo conceitual:
- modelo rápido para autocomplete/triagem;
- modelo forte para planning;
- modelo local para tarefas sensíveis;
- modelo especializado para vision.

---

# 20. Observability

Criar uma visão de “Agent Trace” como entidade de produto.

O trace deve permitir analisar:

- prompt;
- retrieved context;
- tool calls;
- decisions;
- latency;
- errors;
- retries;
- compaction;
- subagents;
- final outcome.

Métricas fundamentais:
- task success rate;
- first-pass success;
- correction rate;
- tool error rate;
- context retrieval hit rate;
- unnecessary context ratio;
- token cost;
- task duration;
- approval frequency;
- sandbox failure rate.

---

# 21. Evaluation Framework

Criar uma suite de avaliação própria.

## Dataset

Casos de:
- code navigation;
- bug fixing;
- refactoring;
- feature addition;
- tests;
- documentation;
- architecture questions;
- historical reasoning.

## Experiments

Comparar:
- lexical only;
- semantic only;
- hybrid;
- memory off/on;
- code memory off/on;
- context compression off/on;
- single-agent vs subagents.

## KPI central

Não medir apenas “resposta correta”.

Medir:
- tarefa realmente concluída;
- mudança aceita;
- testes passando;
- número de correções;
- tempo;
- custo;
- segurança.

---

# 22. Fases de implementação

## Fase CM-1 — Stabilization

Objetivo:
tornar as APIs atuais estáveis.

Entregas:
- contratos versionados;
- session lifecycle;
- agent lifecycle;
- error taxonomy;
- trace model;
- API documentation;
- backward compatibility.

Critério de saída:
clients podem operar sem depender de detalhes internos.

## Fase CM-2 — Context Engine

Entregas:
- context budget;
- importance scoring;
- retrieval planner;
- artifact references;
- improved compaction;
- context telemetry.

## Fase CM-3 — Code Memory

Entregas:
- repository model;
- AST integration;
- symbol index;
- reference index;
- Git intelligence;
- hybrid code retrieval.

## Fase CM-4 — Agent Runtime vNext

Entregas:
- explicit state machine;
- resumable runs;
- cancellation;
- retries;
- structured validation;
- delegated tasks.

## Fase CM-5 — Security vNext

Entregas:
- context/capability/execution policies;
- secret classification;
- network policy;
- tool permissions;
- audit.

## Fase CM-6 — Multi-agent

Entregas:
- agent hierarchy;
- parallel tasks;
- isolated workspaces;
- result aggregation.

## Fase CM-7 — Model Routing

Entregas:
- provider registry;
- task routing;
- capability matrix;
- fallback.

## Fase CM-8 — Evaluation Platform

Entregas:
- datasets;
- regression suite;
- trace analysis;
- benchmark dashboards.

---

# 23. Definition of Done

O ContextMemory será considerado pronto para ser backend principal da IDE quando:

- uma sessão pode ser pausada e retomada;
- o Agent consegue recuperar contexto sem stuffing;
- memória permanece auditável;
- code memory responde navegação estrutural;
- artifacts evitam inflar o contexto;
- tools possuem policy;
- ações destrutivas passam por HITL;
- subagents podem trabalhar isoladamente;
- MCP pode ser usado sem quebrar o isolamento;
- múltiplos modelos podem ser utilizados;
- toda execução tem trace observável;
- tarefas podem ser avaliadas automaticamente.

---

# 24. Principais riscos

### Risco 1 — Contexto excessivo
Mitigação: budget, lazy discovery, artifacts e compaction.

### Risco 2 — Memória errada
Mitigação: provenance, temporal validity, confiança e revisão humana.

### Risco 3 — Agent loop infinito
Mitigação: budgets, timeouts, cycle detection e validation.

### Risco 4 — Tool abuse
Mitigação: policies independentes, sandbox e HITL.

### Risco 5 — Acoplamento ao editor
Mitigação: Agent API independente.

### Risco 6 — Dependência de um fornecedor de modelo
Mitigação: model abstraction e routing.

### Risco 7 — Crescimento sem avaliação
Mitigação: benchmark e regression suite desde a fase inicial.

---

# 25. Resultado estratégico

Ao final, o ContextMemory deixa de ser apenas “memória para LLM” e torna-se:

**uma plataforma open-source de execução e memória para agentes.**

A IDE será apenas um cliente especializado.

Isso permite que o mesmo runtime seja usado para:
- programação;
- operações;
- suporte;
- análise;
- automação;
- pesquisa;
- workflows corporativos.

---

# 26. Fontes principais

- ContextMemory repository: https://github.com/vitorcastro78/ContextMemory
- ContextMemory architecture and features: https://github.com/vitorcastro78/ContextMemory/blob/main/docs/architecture-and-features.md
- ContextMemory HITL: https://github.com/vitorcastro78/ContextMemory/blob/main/docs/hitl.md
- Cursor Agent overview: https://cursor.com/docs/agent/overview
- Cursor semantic search: https://prod.cursor.com/blog/semsearch
- Cursor secure codebase indexing: https://prod.cursor.com/blog/secure-codebase-indexing
- Cursor best practices: https://cursor.com/blog/agent-best-practices
- Cursor Cloud Agents: https://cursor.com/docs/cloud-agent
- Cursor SDK: https://cursor.com/blog/typescript-sdk

---

# 27. Nota de licenciamento

O repositório atual do ContextMemory está publicado sob AGPL-3.0. Qualquer decisão sobre distribuição do runtime, integração com a IDE, separação entre processos, eventual dual licensing ou distribuição comercial deve ser validada juridicamente antes de fixar a licença final da nova IDE.