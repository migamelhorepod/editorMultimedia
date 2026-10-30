# POC-003.2 — Evidências de processamento isolado e gates de homologação

Data 28/10/2026. Branch `feature/poc003-2-revogacao-recuperacao`; PR #8 permanece **não homologada e não autorizada para merge**.

## Comportamento real implementado

1. `WindowsAppContainerLauncher.ProbeAuthorizedSeekableMetadataTicks` verifica grant por operação, abre exclusivamente o arquivo de leitura em biblioteca ativa, verifica comprimento e identidade física, entrega um identificador autorizado ao AppContainer por STARTUPINFOEX + lista restrita de HANDLES; não repassa caminho original ao worker.
2. `WindowsInheritedHandleFfprobe.ReadDurationAsync` inicia FFprobe estável dentro do processo isolado com STDIN herdado do recurso real, sem redirecionar para pipe não seekable e sem cópia integral, retorna duração para o worker com limite de tempo. Apenas este experimento usa o recurso sem cópia; métodos legados de diagnóstico ainda preservam staging, mas o serviço de leitura de duração e decodificação não depende mais dessa cópia.
3. `WindowsFileIdentity.FromPath` e `FromHandle` consultam identidade física (volume e file index) por `GetFileInformationByHandle`, impedindo substituição no mesmo caminho entre cadastro e execução; `AuthorizedMediaCatalog.ValidateOpenedHandle` compara a identidade do HANDLE real com a identidade registrada e exige grant ativo.
4. `WindowsWorkerMetadataPipe` aceita exclusivamente resposta do PID real e RequestId previsto. O Job Object encerra o worker e seus filhos no desligamento; subprocesso nunca recebe o caminho original.
5. `MediaWorkerAdmission.IsMediaProcessingAvailable` permanece **false**. É incorreto afirmar processamento geral de produção ativado.

## Evidência no CI Windows

- Execução #704 (40742575123): implementação inicial falhou em WAV/MP4, exit code 77 (não houve sucesso falso).
- Execução #716 (40743403036): corrigido o encaminhamento do HANDLE real para o FFprobe filho com Win32 `STARTF_USESTDHANDLES`; build, xUnit, Playwright e gate de segurança passaram.
- Execução #726 (40743953581): adicionada ligação da mídia à identidade física; resultado deve ser verificado antes de aceitar essa revisão como verde.
- Execução #712 (40743054386): auditoria .NET/npm com zero advisories encontrados, inventário CycloneDX e hash FFmpeg. Build 9.0.2 inclui `--enable-gpl --enable-version3`; distribuição exige revisão de licença.

## Critérios de aceite BDD, sem pressupor homologação

| Cenário | Dado | Quando | Então | Evidência | Situação |
|---|---|---|---|---|---|
| FD-WAV | WAV 1,2 s autorizado | FFprobe lê `fd:` no sandbox | Metadados corretos sem staging | `AppContainerSeekableFdTests`, CI #716 | Passou no runner Windows |
| FD-MP4 | MP4 com `moov` depois de `mdat` | FFprobe lê e busca | Metadados corretos sem staging | `AppContainerSeekableFdTests`, CI #716 | Passou no runner Windows |
| FD-ACL | mídia original sem permissão adicional | worker examina mídia | hash e ACL originais preservados | `AppContainerSeekableFdTests`, CI #716 | Passou no runner Windows |
| FD-REV | grant revogado | tentar abrir recurso | falha antes de iniciar worker | `AppContainerSeekableFdTests`, CI #716 | Passou no runner Windows |
| FD-SWAP | caminho de mídia substituído | tentar abrir recurso | nega identidade física divergente | `AppContainerSeekableFdTests`, CI #726 | Aguardando veredito da execução |
| FD-REDE | worker tenta rede | AppContainer/WFP aplica política | tentativa negada pelo Windows | gate POC-003.1, CI #716 | Gate anterior passou |
| FD-KILL | worker real interrompido | Job encerrado | sem processos órfãos e sem handles sobreviventes | testes de decode anterior e Job Object | Falta prova específica com FD sem cópia e reinício do app |
| FD-LARGE | mídia até 10 GB no HD D: | realizar seek e decode | I/O sem cópia, memória/CPU dentro das metas | testes no computador da usuária | Não executado neste ambiente |
| FD-PROD | fluxo da UI WPF solicita mídia | worker faz processamento real isolado | resultado persistido de modo transacional e recuperável | `MediaWorkerAdmission`, integração UI | **Bloqueado intencionalmente** |

