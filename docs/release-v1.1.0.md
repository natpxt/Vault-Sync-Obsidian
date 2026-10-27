# Vault Sync 1.1.0 · Identidade Órbita

Esta versão traz a identidade visual Órbita e reorganiza a interface para tornar o estado da sincronização e as ações de revisão mais claros.

## Novidades

- Logo próprio no aplicativo, instalador, atalhos e bandeja.
- Navegação lateral com Pastas e prévia, Conflitos, Log de execução e Backups; Configurações fica na base da lateral.
- Temas Sistema, Claro e Escuro, com troca imediata e preferência salva.
- Interface em português, inglês e espanhol, alterável sem reiniciar o monitor.
- Crédito permanente “Desenvolvido por Natan Peixoto Maciel” e acesso ao GitHub no rodapé.
- Grupos de configurações com mais espaçamento e separadores.
- Comparação de conflitos, preview Markdown, diálogos e instalador com a mesma linguagem visual.

## Correções

- Uma segunda abertura restaura a janela existente, inclusive quando oculta ou minimizada, sem depender do ícone da bandeja.
- O ícone é registrado novamente ao reabrir a janela e após o Explorer reiniciar.
- Estados vazios não sobrepõem os cabeçalhos da tabela; a prévia tem rolagem em janelas menores.
- Conflitos exibe apenas seus comandos de revisão e Parar quando o monitor está ativo.
- Contornos dos botões distinguem hover e foco de teclado.
- Campos vazios apresentam orientação clara antes de iniciar uma verificação.

## Instalação e atualização

Baixe `VaultSync-Obsidian-1.1.0-win-x64-Setup.exe` e execute. O runtime está incluído; não exige administrador ou SDK.

Para atualizar, encerre a versão anterior pelo botão × ou pela bandeja antes de executar o instalador. Os vaults e o estado local são preservados. Não execute duas versões sincronizando o mesmo par de pastas.

O arquivo `SHA256SUMS.txt` acompanha o instalador para conferência de integridade.

## Validação

Testes do núcleo, smoke WPF com fixtures isoladas e verificação isolada do instalador. O smoke cobre os três idiomas, os dois temas, navegação, cópia nos dois sentidos, pausa/retomada, resolução de conflito e reabertura com um segundo processo real.

As regras de sincronização permanecem iguais: igualdade por SHA-256; para conteúdos diferentes, prevalece a modificação mais recente; empates e exclusão contra edição exigem revisão. O aplicativo confirma a cópia entre pastas deste PC; a entrega aos demais dispositivos depende do iCloud.

Projeto independente, sem afiliação à Obsidian ou à Apple. Sem telemetria do aplicativo. Os avisos de terceiros acompanham a instalação; não foi adicionada licença aberta ao código nesta versão.
