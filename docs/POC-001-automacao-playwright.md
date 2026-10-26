# POC-001 — integração com Playwright e TypeScript

Plano aprovado na conversa em 26/10/2026: conservar os testes unitários .NET, adicionar Playwright Test com TypeScript como executor de integração do motor real por um auxiliar .NET, e executar essa etapa somente após sucesso dos unitários. Interface WPF e seletores nativos permanecem na homologação manual. Não há servidor HTTP nem interface web simulada.

## Execução no Windows 11

Pré-requisitos: .NET SDK 10 estável, Node.js 24 LTS (inclui npm), FFmpeg e ffprobe no PATH. O FFmpeg gera somente pequenas mídias sintéticas de teste; o ffprobe mede suas durações. Não usar arquivos pessoais. Abra CMD na raiz do repositório.

Para compilar WPF, executar os unitários e, somente se passarem, executar a integração:

```cmd
tests\run-all.cmd
```

Para executar apenas a integração após preparar as dependências, entre na pasta:

```cmd
cd tests\playwright
```

```cmd
npm ci
```

```cmd
npm test
```

Para abrir o relatório HTML:

```cmd
npm run report
```

`npm test` compila o auxiliar, verifica os tipos TypeScript e executa Playwright. Não execute `playwright install`: esta suíte não usa browser, page ou context. Ausência de FFmpeg, ffprobe ou SDK causa falha explícita; não há testes ignorados automaticamente nem retentativas.

## Sequência no GitHub Actions

O job Windows existente agora executa: restore → build da solução com WPF → testes unitários xUnit → Node.js → ferramentas de mídia → npm ci → build do auxiliar → tipos TypeScript → Playwright. As etapas seguintes dependem do sucesso das anteriores; a publicação dos relatórios TRX, HTML e JUnit acontece inclusive em falhas, com retenção de 14 dias. Um único worker e zero retentativas. O auxiliar fica fora da solução principal, sendo compilado explicitamente na etapa de integração.

A instalação do FFmpeg usa o Chocolatey do runner apenas quando necessário. A versão efetiva de FFmpeg/ffprobe é impressa no log; ela pode mudar com o runner e o catálogo Chocolatey. Pacotes npm ficam fixados no package-lock.json. Não se instala software no computador da usuária automaticamente.

## Ferramentas e manutenção

| Componente | Finalidade | Manutenção |
|---|---|---|
| @playwright/test 1.64.1 | Executor, asserções, isolamento por cenário e relatórios | Atualização deliberada com regressão; sem navegadores nesta suíte |
| TypeScript 7.0.2 | Verificar tipos dos comandos e resultados antes de executar | Uma checagem `tsc --noEmit`, separada da transformação do Playwright |
| @types/node 24.10.1 | Tipos das APIs Node utilizadas para arquivos/processos | Compatível com a linha Node 24 |
| Auxiliar .NET 10 | Chamar diretamente Editor.Timeline sem reimplementar suas regras | Sem novas dependências NuGet; adaptar somente se os contratos públicos mudarem |
| FFmpeg/ffprobe | Gerar e medir mídia comprimida real | Disponibilidade e codecs são pré-requisitos explícitos; pequenos arquivos de 12 segundos |

Versões npm estáveis consultadas em 26/10/2026 no registro npm. Fontes oficiais: https://playwright.dev/docs/test-typescript e https://playwright.dev/docs/test-fixtures. Instalação SDK: https://dotnet.microsoft.com/download/dotnet/10.0. O suporte a TypeScript do Playwright não substitui a checagem estática, por isso existe etapa própria.

## Métodos e limites do auxiliar

`Program.cs` recebe um array JSON pelo stdin e responde um array de snapshots pelo stdout, um por comando. Cada processo começa com projeto vazio e encerra após o roteiro. Não expõe rede e não integra o aplicativo distribuído. Somente os testes chamam esse executável.

- `Project()` monta a representação do projeto corrente, sem gravar arquivos. `Text`, `Id` e `Time` leem entradas tipadas do comando; entrada malformada é erro de infraestrutura e encerra o processo.
- `import` usa InspectFile → PlanClip → TimelineSession.Execute; a referência só entra se a inserção funcionar. Duplicata retorna `duplicate`. Essa coordenação espelha o fluxo WPF, mas não testa o handler da janela.
- `insert`, `trimLeft`, `resize`, `split`, `reorder`, `independent`, `move` e `delete` encaminham IDs/tempos ao motor; `marker` usa TimelineMarkers. Cada operação usa a versão atual e o histórico real da sessão.
- `restore`, `relink` e `removeMedia` usam os comandos públicos da sessão. Erros esperados de regra ou I/O são devolvidos com tipo, código e mensagem; erros inesperados encerram o auxiliar e falham o teste.
- `undo`/`redo` usam as pilhas reais. `save` chama ProjectStore.Save; `load` só substitui a sessão depois de carregar e validar o candidato. `availability` verifica presença dos arquivos sem alterar a sessão.
- Cada snapshot inclui JSON v4 real, flags de histórico, duração formatada e eventual erro. As expectativas ficam em TypeScript, não no auxiliar.

