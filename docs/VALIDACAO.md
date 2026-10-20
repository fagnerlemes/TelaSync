# Validação e limites

## Versão 0.1.3

A validação local registrada para a versão inclui:

- 73 verificações de lógica e 36 de interface, sem falhas.
- Instalação e patch em pastas isoladas: preservação de dados, rollback, recusa de base inválida, reaplicação, downgrade e desinstalação.
- Planos de TV exclusiva, retorno à mesa, troca da principal e rotação aceitos pela validação do Windows, sem aplicação física durante esse QA.
- Recuperação de uma troca confirmada sem reversão após o prazo do processo auxiliar.

A operação física de entrar/sair do modo TV e a rotação ainda precisam ser conferidas no uso real. Uma resposta de sucesso do driver não comprova o resultado no painel. O brilho depende da aceitação de cada monitor.

## Tela preta e repouso

A tela preta manual foi observada no Blizzard por 12 segundos enquanto o usuário operava as outras telas; o botão Acender do aplicativo removeu a cobertura. Ela preserva a área de trabalho e mantém a iluminação do painel.

O repouso individual no Blizzard 27' identificado como LHCFFFF permanece incompatível: houve retorno espontâneo da imagem ou ausência de apagamento nos estados de espera/suspensão. O comando físico D6=5 exigiu o botão do monitor e não é usado pela versão atual. O programa não substitui repouso individual pelo apagamento global do Windows nem pela desativação da saída.

## Publicação

Os arquivos-fonte e binários originais de cada versão foram preservados. Cada release inclui `RELEASE.json` com identificação e hashes dos pacotes, além de `SHA256SUMS.txt`. Os diagnósticos locais completos, perfis, identificadores das conexões e caminhos pessoais não são anexados à publicação.
