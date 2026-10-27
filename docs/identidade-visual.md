# Vault Sync · guia visual Órbita

## Marca

O usuário escolheu Órbita: pedra com três facetas e duas setas em ciclo. A geometria aprovada está em `simbolos/01-orbita.svg`; `Brand.cs` reproduz as mesmas coordenadas em WPF. Não substituir o símbolo por monograma ou outra pedra.

Assinatura: **Vault Sync**, Segoe UI Semibold. A referência “Para seus vaults do Obsidian” aparece com menor destaque. Nome do produto preservado: Vault Sync - Obsidian. Projeto independente, sem afiliação à Obsidian ou à Apple.

Aplicações: vetor na interface e nas janelas; ICO com imagens PNG de 16, 24, 32, 48, 64, 128 e 256 pixels no executável; vetor renderizado em 16/32 na bandeja. O ícone da bandeja acrescenta um indicador de estado sem alterar a pedra e as setas. O texto da bandeja informa o estado e a contagem de conflitos.

## Paleta e tipografia

| Papel | Escuro | Claro |
|---|---|---|
| Fundo | #1C1C1C | #FFFFFF |
| Superfície | #232323 | #F6F6F6 |
| Superfície elevada | #282828 | #EFEFEF |
| Borda | #3F3F3F | #DADADA |
| Texto | #DADADA | #222222 |
| Texto secundário | #B3B3B3 | #5C5C5C |
| Destaque | #B49AFF | #6940BC |
| Ação principal | #7852C7 | #6940BC |
| Texto da ação principal | #FFFFFF | #FFFFFF |
| Seleção | #30283E | #EEE7F9 |
| Sucesso | #9BD5B2 | #246440 |
| Atenção | #EAC281 | #805515 |
| Erro | #F0A3A3 | #A33232 |
| Diferenças no comparador | #493B26 | #FFF0CC |

Segoe UI 13 DIP para controles e texto, 11–12 para informação auxiliar, 17–19 para títulos e estado. Consolas 12–13 no comparador e editor de combinação. Recursos locais do Windows, sem download de fontes. Medidas em DIP, respeitando a escala do sistema.

## Componentes

- Espaçamento base: 8 DIP; painéis com 14–20 DIP internos; bordas de 1 DIP; cantos de 6–10 DIP.
- Primário: preenchimento violeta e texto branco. Secundário: superfície elevada, borda neutra e texto normal. Indicação de foco por borda de destaque.
- Controles indisponíveis usam opacidade reduzida; o estado operacional continua explicado em texto.
- Navegação lateral de 174 DIP com Pastas e prévia, Conflitos, Log de execução e Backups; ⚙ Configurações fica ancorado na base. A seleção não muda ao iniciar o monitor.
- Duas pastas em cartões paralelos quando a área disponível comporta os campos; abaixo de 720 DIP no conteúdo, ficam empilhadas. Campos podem rolar horizontalmente e têm tooltip com o caminho completo. A área de configuração pode rolar verticalmente em janelas menores.
- Erros, conflitos e pendências usam texto e símbolo, além de cor. A ausência de itens depois de uma falha não significa igualdade.
- × encerra o monitor. Minimizar para bandeja mantém a execução.

## Temas e preferências

Padrão **Sistema**, com leitura de AppsUseLightTheme do Windows e atualização após o evento de preferência do sistema. **Claro** e **Escuro** fixam a escolha. A preferência é salva atomicamente em `%LOCALAPPDATA%/VaultSync/ui-settings.json`, separada da configuração de sincronização. Preferência ausente ou inválida usa Sistema. A troca não reinicia o monitor e atualiza janelas abertas, documentos e bandeja.

Os controles de seleção de pasta são os diálogos nativos do Windows; sua aparência é controlada pelo sistema operacional. A revisão externa mantém confirmação e prévia, e não é executada automaticamente pela mudança visual.

## Idiomas e autoria

Configurações oferece Português (Brasil), English e Español. A escolha é aplicada em janelas abertas sem reiniciar o monitor e persiste junto com o tema em `ui-settings.json`. Configurações de versões anteriores continuam válidas e usam português quando não há idioma salvo. Conteúdo de notas e nomes de arquivos não são traduzidos. Diagnósticos nativos podem seguir o idioma do Windows.

O rodapé permanente identifica “Desenvolvido por Natan Peixoto Maciel” e oferece acesso ao repositório `https://github.com/natpxt/Vault-Sync-Obsidian` no navegador padrão. Nenhum acesso ao GitHub acontece automaticamente.