`support.ts` agrupa os auxiliares de teste:

- `folder`: fixture Playwright que cria pasta exclusiva e a remove em finally. Falha de limpeza falha o teste.
- `run`: recebe roteiro e variáveis opcionais, inicia o auxiliar sem shell, limita tempo/saída e interpreta JSON. Falha de processo, timeout ou resposta inválida rejeita a execução. EDITOR_FFPROBE_PATH só muda no processo filho.
- `exec`: adaptação assíncrona de execFile para ferramentas locais; propagação de falhas e timeout.
- `wave`: escreve WAV PCM válido com duração conhecida; erros de disco são propagados. `media`: gera 12 s de áudio/vídeo real com FFmpeg, sem download de mídias.
- `ticks`: converte os segundos de referência para unidades de 100 ns. `allOk`: exige ausência de erro em cada resultado informado.

`acceptance.spec.ts`: `insert` monta o comando de importação, `hash` calcula SHA-256 para comparar o conteúdo físico antes/depois. Cada cenário tem nome BDD e asserções explícitas; snapshots completos incluem IDs, tempos, catálogo e versão do histórico.

## Rastreabilidade e lacunas

“Motor” significa verificação de integração; não substitui interações WPF. O documento manual v6.5 continua sendo a referência para homologação.

| CT | Integração nova | Complemento existente / limite |
|---|---|---|
| 001 | Projeto vazio e histórico vazio | Cursor, snap desligado e abertura da janela: manual |
| 002 | MP4 real de 12 s | Exemplo de 704,7 s e formatação: MediaMetadataTests/Poc001ClosureTests; apresentação: manual |
| 003 | Reuso de vídeo contíguo e reordenação | Inserção de vídeos distintos também em MediaMetadataTests |
| 004 | WAV PCM real de 12 s | Painéis e tooltip: manual |
| 005–006 | MP3, M4A, AAC, FLAC e OGG reais; referência ffprobe; salvar/reabrir | Medição é comparada ao mesmo executável, não a um medidor independente; vídeos pessoais não são utilizados |
| 007–008 | Inválido e ffprobe ausente com projeto/histórico preservados | Texto do diálogo: manual |
| 009 | Importação repetida e reutilização por MediaId | Fluxo de catálogo visual: manual |
| 010 | PNG, duração 4 s, corte/divisão sem origem temporal e hash intacto | Tooltip: manual |
| 011–013 | Cortes principal/secundário e divisão com continuidade de origem | Arrastes na janela: manual |
| 014–015 | Limites da fonte com redo preservado | Outros limites em Poc001ClosureTests |
| 016 | Corte secundário, borda direita e histórico | Vínculo/offset em LeftTrimTests/Poc001ClosureTests |
| 017 | Restauração, ondulação e undo/redo; fontes legadas v2/v3 | Filhos posicionados/independentes em MediaMetadataTests |
| 018–019 | Reordenação com filho posicionado e independente | Interface: manual |
| 020 | Aviso persiste em operação não relacionada; movimento limpa; undo restaura | TimelineTests também cobre movimento sem deslocamento |
| 021 | Alocação da primeira trilha livre na importação | Colisão por arraste em TimelineTests; gesto visual: manual |
| 022 | Nove trilhas ocupadas; décima rejeitada; undo/redo sem etapa extra | Mensagem na janela: manual |
| 023 | Marcador na sequência de histórico | MarkerTests e TimelineTests cobrem lógica; zoom, régua, snap e atalhos: manual |
| 024 | Importação → corte → divisão → marcador → undo completo → redo completo | Comparação do projeto completo |
| 025 | Save/load real com catálogo e duração | Efeitos/tempos exatos também em ProjectStoreTests; título da janela: manual |
| 026 | Fixtures sintéticas v1/v2/v3, preservação e gravação v4 | Não são amostras históricas independentes; migrar projetos reais continua pendente |
| 027 | Fonte renomeada, indisponibilidade, relink e undo/redo | Seletor nativo: manual |
| 028 | Destino de gravação bloqueado, JSON inválido, estado/histórico preservados e limpeza temporária | Cancelamentos/fechamento nativos: manual. Preservação de arquivo anterior em falha de validação também em ProjectStoreTests |
| 029 | Remoção em uso rejeitada; remoção sem uso reversível; arquivo físico intacto | Botão/desabilitação: manual |

Sem automação de UI WPF, POC-003/IPC, playback, exportação ou metas de desempenho. A duração de execução não comprova desempenho em HDD. Não houve mudança de regra de negócio nem alteração de código de produção.
