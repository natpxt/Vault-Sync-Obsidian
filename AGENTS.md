# Vault Sync - Obsidian

Projeto integrado ao MegaBrain. Recupere o mestre do vault e a skill canônica memory-sync; não copie essas instruções para o repositório.

Nome do produto: **Vault Sync - Obsidian**. Identificadores de código: `VaultSync`. Não é produto oficial da Obsidian ou Apple.

- Código em `src/`, testes em `tests/`, documentação em `docs/`, automação em `scripts/`.
- Nunca execute sincronização, migração ou testes destrutivos sobre o vault real sem prévia revisada. Use fixtures isoladas.
- Invariantes: igualdade por SHA-256; estado SQLite fora das raízes; falha de leitura não é exclusão; conflitos preservam versões; não seguir links/junções externos; não carregar recursos remotos no preview.
- Não publicar GitHub, telemetria, releases ou dados pessoais por iniciativa própria. Revisar conteúdo e licença antes de publicação.
- Builds e dependências ficam em `%LOCALAPPDATA%/VaultSyncBuild`, fora do iCloud. `dist/` contém somente entregáveis intencionais.
- Rode os testes do núcleo, a verificação do instalador e o smoke test da interface antes de declarar entrega validada.
