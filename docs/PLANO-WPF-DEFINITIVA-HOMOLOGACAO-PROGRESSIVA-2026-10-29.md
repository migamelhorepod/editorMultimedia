# Decisão aprovada: avançar para a interface WPF definitiva com homologação progressiva

Data: 29/10/2026. **Decisão de trabalho aprovada pela usuária**: realizar desenvolvimento funcional pela interface WPF definitiva com uma biblioteca de testes no volume D:, aceitando adiar homologações extremas para um gate de distribuição; manter segurança indispensável sem habilitar processamento irrestrito. Não fazer merge/abrir MediaWorkerAdmission automaticamente.

## Escopo e prioridade

1. **Interface definitiva WPF** como ambiente principal de validação humana, substituindo o ciclo indefinido de POCs sem experiência completa. Usuária revê fluxos/protótipo antes da implementação; cada incremento deve ter build, xUnit, E2E Playwright TypeScript quando aplicável, evidências anexáveis, instruções de homologação e relatório de correção.
2. **FFmpeg: resolver de forma prioritária CVEs, origem, build auditado e licença**. O binário atualmente testado `9.0.2-full_build` possui flags GPL/version3; não declarar LGPL e não distribuir até comprovar correções e conformidade. Política de origem/hashes fixados, exclusão de recursos não essenciais e testes de regressão de codecs.
3. **Biblioteca de testes no D:** `D:\EditorMultimidia\Testes`, como alternativa aprovada para verificar no próprio aplicativo sem risco a mídias pessoais. Não usar a pasta de biblioteca real nem apagar arquivos não criados pela POC.
4. **10 GB:** gerador em `tools/Editor.LargeMediaFixture`; produz RF64 PCM sintético denso sem alocar todo o arquivo em memória e sem renomear arquivo parcial para final até terminar. Smoke de 8 MiB no CI; execução física 10.000.000.000 bytes somente pelo usuário, em CMD. Essa geração comprova bytes escritos/seek e hash do fluxo, **não** comprovou decode/seek/10 GB no worker até o usuário importar a mídia e anexar logs. RF64 pode não representar as limitações de MP4; complemento com MP4/mov grande continua pendente.
5. **Falhas raras:** desconexão de D:, crash/restart do processo, workers órfãos e transações pendentes acompanham testes incrementais e gate pré-distribuição; não bloquear desenho/implementação da interface WPF antes dessas provas finais. Não desativar mecanismos de recuperação existentes para avançar.
6. **MediaWorkerAdmission:** permanece `false`; diagnóstico controlado `IsolatedMediaProcessingService` com AppContainer já funciona em fluxos WPF de importação/relink/atualização. Testes na UI **não** provam que o gate de produção está aberto. O bloqueio de admissão não impede desenvolver UI e fluxos experimentais; até abertura auditada, não declarar produção homologada. Revisar e eliminar rotas divergentes, sem fallback não isolado.
7. **Cloud POC** suspensa por decisão da usuária. Não criar contas, fazer deploy remoto nem migrar mídias.

## Testes manuais WPF com evidências

1. Abrir Visual Studio e atualizar branch `feature/poc003-2-revogacao-recuperacao` usando Git integrado da IDE. Selecionar projeto inicial `Editor.Timeline.Wpf`, configuração **Debug** para investigar erros e iniciar **F5**; para provas de desempenho repetir em Release fora do depurador.
2. Em **Arquivo > Ativar outra biblioteca...**, selecionar `D:\EditorMultimidia\Testes\Biblioteca` (criar pasta pelo Explorador antes, não apontar para originais pessoais).
3. Em **Arquivo > Novo projeto vazio**, importar WAV pequeno real ou arquivo gerado de 64 MiB. Esperado: arquivo copiado para biblioteca, metadados aferidos com worker/AppContainer, item adicionado à biblioteca e timeline, duração correta e status claro. **Importação bem sucedida não significa, sozinha, prova de proteção da VM/worker.**
4. Selecionar mídia no catálogo > **Atualizar dados**. Conferir duração, ausência de travamento, nenhuma cópia integral temporária na biblioteca. Repetir com MP4 de teste, se disponível, preferencialmente sem metadados `faststart`.
5. Abrir **Debug > Windows > Output** (Visual Studio) e escolher **Debug** no seletor. Após importação ou erro, copiar texto pertinente. Em **Debug > Windows > Exception Settings** habilitar pausa em exceções CLR apenas para investigar falha, não deixar ativo no uso comum. Anexar stack trace sem dados confidenciais.
6. Visual Studio **Debug > Windows > Diagnostic Tools** (se disponível), medir CPU/memória em importação ou refresh; salvar screenshots e marcações de início/fim.
7. **Sysinternals Process Explorer:** localizar `Editor.Timeline.Wpf.exe`, `Editor.Security.Worker.exe`, `ffmpeg.exe`, `ffprobe.exe` durante processamento e conferir que worker/codec não ficam órfãos após sucesso/fechamento. Processo rápido pode escapar a observação; ausência na lista não prova que não executou.
8. **Sysinternals Process Monitor (Procmon):** filtrar pelos nomes dos executáveis do editor e worker, e opcionalmente pelo caminho `D:\EditorMultimidia\Testes`. Capturar operações de arquivo/processo enquanto reproduz a ação; gravar `.PML` original localmente e exportar **CSV filtrado, sanitizado** para anexo. Nunca compartilhar caminhos, nomes de mídia pessoais, tokens, usuário ou documentos privados sem revisar.
9. Reabrir aplicativo e projeto, conferir mídias e alterações salvas. Para testar desconexão/queda, usar somente fixtures e procedimentos que não ofereçam risco à integridade do D:; **não desligar fisicamente o HD durante gravação nem matar processos de sistema**.
10. Compartilhar: descrição de reprodução (passos/esperado/obtido), versão/commit, arquivo xUnit/TRX, log do Visual Studio, exportação Procmon sanitizada, screenshots da WPF e horários. O assistente correlaciona eventos e propõe correção com CI completo e novo teste manual.

