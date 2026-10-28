# MIG-91 — Evidência efetiva de auditoria de dependências e licenças

Data: 28/10/2026. Execução GitHub Actions #712: https://github.com/migamelhorepod/editorMultimedia/actions/runs/40743054386

## Resultados comprovados no Windows runner

- Consulta NuGet de dependências transitivas e npm audit concluídas: **0 avisos retornados**, inclusive **0 High/Critical**.
- Licenças npm UNKNOWN no inventário do package-lock.json: **0**.
- FFmpeg/FFprobe: **9.0.2-essentials_build-www.gyan.dev** via Chocolatey.
- SHA-256 ffmpeg.exe observado na execução #712: `3256173F3F8BFFD7DF12227C68ADF68025EDB1832273A9530688A7BB1ED8EDEC`. Trata-se de evidência do binário desta execução, não um pin para futuras aquisições.
- FFmpeg real reportou `--enable-gpl --enable-version3 --enable-static`, inclusive libx264 e libx265. **Não tratar esta distribuição como LGPL**; revisão de GPLv3 e bibliotecas nativas é obrigatória antes de distribuir.
- Artefatos: `TestResults/security-audit/dependency-audit.json`, `nuget-vulnerabilities.json`, `npm-vulnerabilities.json`, `sbom.cdx.json`.

## Bloqueadores restantes

1. A prova de ausência de vulnerabilidades consultadas no ecossistema .NET/npm não equivale à análise de CVEs do próprio FFmpeg e bibliotecas nativas.
2. Antes de distribuir esse binário, validar obrigações da GPLv3, licenças incorporadas, avisos, código-fonte correspondente e estratégia jurídica; alternativa é escolher outro build com características/licença verificadas.
3. Fixar versão e origem verificável de FFmpeg, registrar hashes de proveniência e impedir atualização silenciosa do binário no pipeline.
4. Licenças NuGet extraídas dos nuspec e npm do lockfile exigem revisão humana para conflitos; SBOM é inventário preliminar, não parecer de compliance.
5. A homologação do AppContainer não autoriza por si só redistribuição de codecs.

## Cenários BDD

- **Dado** o inventário transitivo .NET/npm, **quando** o runner consulta os advisories, **então** registra o resultado e bloqueia falhas de consulta ou vulnerabilidades high/critical.
- **Dado** o FFmpeg compilado com GPL e version3, **quando** o produto for preparado para distribuição, **então** a licença é revisada com obrigações comprovadas, sem rotular indevidamente como LGPL.
- **Dado** zero avisos nas fontes NuGet/npm na execução #712, **quando** a auditoria é interpretada, **então** não se afirma ausência absoluta de CVEs ou conformidade legal.

**Estado:** auditoria efetivamente executada, **sem advisories NuGet/npm identificados**; distribuição continua bloqueada por revisão GPL e CVEs nativos pendentes. Não marcar MIG-91 como Done.
## Atualização — triagem efetiva de CVEs nativas FFmpeg (28/10/2026)

A consulta aos registros públicos de segurança identificou **riscos conhecidos aplicáveis ao FFmpeg 9.0.2**; portanto o resultado de '0 advisories' vale somente para as consultas NuGet/npm, **não para a biblioteca nativa FFmpeg**.

- **CVE-2026-107678:** NVD descreve vulnerabilidade até FFmpeg 9.0.2 em `av_encryption_init_info_free` e `mov_read_pssh`, permitindo MP4 especialmente preparado provocar exaustão de pilha e consumo quadrático de CPU. É relevante diretamente ao editor de vídeo que processa MP4. Fonte: https://nvd.nist.gov/vuln/detail/CVE-2026-107678
- **CVE-2026-107696:** NVD descreve loop de redirecionamento RTSP até FFmpeg 9.0.2; a política de worker sem rede reduz essa superfície dentro do AppContainer, mas não remove o bug. Fonte: https://nvd.nist.gov/vuln/detail/CVE-2026-107696
- O próprio projeto FFmpeg mantém o catálogo de correções: https://ffmpeg.org/security.html. A distribuição oficial lista a versão estável 9.0.2 em 28/10/2026: https://ffmpeg.org/download.html.

**Gate fail-closed:** não liberar o decoder genérico em produção, não declarar segurança homologada e não publicar builds para usuários até obter versão/correção verificável (origem, hash e prova de resolução das CVEs) e repetir os testes de segurança e desempenho. A limitação de Job Object é mitigação de impacto, **não correção da falha nativa**. Não presumir que simplesmente atualizar o pacote Chocolatey resolverá o problema; verificar build real e patches presentes.

**Rastreabilidade:** MIG-91 (cadeia de dependências e CVEs), MIG-11 (versões), MIG-12 (isolamento) e POC-003.2 (homologação). Estado MIG-91: **não concluída devido às vulnerabilidades nativas conhecidas, licença GPL e origem/reprodutibilidade**.