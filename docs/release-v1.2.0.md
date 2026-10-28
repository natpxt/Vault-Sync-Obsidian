# Vault Sync 1.2.0 · Atualizações pelo aplicativo

O Vault Sync passa a consultar novas versões no GitHub e oferecer a instalação dentro do próprio aplicativo, com sua confirmação.

## Novidades

- Verificação ao abrir e a cada seis horas, inclusive na bandeja; controle automático e botão **Verificar agora** em Configurações → Atualizações.
- Aviso de nova versão, novidades e ações **Atualizar agora** ou **Depois**. Nenhum download começa sem confirmação.
- Download com progresso e cancelamento, mantendo a sincronização funcionando até o pacote estar validado.
- Conferência de tamanho, SHA-256, digest do GitHub quando disponível e versão do executável; somente releases estáveis completas do repositório oficial.
- Auxiliar de instalação com bloqueio de concorrência, tentativas limitadas e recuperação da versão anterior em caso de falha na troca.
- Retomada no estado anterior: ativo, pausado ou parado. Configuração, SQLite, conflitos, backups, startup, idioma e tema são preservados.
- Interface Órbita mantida em português, inglês e espanhol, nos temas Sistema, Claro e Escuro.

## Instalação

Quem utiliza a 1.1.0 precisa encerrar o aplicativo e instalar a **1.2.0 manualmente uma vez em cada PC**. A partir dela, futuras releases estáveis completas poderão ser oferecidas pelo aplicativo. Não é necessário login no GitHub.

Baixe `VaultSync-Obsidian-1.2.0-win-x64-Setup.exe`. O arquivo `SHA256SUMS.txt` acompanha o instalador. A instalação é por usuário, inclui o runtime e não exige administrador. Avaliações e cópias portáteis continuam usando instalação manual.

## Validação e limites

Validação automatizada com 105 testes, sete cenários de atualização/recuperação entre processos em instalações isoladas, ciclo do instalador e smoke WPF. O responsável autorizou o lançamento, optando por validar dinamicamente a atualização em próximas implementações; não houve homologação manual completa do fluxo entre releases públicas.

As consultas não acrescentam varreduras dos vaults. SHA-256 verifica integridade; assinatura do editor permanece futura. O aplicativo não garante recuperação de versões sobrescritas pelo iCloud antes de chegarem ao PC. Mais detalhes no [manual do atualizador](atualizacoes.md).
