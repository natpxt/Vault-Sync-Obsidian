# Vault Sync - Obsidian

Quando este checkout estiver explicitamente vinculado ao MegaBrain, recupere o mestre do vault e a skill canônica memory-sync; não copie essas instruções para o repositório. Em clones sem esse vínculo, não procure nem crie memória MegaBrain.

Nome do produto: **Vault Sync - Obsidian**. Identificadores de código: `VaultSync`. Não é produto oficial da Obsidian ou Apple.

- Código em `src/`, testes em `tests/`, documentação em `docs/`, automação em `scripts/`.
- Fluxo obrigatório: desenvolver uma versão → usuário testar manualmente → usuário aprovar explicitamente o commit → fazer commit. Pedido de implementação, testes automatizados aprovados ou ausência de objeção não autorizam commit. Sem essa aprovação, manter alterações sem commit; não criar tags/releases nem fazer push por iniciativa própria. Regra confirmada pelo usuário em 2026-10-14; os dois commits anteriores não autorizam novos commits nem reescrita do histórico.
- Nunca execute sincronização, migração ou testes destrutivos sobre o vault real sem prévia revisada. Use fixtures isoladas.
- Invariantes: igualdade por SHA-256; estado SQLite fora das raízes; falha de leitura não é exclusão; conflitos preservam versões; não seguir links/junções externos; não carregar recursos remotos no preview.
- Não publicar GitHub, telemetria, releases ou dados pessoais por iniciativa própria. Revisar conteúdo e licença antes de publicação.
- Builds e dependências ficam em `%LOCALAPPDATA%/VaultSyncBuild`, fora do iCloud. `dist/` contém somente entregáveis intencionais.
- Rode os testes do núcleo, a verificação do instalador e o smoke test da interface antes de declarar entrega validada.
- Versão 1.0.0: o usuário testou o MVP e autorizou explicitamente em 2026-10-15 esta correção, commit em português, publicação na `main`, criação de `dev` e release. Isso não concede aprovação antecipada a próximos commits. Preservar os históricos local e remoto sem push forçado.
