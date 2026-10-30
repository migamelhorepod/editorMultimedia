# Política de retenção de branches

Aplicar ao criar novas branches e ao encerrar PRs, sem apagar histórico de commits integrado à `main`.

- Preservar sempre a branch protegida `main`.
- Preservar a branch de desenvolvimento ativa e qualquer branch com PR aberto.
- Preservar também as **duas branches mais recentemente integradas à main**, identificadas pela data de merge do respectivo PR, independentemente de nome.
- Branches mais antigas **já integradas** podem ser eliminadas após confirmar o merge, que não deve ser revertido.
- Nunca remover branches com trabalho não integrado sem nova autorização específica; caso de rebase/squash, verificar a integração por PR, não apenas pela ancestralidade Git.
- Antes de excluir, consultar branches protegidas e PRs abertos; a limpeza deve ser intencional, auditável e não forçar a remoção de ref protegido.

## Referência em 27/10/2026

Retenção: `main`, `feature/poc003-2-revogacao-recuperacao` (ativa),
`feature/poc003-1-nucleo-seguro` (PR #7) e
`test/poc001-playwright-typescript` (PR #6).

Candidatas à limpeza depois de confirmar proteção e merges:
`feature/poc001-fechamento-integrado`, `feature/v6.1-referencias-de-midia`,
`feature/v6.2-relocalizacao-de-midia`, `feature/v6.3-catalogo-midia-projeto` e
`feature/v6.4-metadados-reais-midia`.

Esta política não é um gatilho de exclusão automática. Excluir exige
uma operação GitHub autorizada, com verificação atualizada antes de cada ação.
