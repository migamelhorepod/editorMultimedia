# POC-CLOUD-001 — Oracle OCI Object Storage vs Cloudflare R2

**Situação:** prova de conceito separada da POC-003.2, sem migração da biblioteca e sem mudança em `MediaWorkerAdmission`. Data: 28/10/2026. Linguagem .NET 10 estável; dependência `AWSSDK.S3 4.0.104.7` (Apache 2.0), fixada no projeto somente da POC.

## Objetivo

Comparar as interfaces **S3 compatíveis** Oracle OCI e Cloudflare R2 para armazenar **somente originais de mídia na nuvem**, mantendo editor WPF, cache e worker AppContainer executando localmente. Esta POC **não** coloca FFmpeg ou o Editor na nuvem, não acessa mídias pessoais e não habilita a produção.

O AWS SDK for .NET foi escolhido por implementar assinatura AWS Signature V4 e S3 interoperável, evitando construir um assinador criptográfico proprietário. Alternativas: SDKs proprietários (acoplamento ao fornecedor) ou implementação manual SigV4 (risco de segurança/manutenção). A POC usa APIs estáveis `PutObject`, `GetObject`, `HeadObject`, `InitiateMultipartUpload`, `UploadPart`, `CompleteMultipartUpload`, `AbortMultipartUpload` e `DeleteObject`.

## Fontes oficiais verificadas

- Oracle OCI S3 Compatibility API: https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/s3compatibleapi.htm — endpoint path-style `https://<namespace>.compat.objectstorage.<region>.oci.customer-oci.com`; região real; chaves de usuário restritas; suporte SigV4.
- OCI Multipart: https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/usingmultipartuploads.htm — upload em partes pode retomar parte falha sem reenviar objeto inteiro.
- Cloudflare R2 S3 Compatibility API: https://developers.cloudflare.com/r2/api/s3/api/ — endpoint `https://<account_id>.r2.cloudflarestorage.com`, região `auto`, Range, GET, PUT e multipart.
- AWSSDK.S3 NuGet: https://www.nuget.org/packages/AWSSDK.S3/4.0.104.7 — versão estável e licença do SDK (verificar licenças transitivas).

## Evidência que os testes podem produzir

| ID | Cenário BDD | Teste | Resultado obrigatório |
|---|---|---|---|
| CLD-01 | **Dado** Oracle com namespace/região válidos, **quando** configurar endpoint, **então** URL HTTPS e região são formadas conforme API oficial | xUnit offline | sem expor segredos |
| CLD-02 | **Dado** R2, **quando** configurar, **então** endpoint HTTPS com região auto | xUnit offline | endpoint correto |
| CLD-03 | **Dado** identificador malicioso/sem credencial, **quando** configurar, **então** falha fechada | xUnit offline | rejeição |
| CLD-04 | **Dado** objeto grande, **quando** solicitar intervalo inválido ou maior que 1 MiB, **então** negar antes da rede | xUnit offline | sem download total |
| CLD-05 | **Dado** resposta HTTP 206, **quando** a faixa for lida, **então** bytes, Content-Range, comprimento e integridade coincidem | xUnit offline | leitura correta |
| CLD-06 | **Dado** resposta HTTP 200 a requisição Range, **quando** ocorrer fallback, **então** negar sem consumir o corpo inteiro | xUnit offline | sem download acidental |
| CLD-07 | **Dado** falha 503/429 transitória, **quando** houver 3 tentativas limitadas, **então** recuperar ou falhar sem laço infinito | xUnit offline | limite de retry |
| CLD-08 | **Dado** bytes corrompidos, resposta truncada, 403 ou cancelamento, **quando** for solicitada a leitura, **então** erro fechado e sem sucesso falso | xUnit offline | isolamento de falha |
| CLD-09 | **Dado** buckets privados reais autorizados, **quando** enviar 64 KiB e ler, **então** bytes/hash conferem, com tempo de PUT/GET | opt-in live | Oracle e R2 aprovados |
| CLD-10 | **Dado** dados sintéticos 6 MiB, **quando** upload multipart de 5+1 MiB concluir, **então** HEAD indica comprimento e 3 faixas de 4 KiB cruzam fronteiras e cauda | opt-in live | leitura parcial real |
| CLD-11 | **Dado** objetos novos com prefixo aleatório exclusivo, **quando** o teste acabar/falhar, **então** apagar somente objetos da POC e abortar multipart pendente | opt-in live | cleanup comprovado |
| CLD-12 | **Dado** contas sem segredo autorizado, **quando** executar teste live, **então** nenhuma operação remota ocorre, status não executado | opt-in live | não inventar medições |

**Limitação dos testes live:** três leituras pontuais de 4 KiB e um upload de 6 MiB são apenas testes de compatibilidade e medições ilustrativas, não um benchmark confiável para vídeos de 10 GB. Dados originais não são baixados nem enviados. Não declarar nuvem vencedora sem rodadas replicadas, condições de rede semelhantes, preços e performance com arquivos grandes.

## Como executar somente pelo CMD

No CMD aberto na raiz do repositório, com .NET 10 instalado:

