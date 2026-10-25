# POC-003 — roteiro de execução segura (v4.7)

Estado: preparação; **nenhum isolamento foi homologado**.

1. Controlador autentica o worker pelo mecanismo IPC do Windows e valida identidade efetiva do processo.
2. Worker opera sob identidade/token restrito por mecanismo do Windows; verificar acesso efetivo com testes adversariais.
3. Controlador concede acesso por tarefa e arquivo específico, nunca por pasta de biblioteca inteira.
4. Serviço de arquivos central valida caminho final, links/junctions, recurso e operação em cada solicitação.
5. Cancelamento: revogar autorização, encerrar operações pendentes, fechar handles; se não for demonstrável, encerrar worker.
6. Simular processo travado, IPC inválido, reuso de token e acesso a arquivo não concedido.
7. Registrar eventos sem conteúdo sensível, mantendo regras de auditoria já aprovadas.
8. Não permitir manutenção de biblioteca inativa, inclusive em tarefas de recuperação.

**Critério de bloqueio:** se isolamento efetivo ou revogação não forem comprovados, a tarefa afetada não pode prosseguir; nunca aplicar fallback inseguro.

A implementação IPC depende da escolha técnica PEN-TEC-004 e do mecanismo Windows PEN-TEC-005.
