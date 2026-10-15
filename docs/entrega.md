# Entrega — 2026-10-14

## Escopo implementado

Aplicativo instalável Windows x64 com configuração de duas raízes, prévia de diferenças, backup de migração, sincronização bidirecional, conflitos, fila SQLite, bandeja e inicialização com Windows. Comparador e preview Markdown locais, resolução manual e preservação de operações interrompidas. Integração opcional revisável com referências do Obsidian e Codex.

O produto passou a se chamar **Vault Sync - Obsidian** por decisão do usuário em 2026-10-14, substituindo a sugestão intermediária ValSync. Código e registros foram reunidos nesta pasta, dentro de `99 System/MegaBrain/`, conforme pedido. O Git é próprio e local, com branch `main`; não há remote ou publicação GitHub. Licença pública permanece pendente.

## Evidências

- Compilação Release sem avisos ou erros.
- 44 testes de comportamento aprovados: edições offline em dois PCs, exclusões, conflitos, hash, alterações de mesmo tamanho/data, reinício, recuperação de operação interrompida, links externos e worker real com pausa/retomada.
- Smoke test de três telas, renderizado e inspecionado: instalação, configuração e comparação de conflitos.
- Executável autocontido validado: instalação por usuário, hash instalado, startup, atualização e remoção dos componentes instalados preservando os dados.
- Verificação de pacote registra memória do processo que renderiza as três telas; não representa o consumo em repouso com um vault real.
- Empacotamento sem compressão de assemblies escolhido após comparação: aproximadamente 162–165 MiB no smoke test de três telas, contra 282 MiB na variante comprimida. O instalador ocupa aproximadamente 156 MiB. A meta inicial de 50–150 MB não deve ser anunciada como medição da interface completa.
- Aplicativo efetivamente instalado, com janela de configuração aberta e sincronização ainda inativa: **141,2 MiB de conjunto de trabalho**, **78,7 MiB de memória privada** e **0,00% de CPU total** em uma amostra de seis segundos. É um cenário diferente do benchmark do núcleo e não inclui o processo do iCloud.
- Relatórios sanitizados em `evidence/`. Fixtures e saídas intermediárias permanecem fora do vault.

Benchmark isolado: 500 arquivos de aproximadamente 2 KB, em pastas temporárias NTFS, núcleo .NET sem WPF nem iCloud. Prévia: **1,37 s**; cópia inicial verificada: **23,27 s**; conferência de conteúdo sem alterações: **1,81 s**; memória: **53,7 MiB**; CPU em repouso: **0,043% da capacidade total**, amostra de seis segundos após a conferência inicial; latência de uma edição: **1,41 s**. Valores experimentais, sem promessa para volumes maiores ou o provedor iCloud.

## Implantação e limites

O instalador do piloto fica em `dist/`, acompanhado de SHA-256. Instala em `%LOCALAPPDATA%/Programs/VaultSync`; estado e backups ficam em `%LOCALAPPDATA%/VaultSync`. O aplicativo não exige SDK ou runtime pré-instalado nas outras máquinas.

O código e o pacote estão prontos para revisar a configuração. A migração do vault real, a alteração das referências externas e a ativação da sincronização dependem da prévia e da aprovação no assistente. Não foram executadas silenciosamente. A infraestrutura canônica de memória, o caminho de trabalho atual e as configurações reais do Obsidian/Codex não foram migrados nesta entrega.

Instalado neste computador para o usuário atual, com atalho, registro de desinstalação e inicialização com Windows. Interface aberta para revisar a configuração; nenhuma raiz foi ativada automaticamente. A instalação e o pacote final foram comparados por SHA-256.

Os outros dois Windows e o fluxo real do iPhone ainda exigem piloto; não há acesso a esses dispositivos nesta sessão. Os testes usam fixtures locais e simulam a ordem de chegada de alterações; não comprovam entrega do iCloud. O produto não oferece garantia distribuída contra versões sobrescritas pela Apple antes de chegarem ao PC.

Executável do piloto sem assinatura comercial, sem atualização automática e sem histórico geral adicional. Pastas vazias não são removidas; renomeações de arquivos são reconciliadas como cópia e exclusão. O preview Markdown é nativo e limitado, sem HTML ativo ou rede. Sincronizadores anteriores precisam ser revisados e desativados pelo usuário antes da ativação.

## Próximos passos

1. Revisar as duas raízes na interface, baixar os arquivos do iCloud e conferir possíveis links externos.
2. Revisar a prévia, desativar tarefas anteriores e aprovar backup/ativação.
3. Revisar a integração de caminhos, reiniciar os aplicativos e validar os hooks no Codex sem alteração automática de confiança.
4. Executar o piloto em cada Windows e no iPhone, com duas notas de teste e um conflito controlado.
5. Antes de publicar: decidir licença e titularidade, revisar o repositório, avaliar assinatura e validar em máquinas independentes.
