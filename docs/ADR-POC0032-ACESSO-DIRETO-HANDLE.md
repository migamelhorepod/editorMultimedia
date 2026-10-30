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
## Decisão de implementação refinada — MIG-7 (28/10/2026)

**Escolha condicional:** adotar um adaptador de entrada *read-only* com seek aleatório, cuja autorização nasce de um HANDLE Win32 aberto pelo processo controlador para **um único arquivo regular**. O HANDLE deve ser transferido ao worker identificado por PID, jamais enviado a um processo arbitrário. No worker, um adaptador de codec deve mapear o recurso concedido a um descritor ou AVIOContext que suporte `read` e `seek`. O FFprobe/FFmpeg continua dentro da identidade AppContainer, sem autorização de caminho original nem expansão das ACLs da biblioteca.

### Contrato e interfaces propostas

- **Controlador/serviço de arquivos:** valida biblioteca ativa, caminho canônico, volume e identidade física do arquivo, rejeita pontos de reanálise; abre HANDLE de leitura somente com compartilhamento controlado, vinculado à tarefa, ao PID e à operação.
- **Canal de concessão:** entrega exclusivamente capacidade de leitura daquele arquivo, com tempo de vida associado ao Job Object. Nunca transmite um caminho que o worker possa reabrir e nunca torna o HANDLE globalmente herdável.
- **Adaptador no worker:** consome apenas a capacidade concedida e faz operações de leitura/seek com limites de posição e overflow, retornando erros fechados. Para `fd:`, exige prova de conversão real do HANDLE para um descritor CRT **válido naquele processo**; alternativamente, usar AVIO customizado no mesmo sandbox, com código e dependências auditados.
- **Revogação:** revogar nova autorização impede novos leitores; para revogar uma leitura já em curso, encerrar o Job/processo e fechar as referências de HANDLE. Não prometer revogação instantânea apenas ao remover um registro de permissão.
- **Proibições:** cópia integral proporcional a 10 GB, fallback externo ao AppContainer, ampliar ACL do original, abrir diretório inteiro, rede do worker, shell/comando arbitrário e declarar sucesso apenas pelo código de saída do processo.

### Alternativas descartadas ou não comprovadas

| Alternativa | Resultado | Motivo |
|---|---|---|
| `handle:` no FFmpeg 9.0.2 do runner | **Descartada no build testado** | Protocolo indisponível, falha fechada comprovada no empacotamento |
| Encaminhar HANDLE Win32 bruto como `fd:` | **Proibida sem adaptador** | HANDLE do kernel não equivale a `fd` da CRT |
| Alimentar `pipe:0` sem seek | **Insuficiente** | MP4 com índice `moov` no fim depende de leitura aleatória |
| Copiar mídia inteira para staging | **Temporária, não satisfaz a POC** | Consome espaço e I/O proporcional a mídias de 10 GB |
| AVIO customizado em worker isolado | **Alternativa admissível** | Exige implementação C/native auditada, integração a FFprobe/FFmpeg e testes reais |

### Gate BDD obrigatório (SEC-01 / MIG-12)

- **Dado** um WAV/MP4 autorizado, inclusive MP4 com `moov` no final, **quando** FFprobe/FFmpeg executam no AppContainer, **então** leitura e busca aleatória funcionam sem copiar a mídia integral, sem abrir o original por caminho e com prova de identidade do token.
- **Dado** um arquivo fora da biblioteca ativa, reparse point ou HANDLE alterado, **quando** o controlador tenta conceder acesso, **então** a operação é negada sem tocar nas ACLs do arquivo.
- **Dado** um processamento em andamento, **quando** ocorre revogação/encerramento, **então** o Job termina, seus descritores são fechados e não permanece subprocesso órfão.
- **Dado** arquivo até 10 GB em Windows 11 com HD convencional, **quando** metadados/decodificação são realizados, **então** medimos I/O, seek, CPU, RAM e demonstramos ausência de cópia proporcional ao tamanho da entrada.

**Limite do aceite MIG-7:** a decisão técnica e o contrato acima ficam documentados. A viabilidade efetiva de `fd:`/AVIO e a homologação da execução pertencem à MIG-12 e sucessoras; esta ADR não prova nem autoriza ativação do processamento genérico.

## Análise de implementação — MIG-12 (28/10/2026)

Inspeção do código existente: `WindowsAppContainerLauncher.RunAuthorizedMetadataProcessing`, `ReadAuthorizedMetadataTicks`, `RunAuthorizedDecodeProcessing` e `RunAuthorizedInterruptedDecodeProbe` ainda criam `WindowsScopedCodecInput`, isto é, cópia integral temporária. O worker `Program.cs` inicia FFprobe/FFmpeg com `-i <caminho da cópia>`. Assim, **o teste real atual prova isolamento de uma cópia, não prova leitura direta do arquivo original sem cópia**. Nenhum teste desta branch autoriza marcar MIG-12 como concluída.

A primeira implementação segura precisa entregar uma capacidade de arquivo ao worker sem expor caminho de origem. O FFmpeg executado como subprocesso **não** recebe automaticamente o HANDLE pertencente ao processo .NET: é necessário duplicar para o PID do subprocesso, criar descritor CRT válido nesse processo ou integrar AVIO nativo, e comprovar seek. Passar o valor numérico do HANDLE via argumento `fd:` não é correto e pode gerar falha de isolamento.

Critérios adicionais de homologação: criar fixture MP4 com `moov` ao final; comparar duração e seek; medir bytes efetivamente copiados e provar zero cópia integral; negar arquivo fora da biblioteca e reparse point; fechar Job em interrupção; não adicionar ACL ao original; comprovar ausência de fallback externo ao sandbox. Enquanto não houver adaptador validado, manter `MediaWorkerAdmission.IsMediaProcessingAvailable=false`.
