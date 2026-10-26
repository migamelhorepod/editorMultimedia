# Editor Multimídia v6.4 — inspeção limitada de metadados

Esta versão avança o catálogo de fontes reais, **sem instalar FFmpeg e sem decodificação de vídeo ou áudio**.

## Comportamentos implementados

1. **MP4/MOV de contêiner ISO Base Media File/QuickTime compatível**: usa o campo `mvhd` da caixa `moov`, sem decodificar vídeo. Suporta versões 0 e 1 do cabeçalho e caixas com tamanho estendido, mesmo que `moov` esteja no final do arquivo, após um `mdat` grande. **O vídeo entra na trilha principal com a duração identificada, não com 5 segundos.**
2. **Vídeos cuja duração não possa ser determinada**: a inserção na timeline é recusada com aviso, em vez de criar um corte silencioso de 5 s. Os demais contêineres (MKV, AVI, WebM) aguardam um mecanismo de inspeção completo.
3. **Restaurar duração original** (menu contextual do clipe ou botão lateral): reanalisa o arquivo vinculado e aplica o tempo total à ocorrência selecionada. Na trilha principal, os clipes seguintes são deslocados pelo motor da timeline; mídias posicionadas seguem os pais e as independentes preservam suas posições. É uma ação única de Ctrl+Z/Y, e outras edições manuais não são alteradas.
4. **WAV PCM ou IEEE float simples** com cabeçalho RIFF/WAVE consistente: identifica a duração no formato de ticks de 100 ns, lendo apenas cabeçalhos e os comprimentos dos blocos. Os clipes **novos** criados dessa mídia utilizam a duração aferida. O código verifica taxa de amostragem, canais, block alignment e integridade básica dos tamanhos.
5. **Imagens PNG** com assinatura e IHDR reconhecidos: coleta dimensões em pixels sem decodificar a imagem. A duração do clipe de imagem continua configurada em **4 s** por padrão.
6. **Outros formatos** (MP3, FLAC, AAC, MKV, AVI, WebM, JPEG etc.), WAV comprimido e cabeçalhos inválidos: preservam o comportamento anterior (**5 s** provisórios para áudio; vídeos sem duração comprovada não são inseridos; **4 s** para imagens). Não afirma duração ou dimensão que não tenha medido.
7. O painel do projeto exibe **duração de WAV ou MP4/MOV aferida**, **dimensões PNG** e disponibilidade dos arquivos. O tooltip de cada clipe distingue a duração do clipe da duração da fonte WAV.
8. **Atualizar dados** no painel: relê os metadados de um arquivo já catalogado — inclusive vindo de um projeto antigo — sem recriar referências e sem mudar os cortes ou durações de clipes existentes. Se os dados mudarem, a atualização integra o histórico de **Desfazer/Refazer**. Arquivos ausentes exigem relocalização prévia.
9. O formato **`.emproj.json` v3** persiste `durationTicks`, `pixelWidth` e `pixelHeight`, todos opcionais. Os projetos v1 e v2 continuam abrindo. Salvar um projeto antigo grava v3.
10. O disquete permanece o único mecanismo de salvamento do projeto. A atualização de dados NÃO altera os arquivos de mídia originais.
11. Validação de esquema rejeita dimensões negativas/fora do limite, metadados inconsistentes e duração não positiva. Os testes usam pequenos WAV/PNG gerados em memória, sem codecs nem ferramentas externas.

## Roteiro de teste manual

- Crie um projeto vazio. Importe um WAV PCM válido de 8 ou 16 bits e confira o tempo real aferido no painel e na timeline (em vez dos 5 s provisórios).
- Importe um PNG e confira largura × altura no painel. A duração da imagem deve continuar 4 s.
- Importe um MP4 de **00:11:44**. Ele deve entrar na trilha principal com **704 segundos**, sem corte automático. Importe outro vídeo e confira que ele começa após os 704 s.
- Se já houver um clipe MP4 importado anteriormente com 5 s, selecione esse clipe e use **Restaurar duração original**. Confirme que ele cresce até 11m44, que os clipes posteriores acompanham e que Ctrl+Z restaura os 5 s anteriores.
- Importe um MP3: áudio sem metadados ainda terá duração **provisória** de 5 s. Um vídeo MKV/AVI/WebM sem medição deve produzir aviso e **não** gerar clipe de 5 s.
- Abra um projeto v6.3 (`formatVersion: 2`) com WAV já importado. Selecione-o no painel e clique em **Atualizar dados**. Os novos metadados devem aparecer sem mexer em cortes já realizados. Faça Ctrl+Z, Ctrl+Y, salve, feche e reabra.
- Renomeie o arquivo de origem e tente atualizar dados: a operação deve ser rejeitada sem mudar o projeto. **Relocalizar** continua disponível.
- Confirme que a interface abre sem avisos e que o GitHub Actions aprova a compilação WPF e os testes.

## Segurança e limites

- Leitura síncrona e limitada de cabeçalhos de arquivos **explicitamente selecionados**. MP4/MOV: até 1024 caixas principais e 2048 caixas de `moov`, com leitura de cabeçalhos limitada a 16 MiB após entrar em `moov`; bytes de `mdat` são pulados. WAV: até 128 chunks e limite de varredura de 4 MiB antes do primeiro bloco útil; carga de dados de áudio é pulada, não decodificada. PNG: apenas 24 bytes de cabeçalho.
- Não há validação de integridade integral de PNG/codec WAV nem prova de equivalência de conteúdo na relocalização. Não interpretar metadados lidos como prova de arquivo reproduzível.
- Não há playback, thumbnails, FPS, inspeção de MP3 ou de codecs comprimidos; MP4/MOV são inspecionados apenas pelo cabeçalho de duração do contêiner (mvhd), que pode diferir da duração visual de faixas com edições complexas; todos exigem o mecanismo multimídia e o isolamento de workers ainda pendentes. **Esta versão não homologa POC-002/POC-003.**
- Um WAV já cortado ou inserido em versão anterior **não é redimensionado automaticamente** por uma nova medição. A mesma mídia pode ter durações de clipes editadas manualmente.
