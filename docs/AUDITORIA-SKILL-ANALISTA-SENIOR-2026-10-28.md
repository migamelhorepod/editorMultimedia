# MIG-5 — Auditoria literal da habilidade “Analista sênior e desenvolvedor de software”

- **Data:** 28/10/2026 (America/Sao_Paulo).
- **Fonte primária:** texto integral `$analisar-e-desenvolver-software` fornecido pela usuária em anexo Markdown nesta conversa; não republicado integralmente por ser habilidade pessoal.
- **Repositório:** `migamelhorepod/editorMultimedia`, branch `feature/poc003-2-revogacao-recuperacao`.
- **Objetivo da issue:** auditar cada cláusula, documentar evidências, divergências e ações. **Concluir a auditoria não significa homologar todo o aplicativo.**
- **Evidências observadas:** `README.md`, `.github/workflows/build-test.yml`, `src/Editor.Security/Editor.Security.csproj`, `tests/Editor.Timeline.Tests/WorkerIpcCompletionPolicyTests.cs`, `tests/playwright/package.json`, `docs/ADR-POC0032-ACESSO-DIRETO-HANDLE.md`, `docs/POC-003.2-checklist-homologacao.md` e CI #678 (build-test e gate de segurança verdes).

## Critérios de classificação

- **Atende conforme evidência disponível:** há prova localizada; não comprova ausência global de defeitos.
- **Parcial / há pendências:** parte atendida, faltam verificações/correções com issue relacionada.
- **Pendente / não comprovado:** nenhuma evidência suficiente para declarar conformidade.
- **Diretriz de atuação contínua:** modo de trabalho que depende das próximas entregas.
- **Divergência controlada:** duas referências anteriores requerem tratamento explícito, sem alterar requisitos aprovados.

## Conversar e ensinar

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| C-01 | Analista e implementador, crítico e colaborativo | Diretriz de atuação contínua | Processo de aprovação e issues Linear | Observar em cada nova entrega |
| C-02 | Perguntas relevantes, evitar questionário e repetição | Diretriz de atuação contínua | Aprovações anteriores registradas no projeto | Não reabrir decisões aprovadas |
| C-03 | Português claro, explicar termos na primeira ocorrência | Diretriz de atuação contínua | Documentação em português | Avaliar linguagem nas entregas futuras |
| C-04 | Informar objetivo, motivo e resultado de passos significativos | Diretriz de atuação contínua | Comentários em MIG-7 e MIG-19 | Relatar sem narrar comandos |
| C-05 | Explicar cada biblioteca nova e alternativa de não usar | Pendente / não comprovado | `tests/playwright/package.json` expõe dependências; justificativas não consolidadas | MIG-11 / MIG-91: registrar necessidade, licença, custo e alternativa antes de atualizar |
| C-06 | Explicar métodos alterados: nome, arquivo, entradas, saída e erros | Parcial / há pendências | `WorkerIpcCompletionPolicy` e testes Given/When/Then | Padrão obrigatório nos relatórios de novos commits |
| C-07 | Arquitetura por responsabilidades e fluxo de dados | Atende conforme evidência disponível | `docs/ADR-POC0032-ACESSO-DIRETO-HANDLE.md` | Manter diagrama somente se ajudar |
| C-08 | Criticar ambiguidades e preservar regras de negócio | Atende conforme evidência disponível | Checklists POC-003.2, bloqueio de produção | Não alterar regras sem apresentar impacto |
| C-09 | Encerrar com próxima etapa adequada | Diretriz de atuação contínua | Ritual de comunicação do projeto | Manter em cada resposta de trabalho |

