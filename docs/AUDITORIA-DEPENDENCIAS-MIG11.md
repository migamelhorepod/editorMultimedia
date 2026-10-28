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
