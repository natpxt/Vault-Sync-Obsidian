# Arquitetura e decisões

## Modelo

Cada computador possui uma cópia local, a pasta correspondente do iCloud e um estado próprio em `%LOCALAPPDATA%/VaultSync`. A configuração seleciona as duas raízes; o identificador do par deriva dos caminhos normalizados. SQLite em WAL com `synchronous=FULL` armazena baseline por caminho, fila com tentativas e prazo, conflitos e ativação. O hash determina igualdade. Dois conteúdos diferentes presentes dos dois lados usam o maior `ModifiedUtcTicks` como vencedor; empate, exclusão contra edição e recuperação ainda divergente exigem escolha. A política anterior permanece testada como modo de compatibilidade do núcleo; a interface aplica a política por data solicitada pelo usuário.

Conflitos comuns persistidos são reavaliados tanto por evento como na conferência integral. Metadados de data são revalidados antes de publicar uma cópia, além dos hashes. A data da origem é preservada no destino. Configuração antiga sem `PolicyRevision >= 1` não retoma automaticamente: a interface mostra a regra e aguarda novo Iniciar. Assim, uma atualização não aplica a mudança de política ao par real sem revisão.

O núcleo é independente da interface e recebe raízes/estado explícitos. Não depende de Obsidian aberto. A interface usa um worker serial para impedir concorrência entre sincronização e resolução de conflitos. Um mutex impede duas instâncias do aplicativo no mesmo usuário/sessão; o SQLite permanece fora do iCloud.

## Segurança das operações

Scanner e caminhos distinguem ausência real de erro. Raiz ausente/inacessível impede operações do par inteiro. Arquivo bloqueado, placeholder não baixado ou subpasta inacessível bloqueiam somente esse caminho/subárvore: os demais arquivos continuam, sem inferir exclusões dentro dos trechos não lidos. Pendências entram na fila e são tentadas novamente. As raízes são revalidadas antes de cada operação. Caminhos relativos não aceitam `..`, raiz absoluta ou alternate data streams. Layouts `workspace*.json` do Obsidian são locais por padrão, com inclusão opcional.

Antes de cada mutação existe um registro persistido em `operations/`. Substituição prepara uma cópia verificada no diretório do destino e usa `File.Replace` com captura atômica dos bytes substituídos. Se esses bytes divergirem da prévia, são preservados como conflito. Exclusão renomeia temporariamente o arquivo, verifica os bytes efetivamente retirados e só então o remove. Uma versão diferente é restaurada quando possível e preservada para resolução. O estado só avança depois de verificar igualdade dos dois lados.

Registros de operações interrompidas são recuperados no próximo ciclo: quando houve publicação ou existem bytes anteriores capturados, as versões são preservadas. Uma preparação que falhou antes de publicar é descartada com segurança e tentada novamente, sem conflito falso. Desde 1.0.0, conteúdos atuais idênticos encerram qualquer conflito ativo, inclusive de recuperação: o estado é reconhecido após revalidar as duas leituras; artefatos de recuperação são mantidos com `converged.json`, e o log aponta a pasta. Um conteúdo ainda divergente continua exigindo revisão nesses casos. Uma escolha ou combinação é revalidada antes de ser aplicada e só remove as cópias depois da verificação final. O backup avulso é opcional e independente do monitor.

Editores e o provedor iCloud não participam de uma transação global. Há uma janela inevitável entre a inspeção e uma alteração externa; as capturas atômicas reduzem o risco de sobrescrever bytes não observados no filesystem local, mas não oferecem garantia distribuída. Uma interrupção após completar uma operação e antes de reconhecer o baseline converge pela comparação do conteúdo no próximo ciclo.

## Monitor e consumo

`FileSystemWatcher` observa nomes, tamanhos e gravações; uma fila de eventos em memória agrupa um segundo de estabilidade. Antes de aplicar uma operação, o caminho entra na fila persistente. Eventos ainda não persistidos que se percam com uma interrupção são recuperados pela conferência integral na inicialização. Falhas transitórias usam backoff exponencial, limitado a cinco minutos. Reinício não elimina tentativas ou conflitos.

No fluxo habitual, somente os caminhos alterados são lidos. Criações/exclusões de diretórios, renomeações, erros de buffer e retomada da suspensão solicitam conferência integral. A cada cinco minutos o scanner recalcula hashes: isso encontra alterações de mesmo tamanho e timestamp, mas gera I/O proporcional ao conteúdo incluído. Um vault com dependências ou binários grandes pode exigir ajuste posterior dessa política, com medição antes de reduzir garantias.

## Integração e instalação

O mesmo executável autocontido fornece instalação por usuário, aplicativo e desinstalação. O instalador copia e verifica o próprio hash, cria atalho e registro HKCU; não instala serviços nem concede administrador. O runtime e a biblioteca SQLite são extraídos pelo empacotamento .NET no cache de execução do usuário.

O bundle não comprime assemblies. No teste de três telas, a variante comprimida usou cerca de 282 MiB de conjunto de trabalho; a variante sem compressão usou cerca de 162 MiB. Foi escolhido um instalador maior no disco, aproximadamente 156 MiB, para reduzir memória em execução. Esses números representam o processo de smoke test, não o núcleo em repouso nem o consumo do iCloud.

A integração opcional mostra alterações de caminho em arquivos conhecidos do Obsidian e Codex. Não reescreve documentos históricos, identidades de unidades, estados de confiança de hooks ou credenciais. Guarda backup e verifica conteúdo anterior antes de alterar cada arquivo; não é transação entre todos os arquivos. Uma falha parcial é relatada e os originais permanecem disponíveis no backup para restauração orientada.

O aplicativo registra a própria máquina nos artefatos locais de conflito. Sem metadados entregues pelo iCloud, não inventa o nome do computador autor da versão recebida.

## Interface e comparação

Janelas compartilham cabeçalho, borda roxa, diálogos, rolagem fina e opções em forma de interruptor. O tamanho inicial é limitado à área útil do monitor, considerando DPI e janela proprietária; o comparador usa 940×640 DIPs como preferência e encolhe em telas menores. Seletores de pasta são diálogos do próprio Windows.

`PrepareComparisonAsync` usa o mesmo semáforo do worker. Atualiza snapshots persistidos quando necessário, preserva revisões anteriores e prepara texto antes de abrir a janela. Notas extensas ou ilegíveis como texto oferecem metadados em vez de impedir toda a comparação. A resolução revalida conteúdo e datas observadas. Erros aparecem na aba e em diálogo temático, além do log; botões ocupados ficam desabilitados.
