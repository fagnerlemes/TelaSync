# TelaSync

Gerenciador de monitores para Windows. **By Fagner Lemes.**

Organize suas telas, preserve a posição ao reativá-las e alterne entre a mesa e a TV pelo aplicativo.

## Download

**Versão atual: [0.1.3](https://github.com/fagnerlemes/TelaSync/releases/tag/v0.1.3)** · Windows 10/11 x64 · .NET Framework 4.8

- [Instalador completo](https://github.com/fagnerlemes/TelaSync/releases/download/v0.1.3/TelaSync-0.1.3-Setup.exe): primeira instalação ou reparo.
- [Patch 0.1.2 → 0.1.3](https://github.com/fagnerlemes/TelaSync/releases/download/v0.1.3/TelaSync-0.1.3-Patch.exe): atualiza a instalação existente, preservando perfis e preferências.
- [Versão portátil](https://github.com/fagnerlemes/TelaSync/releases/download/v0.1.3/TelaSync-0.1.3-portatil.zip).
- [Todas as versões](https://github.com/fagnerlemes/TelaSync/releases), incluindo 0.1.0, 0.1.1 e 0.1.2.

Feche o TelaSync pelo menu **Sair** na bandeja antes de atualizar. O patch exige a versão anterior; para atualizar a partir de uma versão mais antiga, use os patches em sequência ou o instalador completo atual. A instalação padrão é por usuário, sem administrador, em `%LOCALAPPDATA%\Programs\TelaSync`.

## Recursos

- Mapa com detalhes, arraste de monitores, coordenadas X/Y e alinhamento.
- Memória de posição e orientação ao desativar e reativar telas.
- Rotação de 90° nos dois sentidos e escolha da tela principal.
- **Jogar na TV**: TV como única tela ativa e principal; **Voltar ao normal** recupera a disposição anterior.
- **Tela preta** individual, removida pelo aplicativo ou pelo atalho, sem desconectar o monitor.
- Controle de brilho e repouso individual por DDC/CI, quando aceitos pelo monitor.
- Perfis, atalhos, temas claro/escuro, bandeja e início com o Windows opcional.
- Memorização da escolha ao fechar pelo X, com opção para voltar a perguntar nas Preferências.

## Limites conhecidos

**Tela preta mantém a iluminação do painel ligada.** É uma cobertura visual, diferente de repouso e de economia de energia. Mouse e teclado nas outras telas não a removem; use **Acender**, **Remover tela preta** ou `Ctrl+Alt+L`.

Brilho e repouso dependem de DDC/CI, do monitor e da conexão. O repouso está bloqueado no Blizzard 27' identificado como LHCFFFF devido a retorno espontâneo da imagem. O estado de desligamento físico D6=5 não é usado na versão atual. TVs podem não oferecer controle individual de energia ou brilho.

Organização exige desktop estendido. A versão atual não edita resolução/Hz, não restaura posições de janelas e não oferece automação por horário. Alterações temporárias de disposição pedem confirmação; o modo TV da 0.1.3 é salvo imediatamente e só retorna pelo comando do aplicativo.

A validação local inclui testes de lógica, interface e instaladores. Aceitação de um plano pelo driver não comprova a troca física dos monitores: veja [validação e limites](docs/VALIDACAO.md).

## Código e compilação

- [Guia completo do aplicativo](Tela/LEIA-ME.txt)
- [Compilar e gerar instalador/patch](docs/COMPILAR.md)
- [Histórico de versões](CHANGELOG.md)

Os fontes estão em `Tela/`. O aplicativo usa C# e WPF com .NET Framework 4.8; não exige Python para funcionar. Os dados locais ficam em `%LOCALAPPDATA%\TelaSync`. A aplicação não acessa a rede. Os executáveis atuais não têm assinatura digital de certificado.

O histórico das versões 0.1.0 a 0.1.3 foi importado dos arquivos-fonte locais preservados. As tags correspondem ao código de cada versão; as datas dos commits de importação indicam a publicação no GitHub, não as datas originais de desenvolvimento.
