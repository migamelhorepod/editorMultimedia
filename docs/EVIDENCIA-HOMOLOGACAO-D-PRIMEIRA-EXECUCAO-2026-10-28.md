# Homologação física D: — primeira evidência recebida em 28/10/2026

## Fonte e escopo

Usuária executou `tests\homologar-d.cmd` em Windows local, D: fixo, com WAV sintético de 64 MiB e forneceu os arquivos `physical.trx`, `physical-hdd-evidence.json` e `physical-test-output.log`. Este resumo registra somente observações constantes dos anexos. Arquivos privados, caminhos de usuário e SID do AppContainer não são publicados neste documento.

## Evidência observada

- TRX: **10 casos** `UnitTestResult`, todos `Passed` e nenhum ignorado.
- Log: `Aprovado! – Com falha: 0, Aprovado: 10, Ignorado: 0, Total: 10` em aproximadamente **24 segundos** de execução xUnit.
- Casos incluem leitura seekable WAV/MP4 com `moov` posterior, decodificação via FFmpeg, proteção contra substituição de arquivo físico, revogação, metadata, arquivo malformado, worker empacotado, 64 MiB e encerramento forçado.
- JSON: `executedLocally=true`, `drive=D:\`, `driveTypeReported=Fixed`, `mediaMiB=64`, WAV sintético com **67.108.908 bytes** (64 MiB + cabeçalho WAV), FFmpeg e FFprobe **9.0.2-full_build-www.gyan.dev** provenientes de instalação WinGet. Versão e hashes reais foram gravados nos anexos privados.
- Arquivo WAV físico criado no D:, com hash SHA-256 inicial registrado, **mas no código daquela revisão a fixture principal não era consumida diretamente pelo serviço**: os testes usavam suas próprias fixtures com TEMP/TMP no D:. Portanto não afirmar que o hash inicial garante integridade após o processamento da fixture principal.
- JSON: `testExitCode=-1`, `testsPassed=false`, `fixtureSha256Unchanged=false`, `stagingFilesRemaining=-1` **são campos de inicialização não atualizados**, não resultados observados de falha. O runner abortou na chamada `Process.PeakWorkingSet64` quando o processo `dotnet test` já havia saído (`InvalidOperationException`).
- `hddTechnologyConfirmed=false`: o Windows identifica volume fixo, não comprova meio magnético. O teste local de 64 MiB não cobre 10 GiB nem benchmark transacional do aplicativo.

## Diagnóstico técnico e correção

1. **Falha no coletor de métricas:** `tools/Editor.PhysicalHomologation/Program.cs` lia `child.PeakWorkingSet64` depois de `child.WaitForExit()`; o Windows não disponibiliza o valor após encerrar o processo e lança `InvalidOperationException`.
2. **Correção em `cf571ed`:** coletar pico de memória com polling enquanto o processo vive; falha de métrica opcional passa a ser `null` e não cancela a interpretação do TRX. Memória reportada é **somente do processo de testes**, não inclui worker/FFmpeg.
3. **Lacuna de validade do teste em `32bddfb`:** usar `IsolatedMediaProcessingService.InspectDurationTicks` e `.DecodeToNull` no WAV físico de 64 MiB, comparar duração esperada e hash SHA-256, observar staging remanescente e preservar grants revogados.
4. **Correção adicional em `a7fdfab`:** preservar a medição de integridade e staging mesmo quando o teste direto do arquivo físico falhar, registrando erro separadamente.
5. **Reexecução no hardware físico:** necessária após validar a compilação e o pipeline. A execução anterior não basta para homologar a fixture física nem afirmar performance.

## Critérios BDD reavaliados

- **Dado** os dez testes xUnit no D:, **quando** são lidos seus resultados TRX, **então** há evidência de **10/10 aprovados**; isso não vira `testsPassed=true` por inferência, porque o coletor abortou.
- **Dado** um coletor de memória opcional com processo já encerrado, **quando** o teste conclui, **então** a memória é `null` se indisponível e o TRX, hash, staging e status final ainda são gravados.
- **Dado** o WAV sintético real criado no D:, **quando** uma execução corrigida invoca o AppContainer, **então** duração e decodificação precisam passar pelo HANDLE do mesmo arquivo, o hash ser preservado e o staging continuar vazio.

## Gates ainda não concluídos

- Reexecutar `tests\homologar-d.cmd` no Windows 11 após `git pull --ff-only`; anexar **novo** JSON e log/TRX, sem reescrever evidência histórica.
- Confirmar mecanicamente que o D: é HDD (não apenas volume `Fixed`).
- Executar benchmark até 10 GiB e recuperação real de tarefas/projetos e verificar CPU/RAM/I/O com instrumentação adequada.
- Vulnerabilidades nativas FFmpeg 9.0.2 e obrigações GPL/redistribuição continuam abertas. `MediaWorkerAdmission.IsMediaProcessingAvailable` deve permanecer `false` e nenhuma PR deve ser integrada para produção com gate aberto.

**Estado:** bateria local xUnit **PASSOU 10/10**; geração de evidência final **FALHOU** por bug do coletor, já corrigido em código e aguardando reexecução física. Não homologado.