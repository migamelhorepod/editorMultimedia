# MIG-6 — Matriz de rastreabilidade BDD do Cycle 1

Data: 28/10/2026. Escopo: requisitos aprovados que impactam a POC-003.2, com referências a evidências reais; não altera decisões de negócio.

| ID | Regra aprovada | Dado | Quando | Então | Evidência / implementação | Estado |
|---|---|---|---|---|---|---|
| SEG-01 | Worker obrigatório AppContainer | mídia solicitada | processamento iniciado | somente worker com token AppContainer processa, sem fallback | `src/Editor.Security/WindowsAppContainerLauncher.cs`, `MediaWorkerAdmission.cs` | Parcial: admissão produtiva bloqueada |
| SEG-02 | Arquivo autorizado individualmente | biblioteca ativa | worker solicita mídia | acesso só ao arquivo/operação autorizados | `docs/ADR-POC0032-ACESSO-DIRETO-HANDLE.md` | Pendente: zero-copy seekable MIG-12 |
| SEG-03 | IPC autenticada | resposta ausente, inválida ou PID estranho | controlador verifica resultado | erro fechado, nunca sucesso falso | `WorkerIpcCompletionPolicy.cs`, `WorkerIpcCompletionPolicyTests.cs` | Parcial: testes de pipe malformado MIG-19 |
| SEG-04 | Sem rede do worker | worker tenta conectar | política AppContainer/WFP aplicada | tráfego negado e registrado | `.github/workflows/build-test.yml`, `tests/security/collect-sec006-wfp.ps1` | Evidência CI, homologação física pendente |
| SEG-05 | Revogação e falha de processo | worker é interrompido | Job termina | sem órfão, grants revogados e recuperação segura | `docs/POC-003.2-checklist-homologacao.md` | Parcial: crash físico pendente |
| PERF-01 | Não copiar mídias de até 10 GB integralmente | mídia regular autorizada | FFprobe lê e busca | sem cópia proporcional, seek correto | `docs/ADR-POC0032-ACESSO-DIRETO-HANDLE.md` | Pendente MIG-12 |
| PERF-02 | Abrir biblioteca até 5 s, buscar até 3 s | 1.000 mídias no HD convencional | abrir e filtrar | cumprir limite em cinco medições frias/quentes | especificação aprovada; QA-04 | Pendente de benchmark real |
| BIB-01 | Disco D: indisponível | volume removido | projeto é aberto | mídias indisponíveis, sem perda do projeto | regras aprovadas, checklist POC-003.2 | Pendente teste físico |
| BIB-02 | Substituição não cancelável após iniciar | substituição confirmada | processamento começa | janela bloqueante e recuperação transacional | checklist POC-003.2 | Parcial, falta integração WPF |
| TIM-01 | Até dez trilhas e cem clipes | projeto aberto | usuário excede limites | informar sem criar trilha extra | `src/Editor.Timeline` e testes xUnit | Revalidar integração WPF |
| UX-01 | Snap inicia desativado | editor inicia | timeline aparece | encaixe desmarcado | requisitos aprovados; QA-01 | Verificação de UI pendente |
| GOV-01 | Regras BDD, stack LTS, segurança primeiro | mudança proposta | revisão é feita | registrar evidência e lacuna | `docs/AUDITORIA-SKILL-ANALISTA-SENIOR-2026-10-28.md` | Auditoria concluída MIG-5 |

## Controle de mudança

1. Regra já aprovada só muda mediante aprovação explícita; registrar ID, impacto e testes afetados.
2. Requisitos sem teste automatizado não podem ser marcados como homologados. Vincular a issue existente antes de criar duplicatas.
3. Cada novo cenário BDD deve apontar para teste, arquivo/método, commit e execução de CI; em falha, registrar causa e correção.
4. Prioridade: segurança, performance e depois UI. Hardware mínimo de referência preservado em 16 GB; a skill informa máquina atual de 24 GB.
5. Não declarar o editor concluído nem liberar `MediaWorkerAdmission` até a POC-003.2 passar nos gates reais.

## Próximos responsáveis

- MIG-19: IPC e casos adversariais.
- MIG-12: leitura seekable real sem cópia integral.
- MIG-11 / MIG-91: dependências, CVEs, SBOM e licenças.
- QA-01/QA-04: UI WPF e desempenho no HD físico.

**Aceite BDD:** Dado um requisito aprovado, quando o desenvolvedor consulta esta matriz, então identifica ID, cenário, prova ou pendência e issue responsável, sem inventar aprovação.