## Não concluir POC-003.2 ainda

- Validar último commit no CI e completar testes adversariais específicos do HANDLE herdado (inserção PID estrangeiro, revogação após abertura, crash com subprocessos).
- Integrar entrada seekable aos fluxos produtivos de metadados, decodificação e exportação conforme autorização e projeto, com garantias de transação, backup e recuperação após kill/cold start.
- Medir no Windows 11 x64 / i5-8265U / HD convencional D: com mídia de vários GB e até 10 GB, sem extrapolar resultados do runner efêmero.
- Auditar CVEs e obrigações da distribuição nativa GPLv3 de FFmpeg antes de empacotar aplicativo.

**Decisão:** pesquisa técnica do FD seekable demonstrada em runner Windows no CI #716; homologação de produção, release e fechamento da POC-003.2 permanecem pendentes até todos os gates serem comprovados.
## Incremento adicional: integração da consulta de duração

- `IsolatedMediaProcessingService.InspectDurationTicks(registeredMediaPath)` passou a usar `WindowsAppContainerLauncher.ProbeAuthorizedSeekableMetadataTicks`, isto é, consulta real FFprobe com identificador de leitura de arquivo autorizado, **sem staging integral**. Entrada: caminho cadastrado na biblioteca ativa; retorno: duração em ticks de 100 ns; falhas: grant/identidade inválidos, FFprobe sem metadados, IPC sem resposta autenticada ou limite de tempo.
- `WindowsAppContainerLauncher.ProbeAuthorizedSeekableDecode(workerExecutable, catalog, request)` adicionou prova isolada de decodificação real por FFmpeg com grant `ReadChunk`; recebe worker/catálogo/requisição, retorna sem dados exportados após confirmação de IPC ou falha fechada se codec/token/identidade inválidos. `IsolatedMediaProcessingService.DecodeToNull` passou a invocar `ProbeAuthorizedSeekableDecode` depois do gate #742; a saída nula do codec é processada no AppContainer sem staging integral. A regressão do contrato de erro em mídia malformada foi identificada no CI #752 e corrigida no commit `bd3fa71`, aguardando CI.
- `WindowsInheritedHandleFfprobe.DecodeToNullAsync` chama o FFmpeg estável com stdin herdado seekable e saída nula, com limite total compatível com a política de até cinco minutos do launcher para disco convencional. Nenhum pacote de terceiros novo foi acrescentado; a implementação usa APIs Win32 e `AnonymousPipeServerStream` do .NET já presente.
- O gate de pesquisa `tests/security/assert-poc0032-fd-research-gate.ps1` exige provas BDD de WAV/MP4, revogação, identidade contra substituição, decodificação real e auditoria de vulnerabilidades; reporta `readyForProduction=false` e `readyForDistribution=false` independentemente de seu próprio sucesso.

**Importante:** esse incremento não equivale à integração da UI WPF ou à homologação de arquivos de 10 GB no HD físico. A produção geral continua bloqueada pela política fail-closed.

## Bloqueio adicional de segurança nativa

Em 28/10/2026 foram identificadas CVE-2026-107678 (MP4 pssh, CPU/pilha) e CVE-2026-107696 (RTSP redirect), relatadas como afetando FFmpeg até 9.0.2. O AppContainer mitiga impacto, não corrige. Registrar em `docs/MIG-91-RESULTADO-AUDITORIA-2026-10-28.md`; não homologar nem distribuir o decoder 9.0.2 até revisão de build/patch e licença GPLv3.