### Entrada, saída e falhas de cada componente

- `LibraryFileStore.ImportAsync(path)`: entrada arquivo escolhido, saída caminho da cópia na biblioteca; falhas I/O/ocupação/cópia parcial. A WPF só deve cadastrar mídia após inspeção bem-sucedida.
- `IsolatedMediaProcessingService.InspectDurationTicks(path)`: entrada mídia **já copiada** para biblioteca ativa, saída duração em ticks; falha se media/grant/identidade, IPC/codec ou deadline inválidos.
- `IsolatedMediaProcessingService.DecodeToNull(path)`: entrada mídia já cadastrada, saída sucesso/erro de decodificação sem gerar mídia exportada; falha fechada, não abre pipeline livre.
- `MediaWorkerAdmission.DemandMediaProcessing()`: permanece negando chamadas produtivas; não retirar flag para simplesmente passar num teste da interface.
- `Editor.LargeMediaFixture`: entrada tamanho aprovado em CMD, saída arquivo RF64 novo + manifesto JSON contendo hash do fluxo e prova de leitura de offsets; falhas interrompem sem promover arquivo parcial para final; sem PowerShell.

## BDD de homologação progressiva

- **Dado** biblioteca de testes no D:, **quando** a usuária importa WAV/MP4 e atualiza metadados, **então** a mídia aparece na timeline com duração correta e o diagnóstico comprova worker autenticado.
- **Dado** uma falha do worker, **quando** ocorrer durante importação, **então** erro é informado, arquivo original permanece e não há catálogo/timeline parcialmente confirmados.
- **Dado** arquivo sintético grande, **quando** for gerado em D:, **então** manifesto confirma tamanho, integridade, escrita real e leitura aleatória; não alegar decode/10GB só pelo gerador.
- **Dado** UI em evolução, **quando** recursos novos são entregues, **então** testes locais e CI são anexados e riscos ainda não cobertos permanecem visíveis, sem bloqueio global da UX.
- **Dado** intenção de distribuir o software, **quando** preparação de release começar, **então** origem e CVEs nativas corrigidas, licença e recuperação transacional precisam de aprovação específica.

## Próxima implementação

1. Priorizar MIG-91 (FFmpeg), verificando build LGPL estável e paridade com 9.0, **não substituir binários automaticamente** sem hash/prova de CVEs. A página oficial https://ffmpeg.org/legal.html diz que '--enable-gpl' altera as obrigações; versão LGPL não garante CVE corrigida.
2. Revisar protótipo de **interface WPF definitiva** com a usuária, depois implementar por blocos observáveis: biblioteca, importação/estados, timeline, gestão de referências, diagnósticos, recuperação.
3. Acrescentar painel/exportação de diagnósticos próprios da aplicação com RequestId, operação, estado, resultado, PID, duração e código de erro (sem nome de mídia/chave em logs públicos), para evitar dependência exclusiva de Procmon.
4. Repriorizar testes extremos e crash/cold-start como gate antes do lançamento, não como condição para desenhar interface.

**Decisão operacional:** todo comando fornecido à usuária deve ser do **Prompt de Comando (CMD)**, um por bloco; nenhum comando PowerShell.
