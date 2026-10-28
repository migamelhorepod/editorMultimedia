# ADR — entrada seekable sem cópia integral no AppContainer

**Status: investigação concluída; implementação NÃO homologada e NÃO ativada.**
Branch de trabalho: `feature/poc003-2-revogacao-recuperacao`.

## Objetivo aprovado

O processamento de mídias de até 10 GB deve acontecer dentro de um worker AppContainer, sem precisar duplicar integralmente cada mídia no HD convencional. A solução deve permitir acesso aleatório (seek) necessário para contêineres como MP4, mantendo a segurança e o desempenho como prioridades.

## Descobertas verificadas

- A compilação **FFmpeg/FFprobe 9.0.2-essentials_build-www.gyan.dev** obtida via Chocolatey na execução GitHub Actions #645 **não contém o protocolo `handle:`**. O script de empacotamento corretamente rejeitou a ausência.
- A versão 9.0.2 contém a documentação do protocolo **`fd:`**, com acesso seekable se o descritor corresponder a um arquivo regular, mas é necessário validar a herança/mapeamento de stdin no Windows. Não é correto assumir que o identificador nativo Win32 seja um descritor CRT `fd`.
- O controlador atual possui validação por biblioteca, recurso, tarefa e operação, token AppContainer, Job Object e IPC autenticada. O processamento vigente usa cópias temporárias identificadas e recuperáveis. A execução #629 aprovou 305 testes .NET e 27 testes Playwright antes da investigação do zero-copy.
- Foi tentada uma mudança baseada em identificadores Win32 herdáveis; **não foi homologada**. Uma alteração necessária na passagem da entrada ao codec foi bloqueada pela ferramenta de edição. Os arquivos de execução foram restaurados ao último estado comprovadamente funcional, sem fallback fora do AppContainer.

## Decisão arquitetural proposta

1. Definir uma abstração de autorização de entrada de mídia isolada, independente da UI e da biblioteca.
2. Preferir entrega de um HANDLE de leitura **específico por arquivo**, sem transmitir o caminho original ao worker e sem alterar ACLs da mídia.
3. Converter o HANDLE para entrada seekable real suportada pela versão estável do decodificador; validar com testes do processo no runner Windows antes de ativar.
4. Garantir que fechamento do Job e revogação de tarefa parem o uso de identificadores já concedidos.
5. Rejeitar simbolic links/reparse points, mudanças de identidade de arquivo e entrada não autorizada, sem conceder acesso ao diretório nem permitir fallback irrestrito.

## Bloqueadores para implementação completa

- Escolher e validar um adaptador de entrada seekable para **FFmpeg 9.x estável** (protocolo `fd:` em descritor CRT válido ou build estável com protocolo Windows HANDLE nativo).
- Provar em CI: WAV e MP4 com `moov` no final, zero cópias, nenhum ACL novo no original, Job kill, sem processos órfãos, sem rede e revogação imediata.
- Testes físicos com mídias de vários GB no Windows 11 e HD convencional D: antes da homologação de 10 GB.

**Regra:** não declarar POC-003.2 concluída, não ativar processamento genérico e não permitir fallback fora do AppContainer até existir execução validada.