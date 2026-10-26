# Casos de teste manuais — Editor Multimídia v6.5 / POC-001

**Objetivo:** homologar em **um único ciclo** a branch `feature/poc001-fechamento-integrado`, incluindo a regra de que **TODO áudio e TODO vídeo precisam de duração realmente aferida para entrar na timeline**.

**PR:** [#5 — Fechamento da POC-001](https://github.com/migamelhorepod/editorMultimedia/pull/5)  
**Responsável:** ____________________  
**Data:** ____________________  
**Ambiente:** Windows ______ / VS Code ______ / .NET SDK ______ / ffprobe ______  
**Build/commit testado:** ____________________

## Como registrar

Preencha uma marca em cada caso: `[ ] Aprovado`, `[ ] Reprovado`, `[ ] Bloqueado` ou `[ ] Não aplicável`. Anote o **obtido** caso difira do esperado e, quando falhar, anexe captura e log. Marque como **bloqueador** qualquer falha P0: não integrar o PR à `main` antes da correção.

Um caso com medição na interface é **inspeção lógica de edição**: esta POC **não** reproduz áudio/vídeo nem permite confirmar quadros visuais por playback.

### Preparação

1. No CMD, confira `dotnet --version` e `ffprobe -version`. Para instalar o segundo, caso necessário: `winget install --id Gyan.FFmpeg --exact` e **reabra** o terminal/VS Code.
2. Faça `git fetch origin` e acesse a branch com `git switch --track origin/feature/poc001-fechamento-integrado` (se ela já existir localmente, `git switch feature/poc001-fechamento-integrado` seguido de `git pull --ff-only`).
3. Execute `dotnet test EditorMultimidia.sln` e `dotnet run --project src\Editor.Timeline.Wpf\Editor.Timeline.Wpf.csproj`.
4. Use uma **pasta de teste** para as mídias e projetos. Trabalhe com cópias dos arquivos ao simular mídia ausente. Não altere um projeto de produção.
5. Prepare ou selecione estas mídias reais, conferindo duração antes dos testes:
   - **V1** — o MP4 da captura, `2026-10-11 20-42-34.mp4`, duração esperada próxima de **00:11:44,7** segundo o dado medido pelo editor.
   - **V2** — outro vídeo MP4 curto, preferencialmente **20–30 segundos**, para testar cortes e divisão visualmente com zoom.
   - **A1** — WAV **PCM descompactado** com duração conhecida de pelo menos 10 segundos.
   - **A2** — MP3 válido com duração **diferente de 5 s**.
   - **A3** — um M4A ou AAC válido e, se disponíveis, FLAC e OGG com duração conhecida.
   - **I1** — uma imagem PNG válida.
   - **X1** — cópia descartável de arquivo inválido ou arquivo de texto renomeado com extensão `.mp3`.
   - **L1** — um projeto antigo da versão v6.1/v6.3/v6.4, se você tiver um disponível, incluindo clipe MP4 anteriormente cortado para 5 segundos.
6. **Medição de referência:** os segundos obtidos por `ffprobe` e a UI podem diferir dos segundos inteiros mostrados pelo Explorador. Compare até os milissegundos apresentados, **não** com o arredondamento do Explorador.
7. Para os testes com cortes muito pequenos, aumente o **Zoom** ou use **Ajustar zoom**; passe o mouse sobre o clipe para ler `Trecho na fonte`, `início` e `duração editada`.

### Critério geral

**P0 = bloqueador de homologação.** **P1 = regressão funcional importante.** **P2 = apresentação/ergonomia.**

---

## A. Inicialização, duração e catálogo

### CT-001 — Projeto vazio e configuração inicial | P0
**Pré-condição:** aplicativo recém-aberto ou `Ctrl+N` em projeto sem alterações não salvas.  
**Passos:** (1) Clique em **Arquivo > Novo projeto vazio**. (2) Observe a timeline e o painel de mídias. (3) Inspecione **Encaixar a cada 0,5s**.  
**Esperado:** nenhuma mídia/clipe; cursor inicia em zero; encaixe **desligado**; até 10 trilhas possíveis. Não é iniciada demonstração automaticamente.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-002 — Duração real e formatação do MP4 V1 | P0
**Pré-condição:** projeto vazio; V1 disponível.  
**Passos:** (1) `Ctrl+I`, selecione V1. (2) Observe o painel **Mídias do projeto**. (3) Observe o clipe na trilha **Principal** e seu tooltip. (4) Clique **Ajustar zoom** para navegar pelo clipe.  
**Esperado:** uma mídia e **um clipe com duração 00:11:44,7 (aproximadamente)**, não `704,7s`, `00:00:05` nem `5s`; começa em `00:00:00`; tooltip distingue duração da fonte e do clipe; sem corte automático.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-003 — Segundo vídeo segue a principal sem lacuna | P0
**Pré-condição:** CT-002 concluído, V2 disponível.  
**Passos:** (1) Importe V2. (2) Localize os dois clipes na Principal. (3) Inspecione o início de V2 e a duração total.  
**Esperado:** V2 inicia **exatamente onde V1 termina**; duração de V1 continua íntegra; nenhum espaço/overlap na trilha principal.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-004 — WAV PCM é importado com sua duração real | P0
**Pré-condição:** vídeo importado e A1 disponível.  
**Passos:** (1) Posicione o cursor em momento livre. (2) Importe A1. (3) Veja a duração no catálogo e tooltip do clipe.  
**Esperado:** WAV entra em uma trilha secundária com a **duração medida de A1**, nunca 5s por padrão; fonte preservada e mesmo tempo exibido como `HH:MM:SS[,fff]`.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-005 — MP3 tem duração real via ffprobe | P0
**Pré-condição:** `ffprobe -version` funciona; A2 é um MP3 válido com duração diferente de 5s.  
**Passos:** (1) `Ctrl+I` e importe A2. (2) Compare o catálogo, o clipe e o tempo aferido pelo ffprobe. (3) Salve, feche e abra o projeto.  
**Esperado:** MP3 entra na secundária com sua **duração real**, exibida como tempo legível, preservada após reabertura; nunca uma duração fictícia de 5s.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-006 — M4A/AAC/FLAC/OGG com duração medida | P0
**Pré-condição:** ffprobe funcionando, arquivos A3 válidos.  
**Passos:** (1) Importe um M4A ou AAC e, se disponíveis, FLAC e OGG, um por vez. (2) Observe cada duração no catálogo/clipe.  
**Esperado:** cada formato **suportado e válido** entra com duração medida, nunca 5s arbitrários. Registre exatamente qual formato falhou. Um formato não disponível pode ser `N/A`, sem bloquear as verificações dos demais.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-007 — Arquivo temporal inválido não cria clipe fictício | P0
**Pré-condição:** cópia X1 inválida (texto renomeado como `.mp3` ou outro arquivo deliberadamente corrompido).  
**Passos:** (1) Anote quantidades de mídias/clipes. (2) `Ctrl+I` e tente importar X1. (3) Confira quantidades e duração total.  
**Esperado:** mensagem de erro clara de duração não identificada; **não** cria entrada no catálogo/timeline; nenhum clipe de 5 s; outras edições não mudam.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-008 — ffprobe indisponível: falha segura, sem fallback | P0
**Pré-condição:** usar janela CMD **separada** e **apenas temporária**, sem alterar a configuração global.  
**Passos:** (1) No CMD de teste, execute `set "EDITOR_FFPROBE_PATH=C:\ffprobe-qa-inexistente\ffprobe.exe"`. (2) Inicie o editor **nessa mesma janela**, com `dotnet run --project src\Editor.Timeline.Wpf\Editor.Timeline.Wpf.csproj`. (3) Crie projeto vazio e importe MP3 A2. (4) Feche essa instância; outras janelas não são afetadas.  
**Esperado:** recusa a importação do MP3, com orientação; **não** cria clipe de 5s nem entrada no catálogo. Uma mídia WAV PCM/MP4 compatível com leitor interno ainda pode ser importada.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-009 — Importação duplicada e reutilização da fonte | P1
**Pré-condição:** projeto com A2 já importado.  
**Passos:** (1) Tente importar o **mesmo arquivo** via `Ctrl+I`. (2) No catálogo, selecione A2, mude o cursor para outra posição e clique **Usar no cursor** (ou dê clique duplo).  
**Esperado:** importação repetida **não duplica a referência**; ação **Usar no cursor** cria **outra ocorrência** na timeline, ambas vinculadas à mesma mídia, sem copiar o original.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-010 — PNG tem duração de edição e não origem temporal | P1
**Pré-condição:** PNG I1 disponível.  
**Passos:** (1) Importe I1. (2) Confirme `00:00:04` inicial. (3) Corte sua borda esquerda e divida dentro do clipe. (4) Inspecione tooltips.  
**Esperado:** imagem não exige duração de áudio/vídeo; suas ocorrências têm duração editável; **não** ganham `SourceStart` temporal diferente de zero; o arquivo original é intocado.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

---

## B. Trechos da fonte e cortes na timeline

> Para CT-011 a CT-016, prefira **um projeto novo** contendo V2 na principal, mais outro vídeo ou áudio quando indicado. Use o Zoom para visualizar alças de redimensionamento. O `SourceStart` pode ser inspecionado em **Trecho na fonte** no tooltip; sem reprodução, o teste atesta valores e integridade lógica.

### CT-011 — Corte à esquerda avança dentro da fonte | P0
**Pré-condição:** V2 de ~20s na Principal, não editado.  
**Passos:** (1) Arraste **apenas a borda esquerda** cerca de 2s para a direita. (2) Inspecione tooltip.  
**Esperado:** começo do clipe **na timeline** permanece 00:00:00 se ele for o primeiro principal; sua duração diminui ~2s; trecho na fonte começa em ~`00:00:02` e termina no fim original.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-012 — Corte à direita não altera início dentro da fonte | P0
**Pré-condição:** após CT-011.  
**Passos:** (1) Arraste **a borda direita** ~3s para a esquerda. (2) Inspecione a origem.  
**Esperado:** o início na fonte continua no tempo configurado no CT-011 (~2s); apenas a **duração editada** e o fim na fonte diminuem.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-013 — Divisão preserva continuidade dentro do arquivo | P0
**Pré-condição:** V2 já cortado à esquerda; cursor **dentro** do clipe, não na borda.  
**Passos:** (1) Clique na régua para colocar o cursor cerca de 5s após o início do clipe. (2) Selecione o clipe e use `Ctrl+B` (Dividir no cursor). (3) Passe o mouse em cada metade.  
**Esperado:** duas ocorrências contíguas na timeline; **fim na fonte da esquerda = início na fonte da direita**; o segundo clipe não recomeça em `00:00:00`; soma das duas durações igual à anterior.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-014 — Não estender além do fim do arquivo | P0
**Pré-condição:** V2 ou A1 em duração integral, sem corte à direita, com fonte aferida.  
**Passos:** (1) Tente arrastar a **borda direita para além** da duração original. (2) Solte. (3) Observe a duração, demais clipes e `Ctrl+Z`.  
**Esperado:** operação rejeitada com aviso, sem crescimento do clipe, sem deslocamento de outros e **sem nova etapa de histórico**.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-015 — Não estender antes do primeiro segundo da fonte | P0
**Pré-condição:** use um vídeo **não editado** que não seja o primeiro na principal, ou um áudio sem corte na secundária.  
**Passos:** (1) Arraste a borda esquerda **para a esquerda** de seu ponto inicial, tentando aumentar a duração sem espaço de origem disponível. (2) Solte.  
**Esperado:** nenhuma origem temporal negativa; operação rejeitada sem alterar o projeto nem histórico. **Não é permitido roubar duração do clipe anterior.**  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-016 — Corte secundário preserva a borda direita e a associação | P0
**Pré-condição:** A1 (ou A2) na trilha secundária, dentro da duração de um vídeo principal.  
**Passos:** (1) Corte ~2s pela borda esquerda do áudio. (2) Confira posições na timeline e origem no tooltip. (3) Desfaça/refaça.  
**Esperado:** borda direita absoluta permanece, início na timeline avança, duração diminui, `SourceStart` avança e, se posicionado, vínculo/offset são atualizados. Ctrl+Z/Y restaura o estado exato.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-017 — Restaurar duração original de clipe legado | P0
**Pré-condição:** projeto L1 contendo vídeo importado no passado com duração de 5s, mais um clipe principal depois dele.  
**Passos:** (1) Abra L1. (2) Selecione o clipe curto. (3) Clique **Restaurar duração original do clipe** (ou clique direito). (4) Faça `Ctrl+Z` e `Ctrl+Y`.  
**Esperado:** clipe curto volta à **duração total medida**, `SourceStart` é zerado; próximo principal é deslocado, clipes posicionados acompanham seu pai, independentes não se movem; desfazer/refazer exatos.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

---

## C. Associações, colisões, navegação e histórico

### CT-018 — Reordenação principal preserva vínculos e origens | P0
**Pré-condição:** dois vídeos principais; áudio/imagem posicionados dentro do segundo.  
**Passos:** (1) Reordene o segundo vídeo para antes do primeiro usando arraste. (2) Inspecione a mídia vinculada e os trechos de fonte.  
**Esperado:** principal continua contínua; secundárias **posicionadas** acompanham seu vídeo; `SourceStart` e duração de todos os clipes permanecem iguais.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-019 — Mídia independente não acompanha reordenação | P0
**Pré-condição:** projeto do CT-018.  
**Passos:** (1) Clique direito num clipe secundário posicionado > **Tornar independente**. (2) Anote seu tempo absoluto. (3) Reordene os principais.  
**Esperado:** clipe independente permanece no **mesmo instante absoluto da timeline**, sem vínculo com pai. Seu trecho da fonte não muda.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-020 — Vínculo perdido gera aviso persistente | P1
**Pré-condição:** secundário posicionado começa perto do fim do vídeo pai.  
**Passos:** (1) Encurte o pai até que o início da secundária fique fora da duração dele. (2) Observe aviso. (3) Faça uma operação sem mover esse secundário. (4) Arraste o secundário para uma posição válida.  
**Esperado:** aviso de **vínculo perdido** persiste até a mídia ser realmente movida; Undo restaura os estados correspondentes; não existe botão manual de dispensa do aviso.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-021 — Colisão realoca a mídia secundária | P1
**Pré-condição:** dois áudios/imagens com sobreposição temporal potencial.  
**Passos:** (1) Coloque um na trilha secundária ocupada do outro, no mesmo intervalo. (2) Solte o arraste.  
**Esperado:** o clipe arrastado mantém seu instante, mas é **realocado automaticamente para a primeira trilha secundária disponível**. O outro não é deslocado.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-022 — Todas as nove secundárias ocupadas | P0
**Pré-condição:** ocupação simultânea de trilhas 2–10 no mesmo intervalo, com cópias/ocorrências suficientes.  
**Passos:** (1) Tente inserir ou mover mais uma mídia para esse intervalo, quando não existe faixa livre. (2) Anote a quantidade de clipes e o estado.  
**Esperado:** mensagem **“Não é possível criar mais trilhas”**, sugerindo realocação; nenhum clipe desaparece nem é sobrescrito; falha atômica, Undo não ganha ação de falha.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-023 — Marcadores, zoom, cursor e encaixe | P1
**Pré-condição:** projeto com vários clipes, encaixe inicialmente desligado.  
**Passos:** (1) Clique na régua para mover o cursor. (2) Use `M` para marcador e `Shift+M` para retirar; opcionalmente `Alt+setas` para navegar entre marcadores. (3) Mova o slider **Zoom**, `Ctrl+roda do mouse` e clique **Ajustar zoom**. (4) Ative/desative encaixe de 0,5s.  
**Esperado:** cursor, marcadores e zoom operam sem deslocar clipes indevidamente; encaixe somente quando ativado; apresentação da régua e do status é legível como `HH:MM:SS[,fff]`.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-024 — Histórico cronológico após várias ações | P0
**Pré-condição:** projeto de teste com fonte real.  
**Passos:** (1) Importe áudio. (2) Insira outra ocorrência por **Usar no cursor**. (3) Corte a ocorrência. (4) Adicione marcador. (5) Faça `Ctrl+Z` sucessivamente e depois `Ctrl+Y`.  
**Esperado:** cada ação desfaz/refaz **uma vez e na ordem**; catálogo, clipes, origem temporal, marcadores e vínculos retornam aos estados anteriores sem corrupção.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

---

## D. Persistência, disponibilidade e proteção de dados

### CT-025 — Salvar e reabrir preserva cortes, tempos e mídias | P0
**Pré-condição:** projeto com vídeo, áudio, imagem, corte à esquerda, divisão e marcador.  
**Passos:** (1) `Ctrl+S` e salve como `QA_POC001_v65.emproj.json`. (2) Feche e reabra o editor. (3) `Ctrl+O` e abra o projeto salvo. (4) Compare catálogo, tooltips, posições e total.  
**Esperado:** mídias, intervalos na fonte, clipes e marcadores intactos; tempos iguais; sem arquivos originais alterados; o nome da janela não mostra `*` após salvar.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-026 — Formato v4 e leitura de projetos antigos | P0
**Pré-condição:** cópias seguras de projetos v1–v3 reais, se disponíveis.  
**Passos:** (1) Abra um projeto anterior, confira os clipes. (2) Salve **uma cópia**, sem sobrescrever o original. (3) Abra o `.emproj.json` salvo em editor de texto e procure `"formatVersion": 4`, `sourceStartTicks` e `sourceRangeVerified`.  
**Esperado:** leitura de v1–v3 preserva edições; versão salva é v4; clipes antigos sem origem medida não são cortados silenciosamente. Se não existirem arquivos legados de teste, marque `N/A` e registre a falta de amostras reais.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-027 — Mídia indisponível e relocalização manual | P0
**Pré-condição:** projeto salvo com clipe real; usar **cópia** do arquivo fonte.  
**Passos:** (1) Feche o editor ou deixe a timeline aberta. (2) Renomeie a cópia no Explorador. (3) Execute **Arquivo > Verificar mídias vinculadas**. (4) No catálogo ou clique direito, use **Relocalizar…** e aponte para o novo arquivo. (5) Desfaça/refaça e salve.  
**Esperado:** aviso de fonte ausente, sem perda de clipes/cortes/ID; relocalização restaura disponibilidade e nome/caminho sem apagar edições; Undo/Redo reversível.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-028 — Cancelar e abrir JSON inválido não apaga projeto atual | P0
**Pré-condição:** projeto com edições não salvas e uma cópia descartável de `.emproj.json` inválida.  
**Passos:** (1) Modifique uma mídia na timeline e confirme o `*` na janela. (2) Faça `Ctrl+N` ou feche a janela; escolha **Cancelar** na confirmação. (3) Escolha **Salvar como…** e cancele o seletor. (4) Tente abrir JSON inválido de teste.  
**Esperado:** projeto atual e edição pendente permanecem intactos; nenhum arquivo de origem é apagado; erros são informados sem substituir o projeto ativo.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

### CT-029 — Remover mídia não utilizada não exclui arquivo físico | P1
**Pré-condição:** A2 no catálogo, com uma ou mais ocorrências.  
**Passos:** (1) Tente remover referência enquanto ainda existem clipes. (2) Exclua todas as ocorrências da timeline. (3) Selecione A2 no catálogo, clique **Remover mídia não utilizada**. (4) Confira arquivo no Explorador e `Ctrl+Z`.  
**Esperado:** ação desabilitada/bloqueada enquanto houver clipes; ao remover sem uso, referência some do catálogo, mas **arquivo físico continua no disco**. Undo restaura referência.  
**Resultado:** [ ] Aprovado [ ] Reprovado [ ] Bloqueado [ ] N/A  
**Evidência/observação:** ____________________

---

## Resumo de homologação

| Categoria | Casos | Aprovados | Reprovados | Bloqueados | N/A |
|---|---:|---:|---:|---:|---:|
| A. Inicialização/duração/catálogo | 10 | ___ | ___ | ___ | ___ |
| B. Origem/cortes | 7 | ___ | ___ | ___ | ___ |
| C. Associações/colisões/histórico | 7 | ___ | ___ | ___ | ___ |
| D. Persistência/resiliência | 5 | ___ | ___ | ___ | ___ |
| **Total** | **29** | ___ | ___ | ___ | ___ |

**Aceite sugerido:** todos os P0 executáveis aprovados, nenhum defeito que produza duração inventada ou perda de dados, demais falhas classificadas, e GitHub Actions verde no commit revisado. Se faltar fixture v1–v3, registre `N/A` e sinalize a lacuna de teste em vez de declarar essa migração homologada por completo.

### Modelo para registrar falha

```text
Caso: CT-___
Situação: Reprovado / Bloqueado
Versão/commit:
Arquivo utilizado e duração aferida:
Passos realizados:
Resultado esperado:
Resultado observado:
Mensagem de erro:
Captura de tela:
Reproduzível? Sempre / Intermitente
```

**Observação:** estes são testes **manuais de aceitação**. Os testes automatizados de .NET continuam no projeto de testes e não substituem os casos com MP3, interface WPF e arquivos reais.
