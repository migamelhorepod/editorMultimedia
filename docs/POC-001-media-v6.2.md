# v6.2 — Relocalização manual de mídias vinculadas

## Escopo implementado
- **Arquivo > Relocalizar mídia selecionada…** e menu de clique direito.
- Somente clipes importados têm referência física para relocalizar; exemplos simulados continuam sem fonte.
- O usuário seleciona explicitamente um novo arquivo pelo seletor nativo do Windows.
- A nova fonte precisa existir, não estar vazia, ter extensão reconhecida e ser do **mesmo tipo** de mídia. Caminhos duplicados no catálogo são rejeitados.
- Mantém o ID da mídia e, portanto, todas as ocorrências na timeline, posições, cortes, efeitos e associações.
- Verificação de disponibilidade manual; os avisos de mídia ausente somem ao relocalizar para uma fonte válida e podem retornar ao **Desfazer**.
- **Histórico unificado:** importação, cortes e relocalização compartilham a ordem de Desfazer/Refazer. Um único comando de relocalização gera apenas uma entrada no histórico.
- **Salvar continua explícito** (disquete/Ctrl+S); a mudança do caminho aparece como edição pendente.
- Projetos v1 e v2 continuam suportados, sem alteração de formato JSON.

## Roteiro de validação
1. Abra um projeto com arquivo real importado.
2. Renomeie o arquivo original no Explorador para simular indisponibilidade.
3. Menu Arquivo > Verificar mídias vinculadas → aviso sobre o clipe.
4. Selecione o clipe e use Arquivo > Relocalizar mídia selecionada… → aponte para o novo caminho.
5. Confirme que o aviso some sem mover o clipe nem alterar seus efeitos.
6. Ctrl+Z: o caminho antigo volta; Ctrl+Y: a nova referência volta.
7. Ctrl+S, feche e reabra o projeto; confirme nova referência.
8. Escolha tipo incompatível ou mídia já vinculada; a operação deve ser recusada, preservando o projeto.

## Limites
O protótipo ainda não verifica codecs, conteúdos reais nem hashes: valida **extensão e presença do arquivo**, não garante que ele represente o mesmo conteúdo. A relocalização não faz substituição de vídeo por áudio nem converte efeitos. O serviço de arquivos centralizado, isolamento por processo e grants seguros permanecem pendentes da POC-003. Não existe monitoramento automático ou periódico de fontes. Os originais nunca são copiados, excluídos ou alterados por essa funcionalidade.
