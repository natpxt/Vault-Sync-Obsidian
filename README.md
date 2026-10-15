# Vault Sync - Obsidian

Aplicativo Windows para trabalhar em um vault local e sincronizá-lo com uma pasta do iCloud, com prévia de operações e conflitos visíveis. Projeto independente, sem afiliação à Obsidian ou à Apple.

**Versão:** 0.1.0, piloto. **Plataforma:** Windows x64. **Tecnologia:** C# / .NET 8, WPF, SQLite. **Estado:** consultar [registro de entrega](docs/entrega.md).

## Instalar

Abra `dist/VaultSync-Obsidian-0.1.0-win-x64-Setup.exe`. O instalador inclui o runtime, instala somente para o usuário atual e cria um atalho no menu Iniciar e uma entrada em Aplicativos instalados. Não solicita administrador. A atualização usa o mesmo instalador; saia do aplicativo pela bandeja antes de atualizar.

1. Escolha/crie a pasta local de trabalho e selecione o vault correspondente dentro do iCloud.
2. Use **Verificar e pré-visualizar**. Resolva bloqueios, baixe os arquivos pendentes do iCloud e confira a prévia.
3. Desative outros sincronizadores sobre o mesmo par de pastas e confirme essa verificação na tela.
4. Use **Criar backup e iniciar**. A cópia de segurança será verificada antes da aplicação da prévia.
5. Em **Verificações e integração**, revise separadamente referências do Obsidian e Codex. Feche o Obsidian antes de aplicar. Reinicie os aplicativos depois; hooks alterados podem exigir revisão no Codex.

O iPhone pode continuar editando o vault do iCloud. Instale o aplicativo nos outros Windows e configure a pasta local de cada máquina. Não copie o estado SQLite entre computadores.

Fechar a janela mantém o aplicativo na bandeja. Para encerrar, use **Sair** na bandeja. A desinstalação preserva vaults, backups, pendências e conflitos e informa suas quantidades.

## Comportamento

Igualdade é determinada por SHA-256, não por data. A versão sincronizada anterior permite distinguir uma edição de um arquivo inalterado. Edições diferentes dos dois lados pausam somente aquele arquivo. Exclusões contra edições também são conflitos; exclusões contra conteúdo inalterado são propagadas diretamente, sem histórico geral.

Conflitos Markdown têm comparação lado a lado, destaque do trecho divergente, preview nativo e combinação manual. O preview não executa HTML/scripts nem carrega imagens ou links remotos. Formatação Markdown é deliberadamente limitada a texto, títulos e blocos de código; notas acima de 2 MB devem ser comparadas externamente para evitar truncamento. Outros arquivos exibem tamanho e hash; a inspeção do conteúdo binário é externa.

O monitor agrupa eventos por aproximadamente um segundo. A conferência integral ocorre ao iniciar, após suspensão/erro e a cada cinco minutos, inclusive por conteúdo para reparar eventos perdidos. Renomeações são reconciliadas como criação e exclusão verificadas. Pastas vazias não são removidas automaticamente. Links/junções não pertencentes ao mecanismo do iCloud bloqueiam a leitura completa e devem ser resolvidos antes da ativação.

`sync.ffs_db` e temporários `.megabrainsync-*` são preservados/ignorados pelo sincronizador; não são conteúdo replicado. Os demais arquivos, inclusive ocultos, Git e configurações, participam da comparação. Isso pode ter custo significativo em projetos com muitas dependências.

**Limite:** sucesso significa conteúdo verificado entre as pastas deste PC. Não significa confirmação de upload pela Apple. Conflitos de versões que o iCloud sobrescreveu antes de entregá-las ao PC não podem ser recuperados pelo aplicativo. O banco local não fornece uma transação distribuída entre os dispositivos.

## Desenvolvimento

```powershell
./scripts/build.ps1 -Dotnet 'caminho/para/dotnet.exe' -Publish
```

Requer SDK .NET 8 x64. O script compila, executa testes, renderiza a interface e gera o instalador. Use `-SkipUi` em CI sem desktop. Builds, NuGet e fixtures permanecem fora do vault; a saída de build padrão é `%LOCALAPPDATA%/VaultSyncBuild`.

- `src/Core`: comparação, arquivos, SQLite, fila e recuperação.
- `src/App`: interface, instalador e integração opcional.
- `tests`: testes de comportamento e benchmark isolado.
- `docs`: arquitetura, operação, evidências e entrega.
- `scripts`: build reproduzível e validação.
- `dist`: instalador e checksums; o binário não entra no Git.

Sem telemetria do aplicativo. O repositório é local e não tem publicação remota. Antes de disponibilizar publicamente, decidir licença, revisar privacidade, assinar o executável e validar em outros computadores. Os pacotes SQLite/.NET têm suas próprias licenças, preservadas no arquivo de avisos.
