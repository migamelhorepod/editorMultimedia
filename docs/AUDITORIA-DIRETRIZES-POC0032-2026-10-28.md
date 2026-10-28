# Auditoria de diretrizes — Editor Multimídia / POC-003.2
Data: 2026-10-28  
Branch: `feature/poc003-2-revogacao-recuperacao`

## Escopo e limites de evidência

As instruções literais da skill privada `analisar-e-desenvolver-software` **não estavam disponíveis** no catálogo de skills acessível nesta execução. Esta auditoria cobre apenas as diretrizes já estabelecidas com a usuária e verificadas no código: requisitos e protótipos revisados antes de implementar, explicações simples das decisões, versões estáveis recentes, segurança e performance acima de layout, Windows 11/HD como referência mínima, Git, BDD e automação pós-unitários. **Não equivale a certificação de aderência integral à skill** até acesso ao conteúdo instalado.

## Matriz de conformidade verificada

| Diretriz | Evidência | Estado |
|---|---|---|
| .NET/WPF estável e atual | `net10.0-windows` e `net10.0` nos csproj; `setup-dotnet@v5` no CI | Implementado; verificar política exata de versões da skill quando acessível |
| Worker de mídia isolado | `WindowsAppContainerLauncher`, token efetivo AppContainer, Job Object e perfil sem capacidade de rede | Testado em runner Windows; não confundir com homologação final no hardware mínimo |
| Sem fallback inseguro | `MediaWorkerAdmission.IsMediaProcessingAvailable == false` e recusa em `DemandMediaProcessing` | Preservado |
| Verificação das permissões e revogação | `AuthorizedMediaCatalog`, `AccessGrantRegistry`, `WindowsScopedCodecInput` | Implementado e com testes específicos; tratar revogação de handles ativos com finalização do Job |
| Desempenho em HD convencional | Cópia temporária integral com `WindowsScopedCodecInput` | **Não atende ainda ao objetivo zero-copy**; overhead de até 10 GB permanece |
| Recuperação de interrupções | `MediaResultRecovery`, arquivo de controle/lease, recuperação de cópias e testes de kill | Automatizado em cenários restritos; reinício real da aplicação e perda física do D: ainda exigem homologação |
| Testes BDD | xUnit com nomenclatura Given/When/Then e cenários de falha; `WorkerIpcCompletionPolicyTests` adicionados | Implementado, resultado CI mais recente pendente |
| Testes pós-unitários | Workflow `build-test.yml`: xUnit → Playwright/TS → relatórios | Implementado. **Playwright não comprova uso visual real do WPF** |
| Separação implementado/testado/homologado | Checklist, gates e este relatório | Aprovado como critério de relato; homologação final pendente |
| Registro de decisão arquitetural | `ADR-POC0032-ACESSO-DIRETO-HANDLE.md` | Registro explícito, solução ainda sem implementação validada |

## Desvios objetivos e correções desta auditoria

1. **Espera indefinida do IPC após saída exit-code 0.** O controlador usava `serverTask.GetAwaiter().GetResult()` depois da saída do worker, sem limite caso não houvesse conexão de pipe e o processo tivesse encerrado com código zero. Implementado `WorkerIpcCompletionPolicy.RequireAuthenticatedResponse` para impor um prazo curto, preservar erros de autenticação e falhar fechado. Adicionados testes BDD para saída zero sem IPC, erro 77, resposta autenticada, falha de autenticação e prazo inválido. **Aguardando comprovação do CI Windows do commit correspondente.**
2. **Mensagem de estado desatualizada.** `MediaWorkerAdmission.DemandMediaProcessing` dizia que o sandbox Windows ainda não havia sido implementado; corrigido para afirmar que falta homologar a integração produtiva da POC-003.2, sem alterar o bloqueio.
3. **Limitação de arquitetura conhecida.** O processamento atual cria uma cópia integral de cada mídia. Uma tentativa com protocolo `handle:` não passou no FFmpeg 9.0.2 estável obtido via Chocolatey. O código dessa tentativa foi revertido e a execução #658 aprovou a restauração; ainda falta escolher e comprovar um meio de entrada seekable zero-copy, sem acesso amplo ao sistema de arquivos.

## Cenários BDD exigidos para prosseguir

### HU-SEC-0032 — Não aceitar saída do worker sem relatório autenticado

- **Dado** que o worker AppContainer termina com código zero sem enviar o relatório pelo pipe autenticado,
- **Quando** o controlador avalia a tarefa,
- **Então** a tarefa deve falhar dentro do prazo fixado, sem aguardar indefinidamente e sem declarar sucesso.

### HU-PERF-0032 — Ler arquivos grandes sem cópia integral

- **Dado** que a biblioteca ativa contém uma mídia autorizada de vários gigabytes,
- **Quando** o worker consulta metadados ou decodifica a mídia,
- **Então** ele recebe apenas a capacidade de leitura autorizada, suporta busca aleatória e não cria cópia proporcional ao arquivo.

### HU-REC-0032 — Interrupção e recuperação sem corromper original

- **Dado** que um worker real foi interrompido durante processamento,
- **Quando** o editor reinicia e realiza a recuperação,
- **Então** elimina exclusivamente resíduos comprovadamente próprios, preserva as mídias originais e não deixa processos órfãos.

## Gate de encerramento

Manter POC-003.2 e PR #8 abertas até implementar a entrada sem cópia, comprovar o protocolo com FFprobe/FFmpeg estáveis em runner Windows, testar WAV e MP4 seekable, validar o Windows 11 com HD convencional e registrar as evidências de segurança. Não desbloquear a admissão genérica de produção antes disso.

**Pendência externa:** ler a skill `analisar-e-desenvolver-software` diretamente quando suas instruções estiverem expostas à conversa; refazer esta matriz contra cada cláusula literal da habilidade.