## Respeitar o fluxo de revisão

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| F-01 | Levantar objetivo, usuários, plataforma e restrições | Atende conforme evidência disponível | Requisitos do editor e hardware Windows/HD documentados em decisões anteriores | Reavaliar restrições por incremento |
| F-02 | Preparar requisitos, regras, dúvidas e fora de escopo para revisão | Parcial / há pendências | Checklists `POC-003.2-*`; parte do backlog usa descrições resumidas | MIG-6: consolidar rastreabilidade e lacunas |
| F-03 | Requisitos aprovados antes de criar protótipo, salvo autorização | Diretriz de atuação contínua | Aprovações históricas informadas pela usuária | Reusar aprovações sem presumir novas |
| F-04 | Protótipo revisável e aprovado antes de implementação produtiva | Parcial / há pendências | POC-001 protótipo/editor e revisões; integração de produção segue bloqueada | Novos fluxos WPF exigem revisão da parte afetada |
| F-05 | Apresentar arquitetura, versões verificadas e entregas pequenas | Parcial / há pendências | ADR, CI e branch por POC; versões de algumas dependências ainda sem auditoria | MIG-11/MIG-7; consultar fontes oficiais |
| F-06 | Validar implementação, instruir execução e solicitar revisão | Parcial / há pendências | `README.md`, `tests/run-all.cmd`, GitHub Actions; validação Win11/HD física pendente | Não confundir CI com homologação física |
| F-07 | Não presumir aprovação e revisar só mudanças de impacto | Diretriz de atuação contínua | `MediaWorkerAdmission` permanece fail-closed | Não fazer merge PR #8 sem autorização |
| F-08 | Quando autorizado, entregar incremento verificável, não só documento | Parcial / há pendências | Testes MIG-19 e ADR MIG-7; processamento zero-copy ainda bloqueado | MIG-12/SEC-15: prova funcional antes do fechamento |

## Requisitos verificáveis

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| R-01 | Histórias exclusivamente BDD Dado/Quando/Então | Atende conforme evidência disponível | Descrições das issues Linear e testes `WorkerIpcCompletionPolicyTests.cs` | Preservar este padrão |
| R-02 | Cobrir principal, alternativas, permissões e falhas | Parcial / há pendências | Testes IPC e AppContainer, checklists de interrupção | MIG-6/MIG-19 ampliar matriz de cenários adversariais |
| R-03 | Aceite observável, prioridade, dependências e rastreabilidade | Parcial / há pendências | 98 issues Linear, bloqueios nativos, CI | MIG-6/QA-09: vínculo cenário→teste→commit→evidência |
| R-04 | NFR mensuráveis; não inventar métricas | Parcial / há pendências | Meta 1.000 mídias, 5s/3s e 10 GB definida em requisitos aprovados | Executar medições no HD físico, sem alegar cumprimento |
| R-05 | Documentação proporcional e mini-PRD quando útil | Atende conforme evidência disponível | ADR e checklists enxutos por POC | Evitar documentação sem efeito |

## Tecnologias atuais e consolidadas

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| T-01 | Stack escolhida pelo problema, não única | Atende conforme evidência disponível | C#/.NET WPF por desktop Windows; Playwright TS para integração | Justificar cada adição futura |
| T-02 | Consultar versões e suporte em canais oficiais, com data e links | Parcial / há pendências | Consulta 28/10/2026: .NET 10 LTS e Node 24 LTS confirmados | MIG-11: ampliar a FFmpeg, Playwright, TypeScript, xUnit e distribuição |
| T-03 | Excluir beta/preview/RC e sem suporte | Parcial / há pendências | `net10.0` e Node 24 LTS são linhas suportadas | Confirmar restante da árvore de dependências antes de homologar |
| T-04 | Atual e consolidado, sem upgrade automático | Atende conforme evidência disponível | Uso do .NET 10 LTS em vez de .NET 11 RC | Sem atualização major automática |
| T-05 | Versões fixas e upgrades com migração e testes | Parcial / há pendências | `tests/playwright/package-lock.json` via npm ci; `.csproj` referencia versões explícitas; FFmpeg instalado via Chocolatey sem pin | MIG-11: fixação/verificação do binário e hashes FFmpeg |
| T-06 | Arquitetura simples, modular, testável | Parcial / há pendências | Projetos Editor.Security e Editor.Security.Worker isolados; WPF e testes separados | Reavaliar overhead e fronteiras do worker na POC-003.2 |

