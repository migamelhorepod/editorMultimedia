# v6.1 — Referências a mídias reais na POC-001

## Funcionalidades
- **Arquivo > Importar mídias…** (Ctrl+I): seletor nativo do Windows, seleção múltipla. Não copia mídias.
- Extensões reconhecidas: vídeo .mp4/.mov/.mkv/.avi/.webm; áudio .mp3/.wav/.m4a/.aac/.flac/.ogg; imagem .png/.jpg/.jpeg/.webp/.bmp. A extensão **não comprova compatibilidade de codec**.
- Vídeo novo entra no final da trilha principal; áudio/imagem na primeira trilha secundária livre no instante do cursor. Associação automática segue o motor existente.
- Durações **provisórias**: vídeo/áudio 5 segundos, imagem 4 segundos. Ainda não há leitura da duração original.
- O arquivo .emproj.json formato 2 grava ID, nome, tamanho, tipo e caminho absoluto de cada fonte, sem salvar seu conteúdo. Projetos antigos formato 1 continuam abrindo.
- Mídia ausente ao abrir fica marcada com **Mídia indisponível** no clipe. O usuário não perde sua edição. **Arquivo > Verificar mídias vinculadas** revalida manualmente, sem monitoramento periódico.
- Arquivos duplicados pelo caminho são ignorados. Remover ocorrência da timeline não apaga arquivos do disco nem a referência catalogada.
- Desfazer/Refazer atuam nas ocorrências da timeline; o catálogo importado permanece.
- A flag de encaixe continua iniciando desativada.

## Validação manual
1. Ctrl+I: importar um vídeo, áudio e imagem; conferir nomes e duração provisória.
2. Ctrl+S: salvar, fechar, reabrir; conferir referências e clipes.
3. Mover/renomear fonte no Explorer; usar "Verificar mídias vinculadas"; conferir alerta sem excluir clipe.
4. Importar o mesmo arquivo duas vezes; conferir prevenção de duplicação.
5. Verificar que os arquivos originais não foram alterados, e que os arquivos v6.0 (.emproj.json formato 1) continuam abrindo.

## Limites de segurança e arquitetura
Esta entrega é **uma integração inicial**, não substitui os serviços finais de biblioteca, canonicalização segura, revogação de permissões ou trabalhador isolado. Caminhos absolutos do Windows permanecem no arquivo do projeto; arquivos que mudam de local ficam indisponíveis até um futuro mecanismo de relocalização. Não há reprodução, thumbnails, leitura de codecs ou metadados reais, nem isolamento para arquivos externos. Não homologar POC-003 com esta implementação.
