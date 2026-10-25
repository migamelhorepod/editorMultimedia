# v4.7 — incremento do motor

Implementado: `Split` em clipes principais/secundários, reassociação de filhos cujo início está na metade direita, exclusão secundária, correção do relatório de IDs alterados em `DeleteMain`, cinco testes adicionais.

Limites conhecidos: divisão não reinterpreta efeitos que dependam de tempo de origem; o token de efeitos é copiado, mas cortes de fonte, keyframes e offsets de mídia exigem modelo próprio. `ResizeSecondary` ainda não move anexos/grupos de clipes vinculados. Não há UI WPF nem execução dos testes neste ambiente.

Validação necessária: `dotnet test` em ambiente com .NET SDK e testes adicionais de propriedades/invariantes, colisões e casos-limite.
