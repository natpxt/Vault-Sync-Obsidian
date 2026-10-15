# Operação e recuperação

## Estado local

`%LOCALAPPDATA%/VaultSync/config.json` guarda as raízes. Cada par possui `state.sqlite`, `operations/`, `conflicts/` e `migration-backup/`. Backups de integração ficam em `integration-backups/`. Esses dados não são enviados pelo aplicativo a serviços externos.

O banco é a referência de sincronização deste computador: não deve ser compartilhado com outro PC. Se você retirar a configuração para escolher novas raízes, saia primeiro do aplicativo e preserve o diretório de estado. Sem baseline, arquivos diferentes dos dois lados voltam a ser conflitos iniciais; exclusões não são deduzidas de uma origem desconhecida.

## Bloqueios

- **Baixe e mantenha neste dispositivo:** use o Explorador de Arquivos, marque a pasta do iCloud para permanecer baixada e aguarde; depois use Conferir agora.
- **Arquivo bloqueado:** conclua a gravação ou feche o aplicativo correspondente. A fila tenta novamente.
- **Raiz indisponível:** restaure o acesso. Não recrie uma pasta vazia para simular uma raiz desaparecida.
- **Junção/link externo:** não é percorrido. Reveja a inclusão com cuidado; não remova links automaticamente para satisfazer a verificação.
- **Operação interrompida:** abra o conflito e consulte as versões preservadas antes de escolher. Não apague temporários ou registros à mão durante recuperação.
- **Conflito que mudou:** reabra o comparador; a resolução anterior foi recusada para não sobrescrever uma nova edição.

O aplicativo não pode forçar o iCloud a enviar alterações nem certificar que outro dispositivo as recebeu. Confira o status do iCloud antes de desligar e, quando possível, evite editar a mesma nota simultaneamente. Os fluxos offline em notas diferentes e conflitos que chegam ao filesystem local são cobertos pelos testes.

## Reverter a migração

Pause e encerre o aplicativo nos PCs envolvidos. Identifique o backup verificado e as versões que mudaram depois dele. Restaure somente após comparar essas versões, para não substituir trabalho posterior pelo backup inicial. Reverta referências externas pelos arquivos `.before` e manifesto do backup de integração; confirme o alvo da junção `memory-sync`. Reabra o vault pretendido no Obsidian e reinicie Codex. Não rode dois sincronizadores sobre as mesmas raízes.

## Distribuição

O `.exe` do piloto não possui assinatura comercial. Não há atualização automática nem publicação GitHub nesta entrega. Atualizações exigem fechar o aplicativo e executar o novo instalador. Desinstalar preserva os arquivos do usuário e o estado local; o pequeno helper de desinstalação pode permanecer na pasta temporária do Windows.

Antes de uma release pública: escolher licença, revisar arquivos e dependências, avaliar assinatura, executar piloto nos outros Windows e verificar o fluxo real do iPhone. Nunca adicionar ao Git os bancos, configurações, notas ou backups pessoais.
