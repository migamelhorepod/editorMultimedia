# Editor Multimídia v6.4 — inspeção limitada de metadados

Esta versão avança o catálogo de fontes reais, **sem instalar FFmpeg e sem decodificação de vídeo ou áudio**.

## Comportamentos implementados

1. **WAV PCM ou IEEE float simples** com cabeçalho RIFF/WAVE consistente: identifica a duração no formato de ticks de 100 ns, lendo apenas cabeçalhos e os comprimentos dos blocos. Os clipes **novos** criados dessa mídia utilizam a duração aferida. O código verifica taxa de amostragem, canais, block alignment e integridade básica dos tamanhos.
2. **Imagens PNG** com assinatura e IHDR reconhecidos: coleta dimensões em pixels sem decodificar a imagem. A duração do clipe de imagem continua configurada em **4 s** por padrão.
3. **Outros formatos** (MP3, FLAC, AAC, MP4, MOV, MKV, JPEG etc.), WAV comprimido e cabeçalhos inválidos: preservam o comportamento anterior (**5 s** provisórios para áudio/vídeo, **4 s** para imagens). Não afirma duração ou dimensão que não tenha medido.
4. O painel do projeto exibe **duração WAV aferida**, **dimensões PNG** e disponibilidade dos arquivos. O tooltip de cada clipe distingue a duração do clipe da duração da fonte WAV.
5. **Atualizar dados** no painel: relê os metadados de um arquivo já catalogado — inclusive vindo de um projeto antigo — sem recriar referências e sem mudar os cortes ou durações de clipes existentes. Se os dados mudarem, a atualização integra o histórico de **Desfazer/Refazer**. Arquivos ausentes exigem relocalização prévia.
6. O formato **`.emproj.json` v3** persiste `durationTicks`, `pixelWidth` e `pixelHeight`, todos opcionais. Os projetos v1 e v2 continuam abrindo. Salvar um projeto antigo grava v3.
7. O disquete permanece o único mecanismo de salvamento do projeto. A atualização de dados NÃO altera os arquivos de mídia originais.
8. Validação de esquema rejeita dimensões negativas/fora do limite, metadados inconsistentes e duração não positiva. Os testes usam pequenos WAV/PNG gerados em memória, sem codecs nem ferramentas externas.

## Roteiro de teste manual

- Crie um projeto vazio. Importe um WAV PCM válido de 8 ou 16 bits e confira o tempo real aferido no painel e na timeline (em vez dos 5 s provisórios).
- Importe um PNG e confira largura × altura no painel. A duração da imagem deve continuar 4 s.
- Importe um MP3 e um MP4 e verifique que ambos continuam com 5 s **provisórios**: não há leitura da duração real desses formatos nesta etapa.
- Abra um projeto v6.3 (`formatVersion: 2`) com WAV já importado. Selecione-o no painel e clique em **Atualizar dados**. Os novos metadados devem aparecer sem mexer em cortes já realizados. Faça Ctrl+Z, Ctrl+Y, salve, feche e reabra.
- Renomeie o arquivo de origem e tente atualizar dados: a operação deve ser rejeitada sem mudar o projeto. **Relocalizar** continua disponível.
- Confirme que a interface abre sem avisos e que o GitHub Actions aprova a compilação WPF e os testes.

## Segurança e limites

- Leitura síncrona e limitada de cabeçalhos de arquivos **explicitamente selecionados**. WAV: até 128 chunks e limite de varredura de 4 MiB antes do primeiro bloco útil; carga de dados de áudio é pulada, não decodificada. PNG: apenas 24 bytes de cabeçalho.
- Não há validação de integridade integral de PNG/codec WAV nem prova de equivalência de conteúdo na relocalização. Não interpretar metadados lidos como prova de arquivo reproduzível.
- Não há playback, thumbnails, FPS, inspeção de MP3, MP4 ou formatos comprimidos; todos exigem o mecanismo multimídia e o isolamento de workers ainda pendentes. **Esta versão não homologa POC-002/POC-003.**
- Um WAV já cortado ou inserido em versão anterior **não é redimensionado automaticamente** por uma nova medição. A mesma mídia pode ter durações de clipes editadas manualmente.
