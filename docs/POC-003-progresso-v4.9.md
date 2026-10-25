# POC-003 — preparação paralela v4.9

Sem implementação de isolamento até esta versão. Próxima entrega deverá incluir controlador e worker executáveis e comprovação de acesso restrito imposta pelo Windows, sem confiar apenas em verificações do aplicativo. Prioridades: IPC autenticado, acesso por arquivo e operação, revogação efetiva de handles, canonicalização contra junction/symlink, bloqueio sem fallback se isolamento falhar, logs protegidos. Manutenção somente na biblioteca ativa, conforme RN-SEG-011.
