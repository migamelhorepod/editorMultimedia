# Editor Multimídia — v6.5 · Fechamento integrado da POC-001

**Windows · C# · .NET 10 · WPF · VS Code · GitHub.**

Esta é **uma única branch de consolidação**, `feature/poc001-fechamento-integrado`, criada a partir da v6.4 ainda não integrada à `main`. A versão estável na `main` permanece **v6.3** até a validação manual e aprovação do Pull Request.

## Contrato da POC-001
- **Duração original real para todos os vídeos e áudios importados na timeline**. Nada de 5 s fictícios. WAV PCM/float e MP4/MOV compatíveis usam leitores internos; outros formatos (MP3/M4A/AAC/FLAC/OGG/MKV/AVI/WebM etc.) recorrem ao `ffprobe`. Se não for possível aferir a duração, a importação da ocorrência **falha sem alterar a timeline nem o catálogo**.
- **Duração legível**: `HH:MM:SS` com fração de até 3 dígitos, por exemplo `00:11:44,7`, no catálogo, clipes, régua e status. O relógio do motor permanece em ticks de 100 ns.
- **Cortes fiéis à fonte**: o clipe grava `SourceStart` e `Duration`. Cortar pela esquerda desloca o início na fonte, cortar pela direita mantém esse início; dividir mantém dois intervalos contíguos da origem. Nenhuma edição nova ultrapassa os limites de mídia conhecidos.
- **Associações preservadas**: principal contínua, mídias vinculadas ao pai acompanham seus deslocamentos; mídias independentes mantêm o tempo absoluto. As dez trilhas, a realocação automática, os marcadores, o zoom e o encaixe padrão desligado continuam.
- **Persistência**: `*.emproj.json` formato **4**, com `sourceStartTicks` e `sourceRangeVerified`; abre projetos legados v1, v2 e v3. Salvar somente por ação do usuário, com substituição atômica de arquivo e proteção contra fechamento com edições pendentes.
- **Histórico único**: mover, cortar, dividir, restaurar duração original, relocalizar fonte e atualizar metadados participam de Ctrl+Z/Ctrl+Y. Uma operação inválida não muda dados nem cria entrada no histórico.

## Dependência para formatos comprimidos

Para que MP3, M4A, AAC, FLAC, OGG e formatos de vídeo não reconhecidos pelo leitor interno tenham a duração aferida, instale FFmpeg (que inclui `ffprobe`) **uma única vez**, no CMD do Windows:

```cmd
winget install --id Gyan.FFmpeg --exact
```

Reabra o VS Code para atualizar o PATH e verifique com `ffprobe -version`. Também é possível definir a variável de ambiente local `EDITOR_FFPROBE_PATH` para um executável `ffprobe.exe` de confiança instalado em um caminho absoluto. O editor não instala dependências silenciosamente.

## Executar no VS Code

```cmd
dotnet test EditorMultimidia.sln
```

```cmd
dotnet run --project src\Editor.Timeline.Wpf\Editor.Timeline.Wpf.csproj
```

Consulte **[POC-001 — checklist final e critérios de aceite](docs/POC-001-fechamento-integrado.md)** antes de aprovar o PR e integrar à `main`.

**Limite consciente:** a POC-001 trata edição lógica e integridade da timeline. Não há reprodução, decodificação, miniaturas, renderização ou exportação (POC-002), worker restrito e grants Windows (POC-003), nem metas de desempenho da biblioteca HDD (POC-004). Invocar `ffprobe` em processo separado **não constitui isolamento de segurança comprovado**.

---

## Histórico inicial (v6.0.x)

# v6.0.2 — correção de avisos xUnit2031

Foram substituídos os dois usos de `Assert.Single(collection.Where(predicate))` por `Assert.Single(collection, predicate)` em `ProjectStoreTests.cs`. Sem mudanças de comportamento no motor ou na interface.

# Editor Multimídia v6.0.1 — POC-001 · Salvamento e abertura de projetos

**Base:** v5.9.1 validada pelo usuário. Plataforma: **Windows, C#/.NET 10, WPF**.

## Correção da v6.0.1

- Corrigida a ambiguidade `CS0104` na interface WPF entre `System.IO.Path` e `System.Windows.Shapes.Path`, nas 3 referências de nome de arquivo de `MainWindow.xaml.cs`.
- A classe `System.Windows.Shapes.Path` permanece responsável pelos desenhos dos ícones; nenhuma regra de negócio, formato de projeto ou funcionalidade foi alterada.
- Build e testes do .NET **não puderam ser executados neste ambiente**. Compilar a interface no Windows para validar a correção.

## O que entrou na v6.0