```cmd
git pull --ff-only origin feature/poc003-2-revogacao-recuperacao
```

```cmd
tests\testar-nuvem.cmd
```

Os relatórios locais xUnit ficam em `TestResults/cloud-poc/cloud-offline.trx`. Esses testes **não fazem requests à Oracle ou à Cloudflare, não precisam de conta e não geram cobranças**.

## Testes reais futuros nos fornecedores

**Não há credenciais ou buckets das duas nuvens conectados/autorizados nesta conversa.** O workflow opcional `.github/workflows/cloud-storage-poc.yml` executa comparação real somente com `workflow_dispatch` e a opção `live_cloud_compare=true`. Ele jamais roda um teste pago automaticamente em PR ou push.

Configuração esperada no GitHub (nunca enviar valores secretos ao chat, código ou logs):

| Provedor | Variáveis GitHub Actions | Segredos GitHub Actions |
|---|---|---|
| OCI | `OCI_NAMESPACE`, `OCI_REGION`, `OCI_BUCKET` | `OCI_ACCESS_KEY_ID`, `OCI_SECRET_ACCESS_KEY` |
| R2 | `R2_ACCOUNT_ID`, `R2_BUCKET` | `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` |

Criar buckets **exclusivos para a POC**, privados e sem dados pessoais, com política de menor privilégio (listar/HEAD/GET/PUT/DELETE/Multipart **somente prefixo** `editor-cloud-poc/` quando o provedor permitir); nunca usar chave de administrador. O script gera subprefixo aleatório, não cria buckets, não lista bibliotecas e limpa seus dois objetos sintéticos. Se falhar limpeza, o relatório marca `CleanupSucceeded=false` e a conta precisa ser verificada antes de repetir.

No GitHub: **Actions → POC de armazenamento — Oracle OCI x Cloudflare R2 → Run workflow → marcar live_cloud_compare → Run workflow**. Isso só será realizado após autorizar explicitamente o acesso e os possíveis custos nas contas.

O resultado é `TestResults/cloud-poc/live-comparison.json`, com tempos, faixas lidas, integridade e limpeza, **sem as chaves de API**. Faltas de segredos geram falha explícita e não são contabilizadas como aprovação.

## Segurança e arquitetura proposta (ainda não aprovada)

- Originais em bucket privado, jamais público; versões/identidade do objeto preservadas por metadados e políticas; cache local somente de trechos sob demanda com quota.
- HTTPS obrigatório; validação de endpoint e SigV4; credenciais nunca enviadas ao worker AppContainer.
- O **controlador** faz GET Range autenticado e entrega bytes ao worker isolado por interface controlada de leitura/seek. Não dar acesso direto à rede ao worker e não trocar bytes de nuvem por pipes sem seek.
- Em perda de conectividade, editor preserva projeto e marca mídia indisponível se trecho necessário não estiver em cache; falta de rede nunca confirma upload/download ou exclui original.
- O módulo `Editor.CloudStorageProbe` é **separado do aplicativo de produção**. A migração do repositório D: precisa de nova aprovação, teste de 10 GB, quotas/cache, falhas/retries, custos e revisão do AppContainer. FFmpeg nativo/IPC/recuperação permanecem gates de segurança.
- A prova de conceito não muda nada em `src/Editor.Timeline.Wpf` nem na POC-003.2.

## Critérios de decisão

Somente considerar provedor após `CLD-01..08` aprovados no CI, seguida de `CLD-09..11` em **ambos** os buckets autorizados, com relatório não ambíguo. Comparar latência medida em repetidas execuções, classes e custos por GB/operação/transferência, disponibilidade na região desejada e segurança. Sem credenciais reais, o veredito técnico é **contratos testados; fornecedores remotos não verificados**.

## Resultado efetivamente executado (29/10/2026 UTC)

- GitHub Actions [cloud POC #7](https://github.com/migamelhorepod/editorMultimedia/actions/runs/40800263637): **SUCCESS**, xUnit Windows com **22 testes Passed, 0 Failed e 0 Skipped** para endpoints, ranges, verificação de integridade, erros HTTP, retries e cancelamento.
- Job de comparação real Oracle/R2 do mesmo pipeline: **SKIPPED deliberadamente** — não há segredos/buckets autorizados, logo **não existe evidência de upload/download nem latência/custo reais nos fornecedores**.
- Pipeline principal [#819](https://github.com/migamelhorepod/editorMultimedia/actions/runs/40800263655): **SUCCESS** para build/test, gate POC-003.1 e gate experimental POC-003.2. Mudanças isoladas não alteraram admissão produtiva do AppContainer.
- A verificação de limpeza do teste live apaga somente as duas chaves sintéticas geradas em prefixo aleatório mesmo se houver resposta ambígua de PUT (commit `fb1ac34`); nunca apaga objetos pré-existentes.
- **Próximo gate:** obter dois buckets privados exclusivos e credenciais temporárias de mínimo privilégio por meio de Secrets do GitHub, configurar variáveis não secretas, aprovar eventual custo e disparar manualmente o job live. Não marcar fornecedor como aprovado com base apenas nos testes offline.
