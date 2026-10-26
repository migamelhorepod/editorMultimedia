# POC-001 — Testes manuais da interface WPF (v6.5)

**Objetivo:** homologar exclusivamente o que precisa ser visto ou operado na janela do Windows. Os testes de regras internas, fonte, duração e histórico também são executados automaticamente por xUnit e Playwright no PR #5.

**Como usar:** para cada linha, marque **Aprovado / Reprovado / Bloqueado** e registre captura apenas em caso de defeito. Use **cópias** de mídias e projetos nos testes de ausência ou corrupção.

## Sessão 1 — Importação, catálogo e cortes

| Caso | O que testar na WPF | Resultado esperado |
|---|---|---|
| **UI-01** | Criar **Novo projeto vazio** e conferir encaixe | Timeline e catálogo vazios; encaixe de 0,5 s desligado |
| **UI-02** | Importar MP4 de 11m44,7s, WAV e MP3 pelo seletor (`Ctrl+I`) | Catálogo e clipes exibem a **duração real** como `00:11:44,7`, não `704,7s` nem 5s fictícios |
| **UI-03** | Tentar importar mídia inválida e áudio que precisa de ffprobe sem o executável acessível | Mensagem compreensível, sem criação de clipe fictício nem perda de dados |
| **UI-04** | Importar um áudio uma vez e reutilizá-lo **em duas trilhas**, procurar/filtrar a mídia e tentar **Remover do catálogo**; depois excluir cada clipe individualmente | Enquanto existir **qualquer clipe** desse arquivo em qualquer trilha, o catálogo mostra **1 ou mais ocorrências** e o botão de remoção fica desabilitado. **Excluir clipe da timeline** é permitido. Depois de excluir o último clipe, a mídia **continua no catálogo**, podendo então ser removida; o arquivo físico nunca é apagado |
| **UI-05** | Importar PNG, aumentar e reduzir a duração de exibição, dividir | Imagem inicia com 4 s e continua livremente redimensionável |
| **UI-06** | Cortar vídeo e áudio pelas duas bordas; depois **alongar novamente** até os trechos disponíveis da origem; tentar ultrapassar a fonte | Encurtar e reestender dentro da duração original funciona; extrapolar o início ou fim da fonte é recusado, sem criar histórico |
| **UI-07** | Clique direito **sobre um clipe de áudio/vídeo** e selecionar **Restaurar duração original do clipe** | A opção aparece no menu contextual, restaura todo o arquivo, zera o início na fonte e pode ser desfeita/refeita |

## Sessão 2 — Comportamento da timeline

| Caso | O que testar na WPF | Resultado esperado |
|---|---|---|
| **UI-08** | Reordenar vídeos com secundárias posicionadas e independentes | Posicionadas acompanham o pai; independentes permanecem no mesmo instante |
| **UI-09** | Encurtar o pai até gerar **vínculo perdido** e depois mover o clipe dependente | O aviso aparece, persiste e desaparece conforme a regra |
| **UI-10** | Colidir duas mídias secundárias; depois ocupar todas as nove secundárias | Realocação automática para trilha livre; quando não há espaço, aviso sem perda de clipes |
| **UI-11** | Régua, cursor, zoom, marcadores e encaixe ativado/desativado | Todos os controles respondem sem alterar clipes indevidamente |
| **UI-12** | Desfazer/refazer após importar, cortar, dividir e mover | Cada gesto é revertido/refeito em uma etapa coerente |

## Sessão 3 — Persistência e segurança dos dados

| Caso | O que testar na WPF | Resultado esperado |
|---|---|---|
| **UI-13** | Salvar, fechar, reabrir e abrir projeto antigo v1–v3 (se disponível) | Clipes, vínculos, duração e posições na fonte preservados |
| **UI-14** | Renomear uma **cópia** de arquivo no Windows, verificar falta e relocalizar | Catálogo indica arquivo ausente e permite restaurar vínculo sem apagar cortes |
| **UI-15** | Cancelar fechamento/Salvar como; tentar abrir projeto JSON inválido | Projeto atual não é substituído nem descartado sem confirmação |

## Regra de aceite

**Bloqueiam a homologação:** duração fictícia, clipes removidos inadvertidamente, edição temporal fora da fonte, corte/restauração inconsistentes, perda de projeto e qualquer falha crítica nos testes automatizados.

**Vocabulário do UI-04:** *mídia* = uma referência no catálogo; *ocorrência* = cada clipe que utiliza essa mídia na timeline. Excluir uma ocorrência **não** exclui a mídia. Remover a mídia do catálogo só é permitido com **zero ocorrências**, mesmo que todas tenham estado em trilhas diferentes.

**Registro de falha sugerido:** `UI-__ · Esperado: ___ · Observado: ___ · Captura: ___`

**PR:** https://github.com/migamelhorepod/editorMultimedia/pull/5. Não fazer merge até a aprovação dos testes manuais.
