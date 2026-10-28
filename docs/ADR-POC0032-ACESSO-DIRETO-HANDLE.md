# ADR — acesso direto e seguro a mídias pelo AppContainer

Status: implementado na branch de POC, sujeito à homologação da execução real no Windows 11.

## Decisão de arquitetura

O processo principal (controlador) é o único responsável por resolver caminhos e abrir mídias autorizadas no disco D:. Para **InspectMetadata** e **ReadChunk**, ele abre o arquivo com `CreateFileW` em modo de leitura, rejeita o arquivo como ponto de redirecionamento e valida o recurso, a tarefa e a biblioteca. Usa `DuplicateHandle` para preparar apenas o identificador permitido, que é herdado pelo worker AppContainer através de `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` na criação suspensa do processo.

O processo é anexado a um Job Object com limites antes de ser retomado. O worker verifica seu token AppContainer e o identificador herdado. O FFprobe ou FFmpeg recebe esse identificador como entrada via protocolo `handle:`, que admite busca aleatória no arquivo. O caminho físico e a ACL original não são repassados ao worker, e **não existe fallback fora do AppContainer**. O Job Object e o fechamento dos identificadores revogam o acesso efetivo ao encerrar a tarefa.

## Motivação

A cópia temporária prévia de cada mídia obrigava a reservar até 10 GB adicionais, duplicava leitura/gravação no HD convencional e atrasava o início de todas as operações. O novo mecanismo mantém o arquivo no lugar e fornece acesso de leitura limitado por identificador de kernel, sem cópia integral e com busca aleatória.

## Regras inegociáveis

- Somente arquivo da biblioteca ativa e operação autorizada; nenhum diretório herdado.
- Identificador herdado com acesso de **leitura**, nunca escrita; abrir sem seguir o link do próprio arquivo.
- Sem permissões amplas para AppContainer e sem alterar ACLs da mídia original.
- Não usar `pipe:0` como substituto do acesso aleatório para formatos como MP4.
- Somente FFmpeg/FFprobe 9.x estáveis com suporte ao protocolo Windows `handle:`; empacotamento deve falhar se ausente.
- Janela de execução protegida por token, PID, Job Object, tempo limitado e validações de autorização. Decodificação e extração de metadados somente no worker.
- Manter a recuperação dos antigos estágios `.editorworker-*.lease` para instalações já utilizadas; o fluxo novo não os cria.

## Provas necessárias

1. WAV e MP4 sintéticos com metadados reais e decodificação dentro do AppContainer, **sem** conceder ACL de mídia original à identidade do AppContainer.
2. Comprovar ausência de `.editorworker-*` novo no fluxo direto; hash do arquivo original preservado.
3. Testar encerramento forçado e bloqueio de rede; comprovar ausência de processos órfãos e revogação pelo fechamento do Job.
4. Medir memória e disco com vídeos grandes no Windows 11 + HD convencional. O limite de arquivo de 10 GB permanece como requisito e não foi validado apenas com mídias pequenas.
5. Não desbloquear a admissão genérica de produção antes dos testes de segurança e integração com o editor WPF.

## Escopo da POC

A migração para acesso por HANDLE reduz a futura necessidade de refazer o pipeline de leitura e é a base arquitetural definitiva proposta. Isso **não** equivale à homologação completa de 10 GB, exportação, thumbnails ou integração produtiva geral.