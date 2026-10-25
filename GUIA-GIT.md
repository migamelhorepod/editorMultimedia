# Nosso editor no Git + Visual Studio

**Base:** Editor Multimídia v6.0.2 (código funcionalmente inalterado). Esta é uma embalagem para desenvolvimento e versionamento, não uma versão nova de funcionalidades.

## Preparação

- Recomenda-se **Visual Studio 2026 (18.0+)** com carga de trabalho **Desenvolvimento para desktop com .NET**. O projeto usa `net10.0` e `net10.0-windows`; o Visual Studio 2022 não suporta nativamente esses frameworks.
- Git para Windows. É possível iniciar Git pela própria interface do Visual Studio.
- Conta do GitHub se quiser backup/sincronização na nuvem. Recomenda-se **repositório privado** enquanto o código é protótipo.

## Como começar com a IDE Visual Studio (não VS Code)

1. Extraia este pacote em `D:\EditorMultimidia\EditorMultimidia` (sem criar pastas por versão).
2. Abra `EditorMultimidia.sln` no **Visual Studio 2026**: Arquivo > Abrir > Projeto/Solução.
3. No Gerenciador de Soluções, clique com o botão direito em `Editor.Timeline.Wpf` > Definir como Projeto de Inicialização.
4. Pressione **F5** (executar com depuração) ou **Ctrl+F5** (sem depuração). O Visual Studio faz o build incremental necessário automaticamente; você não precisa executar `dotnet build` manualmente a cada mudança.
5. Para os testes use **Teste > Gerenciador de Testes** (Test Explorer) > Executar Todos.
6. Acesse **Git > Criar Repositório Git** (Create Git Repository). Escolha **GitHub**, informe `editor-multimidia`, marque **Privado** e confirme **Criar e Enviar**. Se preferir, crie primeiro um repositório **Somente local**.
7. Em **Git > Alterações do Git**, escreva uma mensagem (`Base estavel v6.0.2`), confirme (**Commit**) e, se houver repositório remoto, envie (**Push**). Algumas versões do Visual Studio já fazem o commit inicial ao criar o repositório.
8. Para mudanças seguintes, use sempre a **mesma pasta**, faça commits após marcos estáveis e evite extrair ZIPs novos por cima do repositório sem conferir as diferenças.

## Se você usa VS Code, não Visual Studio

O VS Code também permite versionar pelo painel **Controle de Código-Fonte**, abrir a pasta do projeto e usar o Git. Os comandos `dotnet run` e `dotnet test` continuam disponíveis no terminal integrado. Já no Visual Studio completo é possível abrir diretamente a `.sln` e pressionar F5.

## Ponto de partida pelo CMD (alternativa à interface)

Abra o terminal **na pasta raiz** do projeto:

```cmd
git init -b main
```

```cmd
git add .
```

```cmd
git commit -m "Base estavel v6.0.2"
```

É necessário ter nome e e-mail configurados no Git para gravar commits. Para conectar a um repositório GitHub já criado e VAZIO:

```cmd
git remote add origin https://github.com/SEU_USUARIO/editor-multimidia.git
```

```cmd
git push -u origin main
```

Substitua `SEU_USUARIO` pelo nome real. Não deixe um README inicial criado separadamente no GitHub se quiser fazer este primeiro push sem resolver históricos diferentes.

## Build e testes automáticos no GitHub

Em `.github/workflows/build-test.yml` existe um workflow do GitHub Actions que, ao enviar commits, executa no Windows: restauração, **compilação da solução completa incluindo WPF** e testes xUnit. O resultado aparece na aba **Actions** do repositório. Isso substitui a necessidade de conferir manualmente todos os builds de cada commit, mas **não substitui** testar a interface e seus comportamentos no Windows.

## O que entra no Git

Inclua `.sln`, `src`, `tests`, `docs`, `.gitignore`, `.gitattributes` e `.github`. Não envie as pastas `bin`, `obj`, `.vs`, mídias locais, projetos pessoais ou arquivos com segredos. O `.gitignore` incluído evita parte desses erros.

## Observações

- Git **não compila** o projeto. O Visual Studio usa compilação incremental e faz o build ao rodar (F5); o GitHub Actions compila/testa automaticamente a cada envio.
- O pacote não contém um repositório remoto e não altera sua conta GitHub. A criação/autorização deve ser feita no Visual Studio ou GitHub.
- O código funcional permanece a versão v6.0.2 validada; a configuração de Git e a solução são adicionais.
