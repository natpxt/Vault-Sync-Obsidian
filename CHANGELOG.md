# Changelog

## 1.0.0 — 2026-10-15

- Primeiro lançamento principal, autorizado após o teste manual do usuário.
- Conteúdos SHA-256 idênticos encerram conflitos antigos, inclusive recuperações; artefatos de recuperação continuam preservados e localizáveis pelo log.
- Atualizar lista e abrir comparação reavaliam igualdade; cópias iguais não abrem um conflito vazio.
- Datas da comparação exibem dia e hora até segundos, sem milissegundos. A comparação interna mantém precisão integral.
- Consolida monitoramento bidirecional, regra do arquivo mais recente, interface roxa, logs, comparação Markdown, recuperação e instalador por usuário.
- README revisado com introdução, instalação por release, configuração, operação, atualização, branches e limites.
- .gitignore ampliado para estado SQLite, credenciais, certificados, logs e artefatos locais. Históricos anteriores preservados.

## 0.1.2 — candidato, 2026-10-15; aguardando teste manual e aprovação de commit

- Regra solicitada pelo usuário: maior data de modificação vence nos dois sentidos; igualdade continua por SHA-256. Conflitos comuns antigos são reavaliados.
- Empates, exclusão contra edição e recuperação explícita permanecem manuais. Revalidação inclui datas relevantes.
- Atualização da 0.1.1 permanece parada até revisar a prévia e iniciar a nova política.
- Comparador atualiza snapshots antes de abrir; duplo clique/Enter, seleção inicial e feedback de preparação/erro. Notas grandes e binários abrem metadados.
- Comparador menor, limitado à área útil com DPI; cabeçalhos e confirmações roxos em todas as janelas próprias.
- Rolagem fina arredondada, opções em interruptores e campos sem sobreposição. Ordem Escolher pasta → Criar.
- Explicação de workspace/layout incluída no aplicativo e no manual.
- Alterações mantidas sem commit, tag ou publicação.

## 0.1.1 — candidato MVP, aguardando teste manual e aprovação de commit

- Iniciar/Parar explícitos; estado ATIVO/PARADO, pastas observadas e última conferência.
- Log persistente de execução, sentido das cópias, pendências e conflitos.
- Backup avulso opcional, sem bloquear o início quando arquivos estão sendo gravados.
- Leitura parcial adia somente arquivos/subárvores afetados, sem inferir exclusões.
- Layouts `workspace*.json` locais por padrão; demais notas/configurações continuam sincronizadas.
- Conflitos da prévia visíveis antes de iniciar e reconhecimento de conteúdo idêntico após conflito comum.
- Preparação interrompida antes da publicação volta para a fila sem conflito falso; recuperação com bytes anteriores permanece explícita.
- Janela compacta, controles visíveis, tema roxo e acesso a dados/logs por associação de pastas do Windows.
- Nenhum commit, tag ou publicação desta versão sem aprovação explícita do usuário.

## 0.1.0 — 2026-10-14

- Aplicativo WPF e instalador autocontido por usuário para Windows x64.
- Seleção de raízes, verificação, prévia, backup e ativação revisada.
- Sincronização bidirecional com SHA-256, baseline SQLite, fila e backoff.
- Conflitos de edição/exclusão, comparação Markdown e combinação manual.
- Registros de operação e recuperação de interrupções.
- Integração opcional revisável de caminhos do Obsidian e Codex.
- Testes isolados, smoke test visual e benchmark reproduzível.

Piloto: não representa homologação nos três PCs ou garantia de entrega distribuída pelo iCloud.
