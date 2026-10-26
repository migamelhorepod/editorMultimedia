# POC-001 — Fechamento integrado · v6.5

**Estado:** implementação disponível numa única branch; **homologação manual ainda pendente**.
**Dependência:** baseada nos incrementos v4.7–v6.4, reunidos aqui em um Pull Request contra `main`.

## 1. Escopo de aceite

| Regra | Contrato verificável |
|---|---|
| Identidade | Arquivo catalogado tem `MediaId` estável; ocorrências possuem `Clip.Id` diferentes. |
| Dez trilhas | Principal é 0, secundárias 1–9; não exceder dez nem sobrepor duas ocorrências na mesma trilha. |
| Principal | Vídeos entram ao final sem lacunas, na duração real do arquivo; reordenação e cortes ondulam apenas clipes posteriores. |
| Secundárias | Áudios e imagens entram no instante do cursor e usam a primeira trilha disponível. |
| Duração real obrigatória | Áudio e vídeo são inseridos **somente** se há medição positiva da duração total original; não aplicar qualquer duração temporal de fallback. |
| Leitores de mídia | WAV PCM/float e MP4/MOV compatíveis podem ser medidos diretamente; os demais tipos temporais dependem do `ffprobe` confiável do Windows. |
| Imagem | Uma imagem estática permanece com duração editável inicial de 4 s; não possui recorte de origem temporal. |
| Tempo visual | Apresentar `HH:MM:SS[,fff]` no catálogo, timeline, régua e barra de status. Não arredondar valores persistidos. |
| Fonte e clipe | `SourceStart` = início real dentro do arquivo; `Duration` = extensão temporal da ocorrência; `SourceEnd` = soma exata. |
| Corte à esquerda | Ao encurtar, avança `SourceStart`; ao alongar um clipe já cortado, recua `SourceStart` até o mínimo de zero, sem ultrapassar a fonte. |
| Corte à direita | `SourceStart` não muda. Áudio/vídeo podem ser encurtados e alongados novamente, respeitando `SourceStart + Duration ≤ duração original`. Imagens possuem duração livremente editável. |
| Divisão | Clipes resultantes usam intervalos de origem contíguos, sem recomeçar o lado direito em 00:00. |
| Restauração da fonte | **Restaurar duração original do clipe**, no clique direito do clipe na timeline, zera `SourceStart`, recupera a duração integral aferida e aplica ondulação à principal, com Ctrl+Z/Y. |
| Associação | Posicionadas acompanham pai; independentes ficam em tempo absoluto. Avisos de vínculo perdido persistem até movimento. |
| Colisão | Tenta outra trilha secundária; se todas ocupadas, erro e alteração atômica (mensagem existente). |
| Histórico | Todas as operações mutantes acima integram Ctrl+Z/Y com uma etapa por gesto/comando. |
| Projeto | Novo vazio, abrir, salvar, salvar como, alterações pendentes, confirmação de fechamento. |
| Persistência | JSON v4, com IDs, tipos, tempos de 100 ns, source start e flag de alcance validado; ler v1–v3. |
| Resiliência | Falha de importação/relocalização/corte, mídia indisponível, cancelamento e erro de gravação não devem corromper a edição. |
| Arquivos originais | Nunca copiar, excluir, sobrescrever ou recodificar mídias como efeito colateral de edição da timeline. |

## 2. Leitura obrigatória de duração

Uma mídia temporal **sempre tem duração real** para fins de entrada na timeline.
Distinguir o fato de o arquivo possuir duração da capacidade técnica de medi-la:
se a extração falhar, o arquivo **não deve** produzir clipe truncado de 5 s.

- Para WAV PCM/float compatível, o leitor interno de cabeçalhos obtém comprimento de dados e taxa de bytes.
- Para MP4/MOV compatível, o leitor interno interpreta `moov/mvhd` sem decodificação (incluindo contêiner com `moov` no final).
- Para MP3, AAC, M4A, FLAC, OGG, WAV comprimido, MKV, AVI, WebM e outros formatos temporais reconhecidos pela extensão, chamar **`ffprobe`** para identificar a duração do contêiner ou da faixa correspondente. Exigir stream de áudio/vídeo do tipo esperado, duração válida e limite máximo.
- Se `ffprobe` não estiver instalado, expirar, falhar ou não conseguir medir a fonte, **recusar o clipe** e mostrar orientação para instalar/configurar a ferramenta. Não aceitar estimativa arbitrária.
- A invocação usa `ProcessStartInfo.ArgumentList`, sem shell e com limite de tempo. Ainda **não** há sandbox de codec nem isolamento efetivo homologado da POC-003.

### Dependência Windows

No CMD:

```cmd
winget install --id Gyan.FFmpeg --exact
```

Depois reinicie o terminal ou VS Code e confira:

```cmd
ffprobe -version
```

Sem essa dependência, MP4/MOV e WAV simples ainda podem ser importados pelos leitores internos, mas mídias comprimidas que exigem `ffprobe` **não terão fallback**.

## 3. Relógio e cortes

Exemplo: arquivo de `00:11:44,7` = `704,7` s. A timeline apresenta `00:11:44,7`; o motor armazena `7.047.000.000` ticks.

