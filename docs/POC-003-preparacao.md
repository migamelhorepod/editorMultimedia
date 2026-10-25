# POC-003 — Preparação paralela (não implementada)

Status: plano técnico; nenhum mecanismo de isolamento foi homologado.

## Fronteiras
- Processo controlador autenticado; worker com identidade verificável no IPC.
- Worker recebe recursos individualmente autorizados; não recebe caminho de biblioteca inteira.
- Serviço central de arquivos aplica autorizações por tarefa, recurso e operação.
- Operações críticas de recuperação usam concessões próprias do coordenador, nunca as da tarefa cancelada.
- Sem fallback inseguro caso o isolamento exigido não esteja disponível.

## Experimentos necessários
1. Verificar isolamento efetivo imposto pelo Windows, não apenas ACLs verificadas em código.
2. Testar handles já abertos após revogação: fechar/invalidar ou terminar worker.
3. Tentar acesso por junction, symlink, caminhos relativos e caminhos canônicos divergentes.
4. Testar mensagens IPC adulteradas, repetidas, expiradas e com recurso trocado.
5. Derrubar worker durante cópia e recuperação; confirmar idempotência.
6. Verificar compatibilidade de codecs com mecanismo de isolamento escolhido.
7. Verificar ausência de privilégios administrativos e manutenção apenas em biblioteca ativa.

## Critério de passagem
Todos os acessos fora da concessão devem falhar por mecanismo efetivo; revogação deve invalidar também acesso residual; falhas não devem expor outras bibliotecas.