## Execução física local Windows 11 / D: (instrumentação entregue)

Script: `tests/security/homologate-physical-d.ps1` (commit `ebb5870`). **Não foi executado no computador da usuária nesta conversa; evidência física ainda ausente.**

### Procedimento seguro no checkout do Visual Studio

1. Na máquina com Windows 11 e HD convencional D:, conferir espaço livre, compilar o projeto em Release e preparar o worker AppContainer e codecs conforme `scripts/preparar-worker-appcontainer.ps1` (sem copiar mídias pessoais). O script de homologação recusa runners GitHub Actions, exige unidade D: fixa e cria uma pasta nova com GUID. Não altera pastas de biblioteca existentes.
2. Abrir PowerShell 7 no diretório do repositório e executar: `pwsh -NoProfile -File tests/security/homologate-physical-d.ps1 -MediaMiB 64` (parâmetro opcional `-IncludeCrashRecovery`). O script gera WAV físico de 64 MiB, registra SHA-256 antes/depois, usa TEMP/TMP no D: para testes do AppContainer e captura TRX e duração; guarda relatório JSON em `TestResults/physical-windows11-hdd/physical-hdd-evidence.json`.
3. Se falhar, **não apagar a pasta de evidências** nem desabilitar o gate; preservar logs e esclarecer se a versão/binário do codec é compatível. Remoção do diretório de fixture só após revisão humana do conteúdo.
4. O script não é benchmark de 10 GiB: WAV RIFF clássico limita o tamanho abaixo de 4 GiB, e a verificação de 10 GiB requer fixture em outro contêiner e medições controladas de I/O, CPU, RAM, seek, revogação e tempo na máquina física. Não inferir sucesso a partir de 64 MiB.

### Situação do FFmpeg em 28/10/2026

- Página oficial `https://ffmpeg.org/download.html` lista **9.0.2** como última versão estável da série 9.0; os rastreadores Debian `https://security-tracker.debian.org/tracker/CVE-2026-107678` e `https://security-tracker.debian.org/tracker/CVE-2026-107696` ainda indicam correção **unfixed** no pacote acompanhado. Pull requests upstream são indicadas, sem prova de release estável com ambos os patches.
- Não instalar automaticamente código de branch de desenvolvimento, release candidate ou executável não auditado para 'resolver' CVE. Continuar bloqueio fail-closed, rever correção upstream assinada e hash/termos de redistribuição quando surgir build estável verificável. A prova do worker é válida apenas para a distribuição exata testada e não equivale à mitigação de todas as entradas hostis.

**Resultado de homologação física:** não executada; aguarda evidência gerada no Windows 11 local com volume D: e testes até 10 GiB. `MediaWorkerAdmission.IsMediaProcessingAvailable` permanece false.

## Fechamento da regressão CI #767

- A execução #767 falhou em `AppContainerStageRecoveryTests.Given64MiBRegisteredWav_WhenRealFfprobeInspects_ThenOriginalRemainsAndStagingIsClean`: o Win32 ainda liberava o HANDLE da fixture durante a exclusão do diretório.
- Corrigido somente o cleanup em `AppContainerStageRecoveryTests.cs` usando `WindowsMediaFixtureCleanup.Remove` (retries limitados com erro permanente não mascarado), commit `c33663d`.
- **Pipeline #771 (40746702396): todos os jobs SUCCESS** — `build-test`, `Security release gate POC-003.1` e `Seekable FD research gate POC-003.2`. O requisito **específico** do adaptador seekable MIG-12 passou no Windows runner e foi marcado Done no Linear.
- **Limites preservados:** produção geral continua bloqueada (`MediaWorkerAdmission=false`); FFmpeg 9.0.2 apresenta CVEs nativas conhecidas; distribuição GPL e homologação física Win11/HD D: com até 10 GiB ainda não foram realizadas. Não marcar POC-003.2 ou MIG-91 como concluídas.
