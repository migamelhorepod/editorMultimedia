# v6.3 — Painel das mídias do projeto

## Funcionalidades
- **Novo projeto vazio** (Ctrl+N), sem clipes de demonstração. A demonstração fica acessível em **Arquivo > Carregar demonstração**.
- Painel lateral **Mídias do projeto**, exibindo nome, categoria, número de ocorrências, tamanho e status de disponibilidade da última verificação.
- Busca pelo nome ou pelo caminho completo e filtros **Todos / Vídeos / Áudios / Imagens / Indisponíveis**.
- **Usar no cursor** (ou clique duplo) adiciona outra ocorrência da mídia selecionada sem duplicar sua referência no catálogo:
  - Vídeos são anexados ao final da trilha principal;
  - Áudios e imagens entram no instante do cursor, na primeira trilha secundária disponível.
  - Durações continuam **provisórias**: 5 s para vídeo/áudio, 4 s para imagem.
- **Localizar clipe** escolhe a primeira ocorrência e movimenta a visualização até ela.
- **Relocalizar…** também disponível pelo painel, inclusive para fontes sem clipes.
- **Remover mídia não utilizada** remove a referência do projeto somente se não houver clipes que a utilizem. Não exclui o arquivo original. Participa de Ctrl+Z/Ctrl+Y.
- Ações de catálogo não salvam automaticamente: só o disquete salva.
- Mantém referências anteriores, avisos sobre mídias indisponíveis, associações e o limite de dez trilhas.

## Como testar
1. Abrir a aplicação e confirmar timeline e catálogo vazios.
2. Arquivo > Carregar demonstração → confirmar clipes de exemplo.
3. Ctrl+N → confirmar novo projeto vazio.
4. Ctrl+I → importar um vídeo e um áudio.
5. Selecionar o áudio no catálogo e usar **Usar no cursor** em posições diferentes; os clipes compartilham a fonte, mas têm identidades próprias.
6. Buscar por nome, filtrar categoria e selecionar **Localizar clipe**.
7. Tentar remover uma fonte em uso → botão desabilitado; remover as ocorrências, depois remover a referência; Desfazer/Refazer.
8. Salvar e reabrir; conferir catálogo e ocorrências.

## Limites e próximos incrementos
Ainda é um **catálogo do projeto**, não a biblioteca persistente de múltiplas pastas aprovada para o produto final. Também não há miniaturas, duração real, decodificação, reprodução, classificação por hash nem migração física de mídias. Arquivos originais não são modificados. A arquitetura de isolamento de processos e concessões de acesso da POC-003 continua pendente.
