# Vault Sync - Obsidian

**Estado:** implementação e pacote do piloto 0.1.0; ativação do vault real pendente de revisão no assistente.

**Data:** 2026-10-14.
**Origem:** solicitação do usuário para implementar aplicativo instalável; nome e endereço do projeto confirmados durante a execução.

## Finalidade e decisões

Trabalhar localmente nos três computadores Windows e sincronizar com o vault do iCloud. O iPhone continua usando Obsidian diretamente no iCloud. Usar comparação SHA-256 e baseline por computador; edições concorrentes diferentes pausam somente o arquivo afetado. Conteúdo idêntico converge. Exclusões contra conteúdo inalterado são propagadas diretamente; exclusão contra edição exige escolha. Não escolher vencedor automaticamente pela data.

Nome aprovado: **Vault Sync - Obsidian**. Projeto completo nesta pasta com desenvolvimento, documentação, registros, testes, instalador, Git e instruções locais para agentes. Não é uma nova Area taxonômica. Repositório remoto e publicação são possibilidades futuras, não ações realizadas nesta entrega.

O aplicativo apresenta verificações e prévia antes da ativação e solicita aprovação específica para referências externas do Obsidian e Codex. Não altera confiança de hooks. O limite conhecido permanece: versões sobrescritas pelo iCloud antes de chegarem ao PC não podem ser recuperadas por um sincronizador externo.

## Entrega e continuidade

- [Uso e desenvolvimento](README.md)
- [Registro de entrega, testes e limites](docs/entrega.md)
- [Arquitetura](docs/arquitetura.md)
- [Operação e recuperação](docs/operacao.md)
- [Marco de governança](docs/IA%20%C2%B7%20GOV%20%C2%B7%20Implanta%C3%A7%C3%A3o%20do%20Vault%20Sync.md)
- [Histórico de versões do aplicativo](CHANGELOG.md)

As evidências principais são 44 testes de comportamento, smoke test visual de três telas e verificações de instalação/atualização/remoção. Consumo medido do núcleo isolado: aproximadamente 54 MiB e 0,04% da CPU total em repouso; interface e iCloud não incluídos nesse benchmark.

No aplicativo instalado com a configuração aberta e sincronização inativa, a medição foi de aproximadamente 141 MiB de conjunto de trabalho e 79 MiB de memória privada. O instalador autocontido tem aproximadamente 156 MiB; o bundle sem compressão foi escolhido para reduzir memória em execução.

Próxima ação: revisar as raízes e a prévia na interface, aprovar o backup e ativação, depois validar os demais dispositivos. O vault real não foi migrado silenciosamente. Não houve nova versão do baseline MegaBrain V1.8 nem alteração de taxonomia.
