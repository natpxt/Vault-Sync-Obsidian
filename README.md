# Vault Sync - Obsidian

Aplicativo Windows para trabalhar em um vault local e sincronizá-lo com uma pasta do iCloud, com prévia de operações e conflitos visíveis. Projeto independente, sem afiliação à Obsidian ou à Apple.

**Versão:** 1.0.0. **Plataforma:** Windows x64. **Tecnologia:** C# / .NET 8, WPF, SQLite.

Você edita em uma pasta local e o aplicativo mantém uma cópia correspondente na pasta do iCloud deste PC. Ele observa os dois lados em segundo plano, agrupa eventos por aproximadamente um segundo e copia somente os arquivos que precisam mudar. O cliente iCloud da Apple faz a entrega aos outros dispositivos.

## Instalar

1. Baixe `VaultSync-Obsidian-1.0.0-win-x64-Setup.exe` em [Releases](https://github.com/natpxt/Vault-Sync-Obsidian/releases). O repositório pode exigir acesso à conta autorizada enquanto estiver privado.
2. Execute o instalador. Ele inclui o runtime .NET, instala somente para o usuário atual em `%LOCALAPPDATA%/Programs/VaultSync`, cria um atalho no menu Iniciar e uma entrada em Aplicativos instalados. Não exige administrador nem SDK.
3. Para atualizar, encerre o aplicativo pelo botão × ou menu da bandeja antes de executar o novo instalador. Os vaults e o estado ficam preservados.

O executável ainda não tem assinatura comercial. Confira o SHA-256 com `Get-FileHash .\VaultSync-Obsidian-1.0.0-win-x64-Setup.exe -Algorithm SHA256` e compare com `SHA256SUMS.txt` disponibilizado na mesma release. Para compilar em vez de baixar, veja Desenvolvimento abaixo; o script gera o instalador em `dist/`.

## Configurar e usar

Requisitos: iCloud para Windows instalado e conectado; pasta do vault disponível localmente, com permissão de leitura/escrita e espaço para a segunda cópia. No Explorador, escolha manter os arquivos do iCloud baixados e aguarde. Não selecione pastas iguais, sobrepostas, de rede ou links externos; não use outro sincronizador sobre esse mesmo par.

1. Escolha/crie a pasta local de trabalho e selecione o vault correspondente dentro do iCloud.
2. Use **Verificar / prévia** se desejar inspecionar as diferenças antes de iniciar. Conflitos da prévia aparecem na aba Conflitos, mesmo com o monitor parado.
3. Clique **Iniciar sincronização**. A verificação é executada automaticamente; o aplicativo passa a mostrar **ATIVO**, quantas pastas estão observadas e a última conferência. Um arquivo ocupado fica pendente e não impede as demais notas.
4. Acompanhe **Log de execução**: caminhos, sentido da cópia, sucesso, pendências, conflitos e tentativas. **Parar** desativa o monitor; **Pausar/Retomar** suspende temporariamente. Confirme que nenhum outro sincronizador escreve nesse mesmo par; não há checkbox obrigatório baseado apenas em um banco antigo.
5. Em **Opções e backups**, uma cópia de segurança avulsa é opcional. Ela serve para recuperação e não é requisito para iniciar. Arquivos ocupados são relacionados como adiados no manifesto.
6. A revisão de referências Obsidian/Codex permanece separada e explícita. Feche o Obsidian antes de aplicar; reinicie os aplicativos e revise hooks depois.

O iPhone pode continuar editando o vault do iCloud. Instale o aplicativo nos outros Windows e configure a pasta local de cada máquina. Não copie o estado SQLite entre computadores.

O botão × encerra o aplicativo e o monitor. **Minimizar para bandeja** mantém o aplicativo funcionando com o estado indicado no ícone. Uma configuração iniciada pelo usuário pode retomar automaticamente na próxima abertura; usar **Parar** persiste a opção de permanecer parado. Ao atualizar de 0.1.1, o aplicativo fica parado para permitir revisar a nova regra por data. Clique **Verificar / prévia**, depois **Iniciar sincronização** para adotá-la. A desinstalação preserva vaults, backups, pendências e conflitos e informa suas quantidades.

## Comportamento

Igualdade é determinada por SHA-256: **conteúdos idênticos estão sincronizados**, mesmo com datas diferentes ou um registro antigo de recuperação. A lista é reconciliada ao conferir, atualizar ou tentar abrir uma comparação. Artefatos de recuperação permanecem nos dados locais e são apontados no log, sem exigir escolher entre duas cópias iguais.

Quando os dois arquivos existem e os conteúdos diferem, prevalece a **data de modificação mais recente**, em UTC, em qualquer dos lados. Datas empatadas com conteúdos diferentes exigem escolha. Exclusões contra edições e recuperações com conteúdo ainda divergente exigem revisão; exclusões contra conteúdo inalterado são propagadas diretamente, sem histórico geral. Mantenha os relógios dos dispositivos corretos: salvar ou restaurar um arquivo pode mudar sua data, e esta política não faz combinação semântica de edições concorrentes.

Conflitos Markdown têm comparação lado a lado, destaque do trecho divergente, preview nativo e combinação manual. Selecione e use **Comparar e resolver**, duplo clique ou Enter. As versões são atualizadas antes de abrir; alterações posteriores recusam uma escolha desatualizada. O preview não executa HTML/scripts nem carrega imagens ou links remotos. Formatação Markdown é limitada a texto, títulos e blocos de código. Notas acima de 2 MB, 250 mil caracteres ou 5 mil quebras de linha e codificações não suportadas abrem uma descrição com hash e tamanho, sem combinação textual, permitindo inspecionar as cópias preservadas externamente. Arquivos binários também abrem essa descrição.

O monitor agrupa eventos por aproximadamente um segundo. A conferência integral ocorre ao iniciar, após suspensão/erro e a cada cinco minutos, inclusive por conteúdo para reparar eventos perdidos. Renomeações são reconciliadas como criação e exclusão verificadas. Pastas vazias não são removidas automaticamente. Links/junções não pertencentes ao mecanismo do iCloud bloqueiam a leitura completa e devem ser resolvidos antes da ativação.

`sync.ffs_db` e temporários `.megabrainsync-*` são preservados/ignorados pelo sincronizador. Layouts `.obsidian/workspace*.json` e seus temporários também são locais por padrão; há uma opção explícita para incluí-los. As demais notas, configurações, arquivos ocultos e Git participam da comparação. Isso pode ter custo significativo em projetos com muitas dependências.

**Incluir layouts do Obsidian:** copia o estado da interface salvo em `workspace.json`, `workspace-mobile.json` e nomes correspondentes: abas abertas, divisões de painéis e barras laterais. Para espelhar também esse estado, pare o monitor, ative a opção e inicie novamente. Com ela desligada, cada dispositivo mantém seu espaço de trabalho; notas, anexos, temas, plugins e demais configurações seguem incluídos. Obsidian pode regravar os layouts frequentemente e possui diferenças entre desktop e celular; arquivos ocupados ficam pendentes, sem bloquear os demais.

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

Fluxo de branches: `main` recebe versões principais; `dev` recebe desenvolvimento. Nesta primeira organização, `dev` preserva os dois commits iniciais e `main` acrescenta a versão 1. Antes de desenvolver a partir de `dev`, incorpore a versão vigente de `main`. Commits e notas de versão novos são escritos em português; o histórico anterior foi preservado. Novas alterações só recebem commit após teste manual e aprovação explícita do responsável.

## Privacidade, estado e licença

Sem telemetria do aplicativo. Configuração, SQLite, logs, temporários, backups e versões preservadas ficam em `%LOCALAPPDATA%/VaultSync`, fora dos vaults. Não compartilhe esse diretório nem copie o estado para outros PCs. `.gitignore` exclui estado local, credenciais, certificados, evidências brutas e instaladores; os binários são distribuídos como assets da release.

O projeto ainda não definiu licença de código própria; a publicação não adiciona automaticamente uma licença de software livre. Os avisos e licenças das dependências .NET/SQLite estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e no instalador. Assinatura do executável, teste nos demais computadores e confirmação da entrega ao iPhone continuam pendentes.