1. **Botão do disquete — Salvar (`Ctrl+S`)**: grava a timeline atual em arquivo `*.emproj.json`, escolhido pela caixa de diálogo nativa do Windows na primeira gravação. Não existe salvamento automático.
2. **Arquivo > Salvar como... (`Ctrl+Shift+S`)**: cria uma cópia em outro caminho e torna esse arquivo o destino de salvamentos posteriores.
3. **Arquivo > Abrir projeto... (`Ctrl+O`)**: carrega um projeto previamente salvo; substitui a sessão somente **depois** de desserializar e validar integralmente os dados. Um arquivo corrompido deixa a edição corrente aberta.
4. **Arquivo > Nova demonstração (`Ctrl+N`)**: reinicia com clipes de demonstração, identidade de projeto nova e nome visível na janela. É uma *demonstração*, não o futuro fluxo definitivo de Novo Projeto vazio, porque a importação de mídias reais ainda não está pronta.
5. **Aviso de alterações não salvas**: `*` no título. Fechar, abrir ou criar demonstração enquanto há mudanças oferece **Salvar / Não salvar / Cancelar**. Cancelar o diálogo de salvamento ou erro de escrita impede a substituição da sessão ou fechamento.
6. **Persistência versionada**: grava ID de projeto, nome, versão da timeline, clipes, tipos, IDs de mídia, horários em ticks de 100 ns, trilhas, vínculos, efeitos representados por token, avisos e marcadores. O encaixe (snap) **não é gravado**, iniciando sempre desativado.
7. **Gravação de arquivo único com substituição atômica**: serialização e validação prévias, arquivo temporário na mesma pasta, flush e substituição por `File.Replace` quando o destino já existe; arquivo novo por `File.Move`. Erros não truncam deliberadamente o arquivo válido anterior. Não é ainda o coordenador transacional completo da arquitetura.
8. **23 novos métodos de teste automatizado** para serialização, persistência real, substituição, arquivo danificado, esquema incompatível, vínculos, alertas, marcadores, limite de tamanho, nome inválido e Desfazer voltando ao conteúdo salvo.

## Como testar — CMD (um comando por vez)

Se o terminal estiver em outra unidade, digite `D:` antes do `cd`.

```cmd
cd D:\EditorMultimidia\editor-timeline-v6.0.1
```

```cmd
dotnet build src\Editor.Timeline.Wpf\Editor.Timeline.Wpf.csproj
```

```cmd
dotnet test tests\Editor.Timeline.Tests\Editor.Timeline.Tests.csproj
```

```cmd
dotnet run --project src\Editor.Timeline.Wpf\Editor.Timeline.Wpf.csproj
```

### Checklist manual

1. Mova uma imagem de trilha secundária, crie um marcador com `M` e redimensione um vídeo. Observe o `*` no título.
2. Clique no **disquete** ou `Ctrl+S`; salve como `meu-teste.emproj.json` em uma pasta de sua escolha. O asterisco desaparece.
3. Continue editando, pressione `Ctrl+Z` até recuperar o mesmo conteúdo salvo: o asterisco deve desaparecer sem executar gravação.
4. Edite novamente e pressione `Ctrl+O`; escolha **Cancelar** na pergunta sobre mudanças. Seu projeto deve permanecer aberto, com as edições.
5. Grave, feche o programa, reabra, use **Arquivo > Abrir projeto...**, escolha o JSON salvo e confira clipes, vínculo do áudio e marcadores. O histórico de Desfazer não será restaurado.
6. Faça uma cópia do JSON e danifique seu conteúdo no editor de texto. Ao tentar abrir, deve aparecer um aviso e o projeto atualmente aberto deve permanecer inalterado.
7. Use **Salvar como** e confirme que o novo caminho passa a ser o destino do disquete.

## Limites importantes e próximos módulos

- Esta POC **não armazena nem importa mídia física**; `MediaId` é um identificador de demonstração. Gravar o JSON não incorpora vídeo/áudio/imagem, não renderiza preview e não exporta vídeo.
- **Histórico de Desfazer/Refazer, seleção, cursor, zoom, rolagem e preferência de snap não são persistidos.** Ao abrir um projeto, inicia-se uma sessão nova sem histórico; snap inicia `false`.
- Não foi implementado salvamento automático, recálculo de mídias indisponíveis, gestor de bibliotecas, a identidade persistente da biblioteca nem a centralização de todas as operações em `IFileAccessService`. Isso pertence às etapas futuras e **não deve ser considerado validado** nesta versão.
- O formato JSON `formatVersion: 1` é deliberadamente estrito: propriedades desconhecidas, formatos futuros, mídias/vínculos inválidos e arquivo maior que 16 MB são rejeitados sem carregar parcialmente.
- A operação de substituição é atômica ao nível de **um arquivo** quando suportada pelo sistema de arquivos. Não equivale a durabilidade garantida sob toda falha física, backup automático, journal de transações ou coordenação entre diretórios.
- Não modificamos as regras existentes de arraste, realocação, limite de 10 trilhas, navegação visual com `Ctrl+setas`, marcadores, edição das duas bordas ou associação automática.

## Validação do pacote

Estrutura do projeto, XML/XAML, vínculos de eventos e formato do ZIP revisados no ambiente de autoria. **O SDK .NET/WPF não está instalado neste ambiente**, portanto o build e os **128 casos automatizados estimados** (*105 anteriores + 23 adicionados*) continuam pendentes de execução no computador Windows do usuário.


## Integração Playwright + TypeScript

Após os testes unitários, o GitHub Actions executa a suíte de integração do motor real com Playwright Test e TypeScript. Ela não automatiza a interface nativa WPF. No CMD, na raiz do repositório, execute `tests\run-all.cmd` para a mesma sequência local. Pré-requisitos, relatórios e rastreabilidade dos 29 CTs: [guia de automação](docs/POC-001-automacao-playwright.md).