## Segurança e desempenho

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| S-01 | Segurança e performance à frente de layout | Atende conforme evidência disponível | AppContainer, Job Object, worker fail-closed; ADR de zero-copy | Não ativar produção até homologar |
| S-02 | Ameaças, validação de fronteiras e menor privilégio | Parcial / há pendências | Token AppContainer, grants, controle por tarefa, Job e IPC com PID | MIG-12/SEC-15: comprovar kernel/handle/seek seguro e input malicioso |
| S-03 | Auth, segredos e criptografia reconhecida quando pertinentes | Parcial / há pendências | IPC verifica processo; sem acesso amplo; não verificado todo o tratamento de segredos | MIG-91/QA-11: revisão aprofundada de logs, hashing e segredos |
| S-04 | Dependências e vulnerabilidades auditadas, sem segurança absoluta | Pendente / não comprovado | CI compila/testa mas não há prova neste exame de SBOM/CVE/licenças | MIG-11 e MIG-91 |
| S-05 | Falhas, recuperação, backup e operações não destrutivas | Parcial / há pendências | `MediaResultRecovery`, `LibraryFileStore.RecoverInterruptedImports`, `POC-003.2-checklist-homologacao.md` | MIG-21/SEC-15: crash real, perda do D:, journal e cold start |
| S-06 | Medir antes de otimizar; I/O limitado e UI responsiva | Pendente / não comprovado | ADR explica custo de cópia integral; benchmark físico não disponível | MIG-12, MIG-25, QA-04; nenhuma promessa de 5s/3s |

## Computador mínimo

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| H-01 | Referência fornecida: Win11 x64, i5-8265U, 24 GB, MX130 2 GB/UHD 620, HD 1 TB | Divergência controlada | Skill atual: RAM 24 GB; requisitos aprovados anteriores: referência 16 GB | Manter compatibilidade mais exigente com 16 GB até decisão explícita; registrar 24 GB como configuração atual informada |
| H-02 | Especificações são declarações, não benchmark | Atende conforme evidência disponível | Sem alegação de medição na máquina da usuária | Não registrar identificadores |
| H-03 | Não presumir SSD, GPU potente ou espaço livre | Atende conforme evidência disponível | ADR prioriza HD convencional e cópia evitada | Validar uso sem GPU dedicada |
| H-04 | Minimizar I/O pequeno, varreduras e cópia desnecessária | Pendente / não comprovado | Implementação vigente mantém staging integral por arquivo | MIG-12/SEC-15: eliminar cópia sem romper isolamento |
| H-05 | Dev viável na máquina; mobile e web exigem requisitos próprios | Parcial / há pendências | CI Windows e IDE Visual Studio desejada; sem emuladores | Verificar custo local quando habilitar IA |
| H-06 | Não extrapolar performance de CI para máquina mínima | Atende conforme evidência disponível | Documentos diferenciam runner Windows de homologação Win11/HD | Exigir 5 medições frio/quente no hardware alvo |

## Implementar e entregar com Git

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| G-01 | Repositório único, inspecionar contexto antes de editar | Atende conforme evidência disponível | `migamelhorepod/editorMultimedia` e branch `feature/poc003-2-revogacao-recuperacao` | Preservar trabalho da main e PR #8 |
| G-02 | Não usar ZIP como entrega principal | Atende conforme evidência disponível | Histórico GitHub/commits e CI | Manter |
| G-03 | Priorizar Visual Studio no desktop; verificar pré-requisitos | Parcial / há pendências | README abre com VS Code, orientação histórica; intenção atual é Visual Studio | Atualizar orientação inicial sem apagar histórico POC-001 |
| G-04 | Incrementos pequenos, arquivos/métodos e instruções reproduzíveis | Parcial / há pendências | Commits individuais e `tests/run-all.cmd` | Explicar métodos em cada relatório |
| G-05 | Comandos CMD isolados e com pasta/pré-requisitos | Atende conforme evidência disponível | Seção CMD `README.md` e habilidade | Seguir em respostas futuras |
| G-06 | CI build/test; distinguir execução, push e publicação | Atende conforme evidência disponível | `.github/workflows/build-test.yml` e gate Windows | Informar resultado real por run |

## Verificar qualidade e evidências

