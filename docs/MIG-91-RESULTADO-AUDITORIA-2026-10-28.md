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