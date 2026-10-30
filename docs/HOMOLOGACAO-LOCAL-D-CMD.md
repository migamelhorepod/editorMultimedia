# POC-003.2 — Homologação física no D: somente com CMD

## Objetivo e limites

Executar **no próprio Windows 11** a bateria de verificação sintética do worker AppContainer, com os arquivos temporários localizados no volume **D:**. Não equivale a um teste com mídia de 10 GiB, à aprovação da segurança nativa do FFmpeg ou à homologação geral de produção.

Este procedimento **não exige executar PowerShell**. A entrada é `tests\homologar-d.cmd`; a ferramenta que coleta o relatório foi criada em C#/.NET 10 e será compilada automaticamente. Ela não muda `MediaWorkerAdmission.IsMediaProcessingAvailable=false` e não altera os arquivos das bibliotecas pessoais.

## Pré-requisitos

- Windows 11 x64 no computador físico, repositório da branch `feature/poc003-2-revogacao-recuperacao` atualizado no Visual Studio.
- SDK .NET 10 instalado e disponível no Prompt de Comando.
- Unidade D: local, fixa, acessível e com pelo menos 1,2 GiB livres para o teste padrão; confirme separadamente em **Desfragmentar e Otimizar Unidades** que D: é um **HD mecânico** (a API `DriveInfo.Fixed` não consegue distinguir HD de SSD).
- FFmpeg e FFprobe **estáveis 9.x**, com executáveis reais na mesma pasta. A ferramenta procura o pacote Chocolatey e diretórios do PATH; se necessário, aceita a variável CMD `EDITOR_FFMPEG_DIR` apontando à pasta real dos dois programas. Não usa shims do Chocolatey.
- Não abrir o editor sobre arquivos usados nos testes. Não elevar permissões ou desabilitar proteções do Windows para forçar testes a passar.
- Os codecs FFmpeg 9.0.2 possuem riscos nativos conhecidos: **somente mídias sintéticas do script**; não processar mídias de terceiros nem liberar produção.

## Como executar no Prompt de Comando (CMD)

1. No Explorador de Arquivos, abra a pasta local do repositório (que contém `EditorMultimidia.sln`). Digite `cmd` na barra de endereço e pressione Enter. Isso abre o Prompt **na pasta certa**, sem precisar adivinhar o caminho.
2. Confirme o Git e atualize a branch sem fazer merge implícito. Execute os três comandos **separadamente**:

```cmd
git fetch origin
```

```cmd
git switch feature/poc003-2-revogacao-recuperacao
```

```cmd
git pull --ff-only origin feature/poc003-2-revogacao-recuperacao
```

3. Verifique o SDK:

```cmd
dotnet --version
```

4. Inicie o teste físico padrão (64 MiB):

```cmd
tests\homologar-d.cmd
```

5. Ao finalizar, abra os resultados:

```cmd
explorer TestResults\physical-windows11-hdd
```

Cada execução cria uma subpasta única com `physical-hdd-evidence.json`, `physical.trx` e `physical-test-output.log`. Uma segunda pasta única de teste é criada no D: com prefixo `editor-poc0032-physical-` e **não é excluída automaticamente**; nunca limpe diretórios da biblioteca pessoal ou qualquer pasta que não tenha sido identificada no relatório.

### Quando FFmpeg não for encontrado

Aponte a pasta **real** que contém `ffmpeg.exe` e `ffprobe.exe` no CMD (substitua o caminho de exemplo pelo caminho verdadeiro; não use o diretório de atalhos do Chocolatey):

```cmd
set "EDITOR_FFMPEG_DIR=C:\CAMINHO\DA\PASTA\DOS\CODECS"
```

Depois execute `tests\homologar-d.cmd` novamente. Se persistir, anexe o texto do erro; não troque por um FFmpeg beta para tentar passar.

## O que será medido

- Windows 11 real e volume D: fixo com espaço livre; tipo mecânico ainda exige confirmação visual;
- preparação e permissões mínimas do worker AppContainer, FFmpeg e FFprobe reais;
- geração física de WAV de 64 MiB, seu tamanho e SHA-256 antes/depois;
- testes BDD para metadados WAV, MP4 com índice no fim, decodificação, revogação, tentativa de troca do arquivo, ACLs, worker empacotado e encerramento forçado;
- registros do xUnit em TRX, duração, pico de memória do processo test runner, espaço livre antes/depois, arquivos temporários restantes e falhas.

**Mesmo quando os testes passarem**, o JSON registra `readyForProduction=false` e `readyForDistribution=false`. A condição `testsPassed=true` comprova somente os cenários sintéticos executados no volume físico. A meta de até 10 GiB e a segurança de toda a distribuição FFmpeg ainda não foram homologadas.

### Cenários BDD

- **Dado** o Windows 11 e o HD D: disponíveis, **quando** o comando CMD de homologação é executado, **então** gera WAV isolado, executa os testes reais no D: e guarda provas de integridade sem tocar nas mídias existentes.
- **Dado** erro no worker, codec, permissão, teste, hash ou evidência, **quando** o processo termina, **então** retorna erro, registra o ocorrido e mantém a produção bloqueada.
- **Dado** testes sintéticos aprovados em D:, **quando** o resultado é interpretado, **então** o estado de liberação permanece falso até tratar as CVEs nativas, licenças e teste de arquivos grandes.

**Próximo passo:** anexar `physical-hdd-evidence.json` e, se falhar, `physical-test-output.log` à MIG-12/POC-003.2 para confronto com as evidências do CI. Nunca marcar homologação física como realizada sem esse resultado local.
