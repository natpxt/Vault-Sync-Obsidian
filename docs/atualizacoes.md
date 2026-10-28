# Atualização assistida — 1.2

## Uso

Disponível a partir da versão `1.2.0`. Quem usa 1.1.0 precisa instalar a nova versão manualmente uma vez; depois disso, o próprio aplicativo pode oferecer releases seguintes.

A instalação padrão verifica ao iniciar e a cada seis horas, sem bloquear a janela ou o monitor. Em Configurações há versão instalada, última consulta, preferência automática e botão manual. O aviso abre os detalhes com **Atualizar agora** ou **Depois**. Adiar não oculta a atualização da consulta manual; a mesma versão não repete notificações durante a sessão e pode ser oferecida na próxima abertura.

Aceitar inicia o download completo com indicação de bytes e cancelamento. O sincronizador continua trabalhando. Só após verificar o pacote o programa impede novas operações, aguarda a operação da interface e encerra o worker sem usar o comando Parar, que alteraria a preferência de retomada. O auxiliar aguarda o processo anterior e troca o executável. O programa reabre ativo, pausado ou parado conforme estava; pausa é aplicada antes do primeiro ciclo do worker.

Avaliações e portáteis permitem consultar e abrir a página oficial, mas não aplicam atualização. As consultas automáticas são iniciadas apenas na instalação padrão. Erros de rede, pacotes incompletos e cancelamentos ficam na área de atualização/log, sem transformar uma sincronização saudável em erro do monitor.

## Contrato com o GitHub

- Fonte fixa: `natpxt/Vault-Sync-Obsidian`, API `/releases/latest`, sem autenticação para o repositório público.
- Só versão estável superior à instalada. Comparação numérica, com versão estável posterior à prévia da mesma base; metadados de build não mudam a ordem.
- Assets obrigatórios: `VaultSync-Obsidian-X.Y.Z-win-x64-Setup.exe` e `SHA256SUMS.txt`. A tag pode ser `vX.Y.Z` ou `X.Y.Z`.
- Conferência de tamanho anunciado, checksum único do arquivo, digest SHA-256 da API quando fornecido e versão do executável. Pacotes acima de 512 MiB ou versões divergentes são recusados.
- URLs iniciais fixadas à mesma release; HTTPS e destinos oficiais do GitHub nos redirecionamentos. Notas são texto, sem HTML ativo ou recursos remotos.
- Consulta com timeout de 15 segundos, cache/ETag e espera conforme `Retry-After`/limites da API. Download com limite total de 30 minutos, espera máxima de 60 segundos por leitura de dados do pacote e cancelamento.
- Conferência de espaço para pacote, auxiliar, backup e estágio do instalador. O hash protege integridade, não substitui uma assinatura do editor ou protege contra comprometimento da conta publicadora.

Publique futuras releases primeiro como rascunho, envie instalador e checksum, confira os arquivos e só então marque como estável/Latest. Commits isolados, drafts e prévias não atualizam o aplicativo. Não reutilize a mesma versão para distribuir binários diferentes.

## Processos, dados e recuperação

O componente `VaultSync.Updates` não depende do núcleo de sincronização nem de WPF. Usa `HttpClient`, JSON e SHA-256 da plataforma; o transporte é substituível em testes.

Três modos internos permanecem como contrato para versões futuras:

- `--apply-update <request.json>`: auxiliar copiado da instalação anterior, valida o pedido/pacote, confirma que está preparado, aguarda o PID antigo e coordena a instalação.
- `--install-update <request.json>`: pacote novo executado sem interface, com destino fixo por usuário e preferência de startup preservada.
- `--resume-update <request.json>`: aplicativo instalado restaura o estado anterior e apresenta o resultado.

O protocolo 1 repassa PID e instante de criação do processo anterior, versão, arquivo, hash, startup, estado do monitor e visibilidade. O pedido deve estar em um diretório GUID dentro da área de atualizações deste usuário; não pode escolher outro destino de instalação nem atravessar links/junções. O auxiliar usa lock exclusivo, e novas aberturas esperam a atualização terminar. Não há encerramento forçado de processos.

São feitas até três tentativas de instalação. Antes da troca, executável, avisos, atalho e valores do instalador/startup são guardados. Uma falha na instalação, verificação ou criação do processo novo tenta restaurar a versão anterior; a restauração dos arquivos usa estágio verificado e substituição, com tentativas limitadas. Se o Windows impedir a recuperação, o erro e os backups permanecem disponíveis; não são removidas proteções ou alteradas permissões.

Preferências/cache e jobs ficam em `%LOCALAPPDATA%/VaultSync/updates`. Não há migração do SQLite ou reescrita dos vaults. Após retomada bem-sucedida, pacote/auxiliar podem ser removidos e jobs anteriores concluídos são limpos; o backup da instalação imediatamente anterior é conservado. Falhas preservam diagnósticos. Consultas não acrescentam varredura dos vaults.

## Validação e próxima etapa

`scripts/build.ps1 -Publish` executa regressão, testes do atualizador, smoke WPF, ciclo isolado do instalador e atualização entre dois pacotes. O pacote secundário estável é fictício, fica somente em `%LOCALAPPDATA%/VaultSyncBuild` e nunca é release ou entregável em `dist/`.

As fixtures usam raízes, mutexes e chaves HKCU próprios. Testam processo anterior aberto, concorrência, ativo/pausado/parado, bloqueio de executável, falha após substituição, pacote adulterado e falha ao reabrir. Conferem configuração, SQLite, conflitos, backup, tema, idioma e startup. Modos internos de teste recusam destinos fora da área GUID de sandboxes.

A consulta real de rede é uma prova separada e opcional (`--test-update-network`), sem baixar ou instalar pacote. Testes determinísticos usam HTTP simulado. Nenhum teste precisa do vault real ou publica versões de teste no GitHub.

O lançamento 1.2.0 foi autorizado explicitamente pelo responsável em 2026-10-28, que optou por validar o fluxo dinamicamente nas próximas implementações. Esta aprovação inclui commit, release, instalação e execução neste PC; não concede aprovação antecipada para próximos lançamentos. Após concluir a release, remover executáveis de prévia de `dist/`, preservando versões oficiais.
