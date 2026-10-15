# Arquitetura e decisões

## Modelo

Cada computador possui uma cópia local, a pasta correspondente do iCloud e um estado próprio em `%LOCALAPPDATA%/VaultSync`. A configuração seleciona as duas raízes; o identificador do par deriva dos caminhos normalizados. SQLite em WAL com `synchronous=FULL` armazena baseline por caminho, fila com tentativas e prazo, conflitos e ativação. Timestamps e tamanhos são informativos; o hash determina igualdade.

O núcleo é independente da interface e recebe raízes/estado explícitos. Não depende de Obsidian aberto. A interface usa um worker serial para impedir concorrência entre sincronização e resolução de conflitos. Um mutex impede duas instâncias do aplicativo no mesmo usuário/sessão; o SQLite permanece fora do iCloud.

## Segurança das operações

Scanner e caminhos distinguem ausência real de erro. Arquivo bloqueado, leitura negada, placeholder não baixado, raiz ausente ou reparse point externo tornam a conferência incompleta. A sincronização integral não aplica nenhuma operação se a conferência estiver incompleta; o worker permanece bloqueado até a próxima conferência completa. As raízes são revalidadas antes de cada operação. Caminhos relativos não aceitam `..`, raiz absoluta ou alternate data streams.

Antes de cada mutação existe um registro persistido em `operations/`. Substituição prepara uma cópia verificada no diretório do destino e usa `File.Replace` com captura atômica dos bytes substituídos. Se esses bytes divergirem da prévia, são preservados como conflito. Exclusão renomeia temporariamente o arquivo, verifica os bytes efetivamente retirados e só então o remove. Uma versão diferente é restaurada quando possível e preservada para resolução. O estado só avança depois de verificar igualdade dos dois lados.

Registros de operações interrompidas são recuperados no próximo ciclo: versões atuais, bytes anteriores e fonte preparada são preservados em conflito. Conflitos são locais e persistem entre reinícios. Uma escolha ou combinação é revalidada antes de ser aplicada e só remove as cópias do conflito depois da verificação final. O backup de migração é independente dessas cópias e não é removido automaticamente.

Editores e o provedor iCloud não participam de uma transação global. Há uma janela inevitável entre a inspeção e uma alteração externa; as capturas atômicas reduzem o risco de sobrescrever bytes não observados no filesystem local, mas não oferecem garantia distribuída. Uma interrupção após completar uma operação e antes de reconhecer o baseline converge pela comparação do conteúdo no próximo ciclo.

## Monitor e consumo

`FileSystemWatcher` observa nomes, tamanhos e gravações; uma fila de eventos em memória agrupa um segundo de estabilidade. Antes de aplicar uma operação, o caminho entra na fila persistente. Eventos ainda não persistidos que se percam com uma interrupção são recuperados pela conferência integral na inicialização. Falhas transitórias usam backoff exponencial, limitado a cinco minutos. Reinício não elimina tentativas ou conflitos.

No fluxo habitual, somente os caminhos alterados são lidos. Criações/exclusões de diretórios, renomeações, erros de buffer e retomada da suspensão solicitam conferência integral. A cada cinco minutos o scanner recalcula hashes: isso encontra alterações de mesmo tamanho e timestamp, mas gera I/O proporcional ao conteúdo incluído. Um vault com dependências ou binários grandes pode exigir ajuste posterior dessa política, com medição antes de reduzir garantias.

## Integração e instalação

O mesmo executável autocontido fornece instalação por usuário, aplicativo e desinstalação. O instalador copia e verifica o próprio hash, cria atalho e registro HKCU; não instala serviços nem concede administrador. O runtime e a biblioteca SQLite são extraídos pelo empacotamento .NET no cache de execução do usuário.

O bundle não comprime assemblies. No teste de três telas, a variante comprimida usou cerca de 282 MiB de conjunto de trabalho; a variante sem compressão usou cerca de 162 MiB. Foi escolhido um instalador maior no disco, aproximadamente 156 MiB, para reduzir memória em execução. Esses números representam o processo de smoke test, não o núcleo em repouso nem o consumo do iCloud.

A integração opcional mostra alterações de caminho em arquivos conhecidos do Obsidian e Codex. Não reescreve documentos históricos, identidades de unidades, estados de confiança de hooks ou credenciais. Guarda backup e verifica conteúdo anterior antes de alterar cada arquivo; não é transação entre todos os arquivos. Uma falha parcial é relatada e os originais permanecem disponíveis no backup para restauração orientada.

O aplicativo registra a própria máquina nos artefatos locais de conflito. Sem metadados entregues pelo iCloud, não inventa o nome do computador autor da versão recebida.