| Cláusula | Exigência da habilidade (resumo) | Resultado | Evidência | Ação / destino |
|---|---|---|---|---|
| Q-01 | Build, testes e análise estática proporcionais aos riscos | Parcial / há pendências | GitHub Actions #678 build-test e gate sucesso; análise estática/SBOM não verificada | MIG-91/QA-03 ampliar gates |
| Q-02 | Cobrir permissões, falhas, regras; não testar só código feliz | Parcial / há pendências | Testes AppContainer/IPC, Playwright TS e WFP | MIG-19 concluir pipe malformado, foreign PID no runner |
| Q-03 | Verificar estados da UI e acessibilidade | Pendente / não comprovado | Playwright atual verifica integração, não janela WPF real | QA-01/REL-05: automação de interface Windows |
| Q-04 | Medições com volume real; não inferir de hardware diferente | Pendente / não comprovado | Sem benchmark de 1.000 mídias/10 GB na máquina mínima | QA-04/QA-05/SEC-14 |
| Q-05 | Relatar provas, limites, resultado e próxima etapa | Atende conforme evidência disponível | CI #678 verde; POC-003.2 declarada não homologada | Continuar relatório sem falsos sucessos |
| Q-06 | Ao concluir: comportamento, métodos, execução, pendências | Diretriz de atuação contínua | Diretriz de entrega vigente desde o recebimento da skill | Cobrir cada incremento |

## Verificações externas oficiais (consultadas em 28/10/2026)

- .NET 10 é LTS ativo (suporte até novembro de 2028): https://dotnet.microsoft.com/en-us/platform/support/policy
- Node.js 24 é LTS: https://nodejs.org/en/about/previous-releases e https://nodejs.org/en/blog/release
- **Escopo limitado:** esta consulta não valida versões atuais, suporte, licenças, CVEs ou integridade de *todas* as dependências. Essa análise pertence à MIG-11 e MIG-91. Não promover major upgrades sem compatibilidade e testes.

## Lacunas críticas e responsáveis existentes

1. **Processamento zero-copy não comprovado:** staging integral no HD e sem seek seguro demonstrado; MIG-7 documenta a escolha condicional, MIG-12 valida adaptador, SEC-15 bloqueia homologação.
2. **Revisão de supply chain incompleta:** binários FFmpeg providos por Chocolatey no workflow sem pin e sem hash documentado; MIG-11 e MIG-91.
3. **Testes de interface WPF real ausentes:** Playwright TypeScript é teste de integração do motor, não teste visual de janela; QA-01 / REL-05.
4. **Performance no Windows/HD físico não medida:** metas aprovadas não são resultados; QA-04, QA-05, SEC-14.
5. **Rastreabilidade incompleta:** cenários/tarefas/testes/commits/evidências ainda não formam uma matriz global; MIG-6 / QA-09.
6. **Divergência de memória controlada:** a habilidade atual informa 24 GB de RAM no equipamento da usuária; o projeto aprovado anteriormente utilizava 16 GB como referência de desempenho. Não subir requisito mínimo por inferência. GPU MX130/UHD 620 não é garantia de aceleração disponível.
7. **Documentação de entrada desatualizada:** README descreve POC-001 e VS Code como principal; cabeçalho de estado atual e Visual Studio preferencial devem orientar sem reescrever histórico.

## Critérios de aceite BDD da MIG-5

**Cenário 1 — Leitura literal**
- **Dado** o texto integral da habilidade recebido na conversa,
- **Quando** todas as seções forem comparadas à implementação/documentação,
- **Então** cada cláusula terá resultado, evidência e encaminhamento rastreável nesta matriz.

**Cenário 2 — Não mascarar lacunas**
- **Dado** um requisito sem prova automatizada ou teste físico,
- **Quando** a auditoria for concluída,
- **Então** o item permanece parcial/pendente e é vinculado a uma issue existente, sem declarar homologação.

**Cenário 3 — Mudança de configuração de referência**
- **Dado** a habilidade atual informar 24 GB e a meta anterior do editor usar 16 GB,
- **Quando** a matriz de requisitos for revisada,
- **Então** não haverá alteração silenciosa dos requisitos mínimos de RAM, preservando a meta mais exigente e registrando a diferença.

## Resultado da MIG-5

**Auditoria documental executada; divergências encaminhadas às issues citadas.** O estado global do produto permanece **não homologado**. O teste automatizado com maior evidência recente é a execução CI #678; isso não substitui teste local real. Próximas entregas devem seguir a política, explicar bibliotecas e métodos alterados e registrar evidência antes de serem marcadas Done.
