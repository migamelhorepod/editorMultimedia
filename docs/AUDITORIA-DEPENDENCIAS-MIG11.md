# MIG-11 — Auditoria de versões e política de dependências

Data: 28/10/2026. Fonte: manifestos e workflow da branch `feature/poc003-2-revogacao-recuperacao`. Esta revisão é de **inventário e política**; não substitui varredura CVE/SBOM da MIG-91.

| Componente | Versão declarada | Uso / justificativa | Risco ou ação |
|---|---|---|---|
| .NET / WPF | net10.0 | Desktop nativo Windows, execução estável | .NET 10 LTS; manter suporte e atualizar patches sem mudar major automaticamente |
| Node.js | 24 | Executar testes Playwright TypeScript em CI | LTS; versão major não fixa patch; validar `node --version` |
| @playwright/test | 1.64.1 | Testes de integração após xUnit | `npm ci` com lockfile; não confundir com teste visual WPF |
| TypeScript | 7.0.2 | Tipagem dos testes | Verificar status estável, compatibilidade com Playwright e `@types/node` em atualização futura; não migrar sem teste |
| @types/node | 24.10.1 | Tipos Node para TS | Conferir consistência com runtime Node 24 |
| Microsoft.NET.Test.Sdk | 17.11.1 | Runner de testes .NET | Versão explícita, verificar CVE/suporte e atualizar com CI |
| xunit | 2.9.2 | Testes unitários BDD | Versão explícita, verificar CVE e suporte |
| xunit.runner.visualstudio | 2.8.2 | Descoberta de testes Visual Studio | Versão explícita, verificar CVE e suporte |
| FFmpeg / ffprobe | Chocolatey sem versão fixa; pacote observado 9.0.2 | Decodificação no AppContainer | **Bloqueador de reprodutibilidade:** CI não fixa versão nem SHA-256; `handle:` indisponível no pacote testado; não trocar binário silenciosamente |
| GitHub Actions | checkout@v5, setup-dotnet@v5, setup-node@v5, upload-artifact@v6 | Build/teste Windows | Tags major mutáveis: avaliar pin por SHA imutável e política de atualização |

## Política aprovada para mudanças

1. Usar somente versões estáveis suportadas; proibir preview, beta e RC em produção.
2. Documentar para cada dependência: função, motivo, alternativa sem ela, licença, versão, suporte, CVEs e origem do binário.
3. Preservar `package-lock.json` e `npm ci`; manter versões .NET explícitas e registrar mudanças em PR com testes.
4. FFmpeg deve ter versão, distribuição, licença e hash verificáveis **antes** de liberar processamento. Não supor que a versão encontrada no runner seja idêntica ao ambiente final.
5. Executar `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev` e `npm audit` em ambiente autorizado, registrar resultados e triagem; falhas não podem ser omitidas. A consulta de CVE e SBOM completa permanece MIG-91.
6. Produzir SBOM e checar licenças/distribuição antes do release, especialmente codecs FFmpeg; distinguir GPL/LGPL conforme configuração real do binário.
7. Nunca atualizar uma major automaticamente para resolver alerta: avaliar breaking changes, desempenho, segurança e Windows 11/HD convencional.

## Pendências explícitas

- Não há evidência nesta auditoria de verificação de CVEs/licenças de todas as dependências; **MIG-91 permanece aberta**.
- Não há hash pinado para FFmpeg e GitHub Actions; reprodutibilidade do pipeline permanece parcial.
- Não há benchmark físico de I/O/CPU/RAM na máquina de referência; não inferir a partir de GitHub Actions.

**Aceite BDD:** Dado o inventário de dependências, quando um componente é atualizado, então a mudança exige versão suportada, licença, segurança, origem verificável, testes e justificativa documentados.

## Revisão de fontes oficiais e CVEs nativas em 28/10/2026

- .NET 10 LTS: política oficial da Microsoft https://dotnet.microsoft.com/en-us/platform/support/policy; compatível com o alvo Windows 11 e `net10.0-windows` do WPF.
- Node.js 24 LTS: tabela de versões oficiais https://nodejs.org/en/about/previous-releases; é apenas a runtime de testes, não do editor distribuído.
- FFmpeg 9.0.2: última versão estável 9.0 publicada em setembro de 2026 na página oficial https://ffmpeg.org/download.html; o pacote do runner identifica `ffmpeg version 9.0.2-essentials_build-www.gyan.dev`.
- **CVE-2026-107678:** NVD declara FFmpeg até 9.0.2 vulnerável à entrada MP4 manipulada que pode provocar pilha/CPU exauridas. https://nvd.nist.gov/vuln/detail/CVE-2026-107678
- **CVE-2026-107696:** NVD declara FFmpeg até 9.0.2 vulnerável a loop RTSP; AppContainer sem rede reduz superfície, mas não corrige a vulnerabilidade. https://nvd.nist.gov/vuln/detail/CVE-2026-107696
- O build real contém `--enable-gpl --enable-version3 --enable-static`; exige análise GPLv3 antes de qualquer distribuição. Inventário/licenças/hashes e evidências do CI #712 em `docs/MIG-91-RESULTADO-AUDITORIA-2026-10-28.md`.

### Plano de atualização verificável

1. **Manter a produção bloqueada** (`MediaWorkerAdmission=false`) enquanto FFmpeg 9.0.2 apresentar CVEs aplicáveis ao processamento de MP4 e a redistribuição GPL não estiver revisada.
2. Selecionar compilação estável com patches comprovados para os CVEs relevantes, origem oficial/verificável, hash SHA-256 comparável e licença compatível com a distribuição planejada; não supor que trocar apenas o número da versão resolve.
3. Atualizar binários no bundle AppContainer mantendo o protocolo `fd:` seekable, e executar CI completo (WAV/MP4, decode, PID/IPC, rede, integridade e recuperação).
4. Validar em Windows 11 no HD D: antes de liberar produção. Atualizações de .NET/Node/Playwright/TypeScript devem passar em testes de compatibilidade e revisão de releases oficiais.

**Resultado da MIG-11:** inventário e política revisados; riscos de CVEs/licença nativos identificados e direcionados à MIG-91 (remediação). Conclusão da auditoria não é homologação de distribuição.