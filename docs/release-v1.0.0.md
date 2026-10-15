# Vault Sync - Obsidian 1.0.0

Primeiro lançamento principal: sincronização bidirecional entre uma pasta local de trabalho e o vault correspondente na pasta do iCloud para Windows. Projeto independente da Obsidian e da Apple.

## Instalação

Baixe `VaultSync-Obsidian-1.0.0-win-x64-Setup.exe` nos arquivos desta release. O instalador inclui o runtime, não exige administrador e preserva seus vaults e estado ao atualizar. Encerre uma versão anterior antes de instalar. O checksum SHA-256 está em `SHA256SUMS.txt`.

Selecione as duas pastas, confira a prévia e clique em **Iniciar sincronização**. O status ATIVO, o log e os controles de pausa/parada permitem acompanhar a execução. O [README](../README.md) detalha configuração, recuperação, layouts do Obsidian e desinstalação.

## O que foi entregue

- Monitoramento das duas pastas, cópia de arquivos novos/alterados, fila persistente e retomada após falhas.
- Igualdade por SHA-256. Quando conteúdos diferem e os arquivos existem dos dois lados, prevalece a data de modificação mais recente.
- Conteúdos idênticos encerram conflitos antigos, inclusive de recuperação; versões recuperadas permanecem guardadas e localizáveis pelo log.
- Empates com conteúdos diferentes, exclusão contra edição e recuperações ainda divergentes exigem escolha.
- Comparação lado a lado, preview Markdown sem conteúdo ativo ou recursos remotos e combinação manual. Datas visíveis somente até segundos.
- Interface roxa, janelas adaptadas à área útil, rolagem fina, log contínuo, bandeja e inicialização com Windows.
- Backup avulso opcional e revisão explícita de referências externas.
- README revisado e exclusão de estado local, credenciais, certificados e artefatos de desenvolvimento pelo `.gitignore`.

## Verificação e limites

79 testes automatizados do núcleo aprovados e smoke test de interface em 11 telas, incluindo o pacote autocontido. Instalação por usuário e hash do binário verificados localmente. O usuário confirmou no teste manual do MVP a cópia de um arquivo criado enquanto o monitor estava parado e aprovou este lançamento com a correção de conflitos.

A confirmação do aplicativo vale para as duas pastas deste PC; entrega à nuvem e aos demais dispositivos depende da Apple. Os outros PCs e o iPhone ainda precisam de homologação. Arquivos indisponíveis/bloqueados ficam pendentes; falha de leitura não vira exclusão. O executável ainda não possui assinatura comercial. A licença própria do projeto permanece sem definição; licenças de terceiros acompanham o instalador.

## Histórico e desenvolvimento

`main` recebe este lançamento principal. `dev` preserva o ponto dos dois commits iniciais para organizar o desenvolvimento; os históricos anteriores não foram reescritos. Antes de começar novas alterações em `dev`, incorpore `main`. Novos commits e releases têm descrições em português e dependem de aprovação explícita após teste manual.
