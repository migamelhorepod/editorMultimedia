# POC-003 — preparação paralela v4.8

## Objetivo
Validar isolamento **efetivo** de um worker Windows, sem serviço privilegiado obrigatório.

## Contrato IPC preliminar (não aprovado nem implementado)
- `protocolVersion`, `requestId`, `taskId`, `libraryId`, `resourceId`, `operation`, `grantId`, `deadline`.
- O processo controlador autentica o par e valida permissões no receptor; nenhum campo fornecido pelo worker prova autorização.
- Operações: `OpenGrantedResource`, `ReadGrantedChunk`, `CancelTask`, `Heartbeat`, `ReportFailure`.
- Erros: `Unauthorized`, `Expired`, `Revoked`, `InvalidResource`, `InvalidProtocol`, `WorkerTerminated`.

## Experimentos obrigatórios
1. Worker com token restrito tenta abrir arquivo fora da concessão; o próprio Windows deve negar.
2. Tentar acesso a pasta-pai, junction e symlink para fora da biblioteca.
3. Revogar acesso com handle aberto; comprovar encerramento do handle ou término do worker.
4. Reiniciar worker e tentar reutilizar `grantId` antigo; acesso negado.
5. Interromper worker no meio de operação; coordenador recupera sem comprometer outros recursos.
6. Rodar sem privilégios administrativos e documentar mecanismo escolhido (AppContainer somente se compatível).

**Estado:** plano de execução. Nenhum worker isolado nem canal IPC foi implementado nesta entrega.