Ao cortar de `00:00:00` para `00:02:30` à esquerda, uma ocorrência que inicialmente representava toda a fonte passa a representar o intervalo **[02:30, 11:44,7)** da mídia. Dividir o restante em `05:00` da fonte conserva o offset correto na segunda parte. A marca de exclusão do final é semântica; não transforma a unidade de tempo em frames.

**Projetos legados:** um clipe salvo em v1–v3 pode ter sido criado antes da medição de duração e conter intervalo impossível. Ao abrir/atualizar metadados, suas edições não são destruídas silenciosamente. Somente novos intervalos ou mudanças nos intervalos serão validados contra a fonte; a ação explícita **Restaurar duração original** normaliza a ocorrência. Clipes novos marcam `SourceRangeVerified=true`.

## 4. Roteiro integrado de aceitação manual no Windows

Marque cada caso após exercitar o aplicativo. A homologação exige que todos os casos críticos funcionem, **inclusive formatos comprimidos com ffprobe instalado**.

- [ ] Criar projeto vazio (`Ctrl+N`), importar MP4 de **00:11:44,7**; catálogo e timeline mostram esse tempo, não `704,7s` nem `5s`.
- [ ] Importar segundo vídeo; inicia após o primeiro na principal, sem lacuna e sem alterar o primeiro.
- [ ] Importar WAV PCM com duração conhecida e MP3/M4A/AAC/FLAC/OGG válidos com `ffprobe`; todos usam suas durações medidas, nunca 5 s arbitrários.
- [ ] Importar arquivo temporal inválido ou remover `ffprobe` do PATH; recebe erro, e timeline/catálogo/histórico permanecem intactos.
- [ ] Importar imagem; duração inicial 4 s, alterar sua duração e dividir, sem `SourceStart` diferente de zero.
- [ ] Cortar 2 s da esquerda de um vídeo, conferir início na fonte e duração. Cortar à direita não altera início na fonte.
- [ ] Dividir o vídeo, conferir que o segundo clipe começa no instante correspondente na fonte e que o total da timeline permanece coerente.
- [ ] Encurtar áudio e vídeo pelas duas bordas; alongar novamente **dentro da fonte** deve funcionar. Tentar ultrapassar início/fim real deve recusar sem criar histórico. Imagens estáticas mantêm duração livre.
- [ ] Clique direito num clipe de áudio/vídeo > **Restaurar duração original do clipe**, verificando duração total, origem zero, ondulação e Ctrl+Z/Y.
- [ ] Reordenar principais; mídias posicionadas acompanham seu pai e independentes permanecem em tempo absoluto.
- [ ] Provocar colisão com múltiplas secundárias; verificar realocação para a primeira trilha livre. Com todas as 9 ocupadas, deve mostrar aviso e não alterar estado.
- [ ] Criar, arrastar e remover marcador; usar as setas e zoom; encaixe inicia **desligado**.
- [ ] Salvar projeto, fechar e reabrir. Conferir catálogo, durações, offsets de fonte, efeitos representados, vínculos e marcadores.
- [ ] Abrir um projeto antigo em formatos v1, v2 e v3; a abertura deve preservar os clipes. Ao salvar novamente, o arquivo passa a formato v4.
- [ ] Mover arquivo original no Explorer, verificar **Mídia indisponível**, relocalizar pelo seletor e conferir preservação de IDs e edições.
- [ ] Corrigir um clipe legado de 5 s pelo clique direito > **Restaurar duração original do clipe** e conferir `Ctrl+Z`/`Ctrl+Y`.
- [ ] Cancelar Salvar como e fechar com alterações pendentes; projeto não é descartado. Corromper um JSON de teste e tentar abrir; a edição corrente permanece.
- [ ] Abrir Pull Request, observar build WPF e testes no GitHub Actions; nenhuma falha ou warning de compilação/testes .NET.

## 5. Cobertura automatizada

`Poc001ClosureTests.cs` consolida cenários de formatação, duração aferida e recusa de ausentes, offsets da fonte, ambas as bordas, divisão, duplicação, ordem, histórico, formatos v1–v4 e consistência de dados do `ffprobe`.

Continuam valendo os testes de `TimelineTests`, `LeftTrimTests`, `MarkerTests`, `ProjectStoreTests`, `MediaReferencesTests`, `MediaRelinkTests`, `ProjectMediaCatalogTests` e `MediaMetadataTests`.

**Limite do CI:** testes sintéticos validam a lógica, mas a inspeção de arquivos reais comprimidos através do executável `ffprobe` e as interações visuais exigem teste manual local. Aprovação automatizada **não é** homologação final.

## 6. Fora do escopo desta POC

- Reprodução/decodificação, miniaturas, FPS efetivo, renderização/exportação: POC-002.
- Grant por recurso, isolamento Windows de workers, autenticação IPC e revogação: POC-003.
- Bibliotecas físicas multi-diretório, metas de desempenho do HDD, indexação com milhares de itens: POCs dedicadas.
- Efeitos/keyframes vinculados à fonte, velocidade variável, áudio multifaixa real e precisão visual por quadros: requerem contratos específicos das próximas etapas.

**Critério de conclusão:** merge somente após GitHub Actions verde **e confirmação manual dos casos críticos** pelo responsável pelo produto. O projeto ainda não é um editor audiovisual reproduzível; esta entrega homologa somente a edição lógica da timeline.
