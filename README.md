<p align="center">
  <img src="assets/banner.png" alt="SharkBabyDrivers - tubar�o beb� de chupeta vermelha; mouse e teclado Attack Shark num app s�" width="100%">
</p>

<p align="center">
  <a href="https://github.com/Juancjc/SharkBabyDrivers/releases/latest"><img src="https://img.shields.io/github/v/release/Juancjc/SharkBabyDrivers?color=e8323c&label=vers%C3%A3o" alt="vers�o"></a>
  <a href="https://github.com/Juancjc/SharkBabyDrivers/actions/workflows/ci.yml"><img src="https://github.com/Juancjc/SharkBabyDrivers/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-e8323c" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8-e8323c" alt=".NET 8">
</p>

# SharkBabyDrivers

Um app s�, leve e em portugu�s (ou ingl�s), para configurar o **mouses Attack Shark X11 e R5 Ultra** e os
**teclados Attack Shark X68 HE e X68 PRO HE**.

| Modelo | Conex�o | Revis�es do teclado |
|---|---|---|
| Attack Shark R5 Ultra | Cabo USB (`373E:0046`) ou receptor 8K (`373E:0047`) | - |
| Attack Shark X11 | Cabo USB ou receptor 2.4G | - |
| Attack Shark X68 HE | Cabo USB (`3151:502D`) | 2270, 2472, 2902 |
| Attack Shark X68 PRO HE | Cabo USB ou receptor 2.4G | 2370, 2901 |

O X68 HE foi adicionado a partir do programa oficial de `ATTACKSHARKX68HE.zip`, incluindo as matrizes
Normal, Fn Windows e Fn Mac de cada revis�o. A integra��o foi validada no simulador; falta validar em um
X68 HE f�sico. O suporte existente do PRO HE continua dispon�vel.

- Barra lateral com cada perif�rico conectado (detecta ligar/desligar na hora).
- **Backup antes de tudo:** ao conectar, o app l� o que est� salvo *dentro* do dispositivo e guarda um
  �nico backup **ORIGINAL** por dispositivo, sem substitu�-lo nas conex�es seguintes. Nada � aplicado antes disso.
  Pelo receptor 2.4G, no X68 HE e no X68 PRO HE V2 (id 2901), o backup do teclado inclui Hall Effect somente
  do perfil ativo. A leitura desse modelo usa apenas consultas, sem trocar o perfil.
- Na aba **Salvos e backup**, salve configura��es com nome, carregue, importe e exporte perfis, ou restaure e
  exporte o backup original. Os perfis salvos ficam agrupados por modelo e podem ser usados pelo cabo ou receptor.
- As altera��es v�o ao dispositivo ao clicar em **Aplicar no dispositivo** (com *Descartar altera��es*).
  No X11, aplicar reenvia os bot�es e macros j� configurados pelo aplicativo, inclusive quando outro programa
  alterou os laterais. Carregar um perfil e aplicar reenvia a configura��o completa. O aplicativo l� o estado
  atual antes de aplicar e confere os valores que o mouse permite consultar depois.

## O que d� para configurar

**Mouse X11** (cabo ou receptor 2.4G)
- Painel com desenho clic�vel do mouse, bot�es numerados e ajustes expans�veis
- Modal para escolher uma tecla ou combina��o (ex.: A, F5, Ctrl+A), com pr�via e confirma��o
- Carregue ou importe um perfil no pr�prio painel, edite os ajustes e salve no mesmo perfil ou em uma nova c�pia
- 8 est�gios de DPI (50-22 000) com cor de cada um e est�gio em uso
- Taxa de polling 125/250/500/1000 Hz, angle snap, ripple control, debounce (4-50 ms)
- Fun��o dos 6 bot�es: cliques, DPI, m�dia, navegador, atalhos prontos, atalho personalizado, macro
- Ilumina��o (7 efeitos, cor, brilho, velocidade), sono e sono profundo
- Macros gravadas pelo teclado, com repeti��o; bateria em tempo real; reset de f�brica

**Mouse R5 Ultra** (adicionado na vers�o 1.7.0)
- Integra��o nativa baseada no pacote `ATTACKSHARKR5.zip` (software oficial 1.0.0.2).
- DPI X/Y dos est�gios existentes (50-42000), est�gio ativo, polling at� 1000 Hz pelo cabo e 8000 Hz pelo receptor.
- Debounce, suspens�o, LOD, Angle Snap, Motion Sync, Ripple Control e Tracking Mode.
- Cinco bot�es: fun��es de mouse, alternar DPI e atalhos de teclado personalizados.
- Backup original, perfis salvos, importa��o/exporta��o e confirma��o por releitura ap�s aplicar.
- O backup cobre os ajustes e atribui��es do **perfil ativo**; n�o inclui os dados de macros nem cores DPI.
  Macros existentes permanecem no mouse; criar ou transferir macros e editar cores DPI ainda n�o est� dispon�vel.
- N�o troca perfis, n�o reseta e n�o atualiza firmware. Para editar outro perfil, selecione-o no mouse e releia.
- **Validado no simulador; valida��o em R5 Ultra f�sico pendente.** O modo `--preview` inclui um R5 simulado.
  Veja [o protocolo e o roteiro de teste](docs/PROTOCOL_R5_ULTRA.md).

**Teclados X68 HE** (cabo USB) **e X68 PRO HE** (cabo USB ou receptor 2.4G, com as limita��es abaixo)
- 4 perfis da mem�ria do teclado; camadas Normal, Fn Windows e Fn Mac
- Remapeamento de qualquer tecla: tecla/combina��o, mouse, m�dia, energia, macro, troca de perfil
- Hall Effect por tecla ou em grupo: acionamento, soltura, Rapid Trigger (sensibilidades), zonas mortas
  superior/inferior, tipo de switch, DKS, Mod-Tap, Toggle, Snap Tap/SOCD, calibra��o
- Ilumina��o (19 efeitos com varia��es, cor/arco-�ris, brilho, velocidade)
- Polling at� 8000 Hz, debounce, detec��o de SO, anti-toque acidental, estabilidade do RT, troca WASDsetas,
  macros; timers de sono no PRO HE. O reset de f�brica do teclado fica desativado por seguran�a, inclusive pelo cabo.

> **X68 HE:** a troca de perfil pelo aplicativo permanece bloqueada nas tr�s revis�es. O backup usa somente
> consultas e l� Hall Effect do perfil ativo. Para configurar outro perfil, selecione-o no teclado e fa�a
> uma nova leitura no app. Os ajustes de suspens�o sem fio ficam ocultos nesse modelo USB.

> **Teclado pelo receptor 2.4G (`3151:5026`):** permite ler e aplicar configura��es, incluindo ilumina��o e
> Hall Effect do **perfil ativo**. A troca de perfil e a calibra��o ficam desativadas sem fio. No **X68 V2
> (id 2901), a ativa��o de perfil pelo app tamb�m fica bloqueada pelo cabo**: durante uma leitura autom�tica,
> houve perda da digita��o e dos LEDs perto de uma troca de perfil. Esse � o principal ind�cio nos registros,
> sem confirmar sozinho a causa. Para configurar HE de outro perfil, troque-o no pr�prio teclado e fa�a uma
> nova leitura no app. As matrizes de teclas dos quatro perfis podem ser lidas sem trocar o perfil ativo.
> Ap�s aplicar pelo receptor, o app rel� os dados e tenta novamente o que n�o
> foi confirmado. Antes de gravar, tamb�m confere o perfil ativo no teclado e interrompe a aplica��o se ele
> mudou desde a leitura. A leitura � mais lenta que pelo cabo.
>
> Aperte uma tecla antes de conectar para acordar o teclado; enquanto ele dorme, o app tenta novamente a cada
> 3 segundos.

## SignalRGB e �cone na bandeja

Escolha **quem controla a ilumina��o do teclado** na aba *Ilumina��o* do X68 ou no �cone da bandeja:

- **SignalRGB:** o teclado entra no modo "cor da tela" e passa a seguir o SignalRGB (as fans e o resto continuam
  no SignalRGB normalmente). As cores chegam pelo plugin do SharkBabyDrivers e � o pr�prio SharkBabyDrivers quem
  as envia ao teclado, entre as suas leituras e grava��es. Durante um backup, *Aplicar* ou carregar perfil, as
  cores pausam por alguns segundos e voltam sozinhas. Enquanto o SignalRGB estiver no controle, o app n�o grava
  ilumina��o (nem ao aplicar, carregar perfis ou restaurar backups).
- **SharkBabyDrivers:** as cores do SignalRGB s�o ignoradas - nada do SignalRGB chega ao teclado - e o teclado
  volta � ilumina��o que tinha antes.

Na primeira vez, o app oferece instalar o plugin em `Documentos\WhirlwindFX\Plugins` (ele substitui o plugin
padr�o do X68 no SignalRGB); depois **reinicie o SignalRGB** e deixe o teclado ativado nele. Vale para o cabo e
o receptor: com o cabo conectado o app usa s� o cabo; ao desconectar, volta para o receptor.

**Apagar a luz para poupar bateria (receptor):** o ajuste *Apagar luz ap�s (sem fio)* (aba Desempenho) vale nos
dois modos. Com o SharkBabyDrivers no controle, o pr�prio teclado apaga; com o SignalRGB, o app manda preto depois
desse tempo sem tecla **neste** teclado e para de enviar (o r�dio fica quieto); a primeira tecla traz as cores de
volta. O app s� percebe *quando* uma tecla do X68 � apertada (Raw Input do Windows), nunca *qual*. `0` = nunca.

**Uma cor por vez ao vivo:** com o SignalRGB, o teclado inteiro mostra uma cor s�. O firmware original n�o tem comando
de cor por tecla em tempo real (o app oficial tamb�m n�o); cor por tecla existe apenas como "imagem de LED" gravada na
mem�ria - mandar isso a cada quadro traria de volta as piscadas e o desgaste. No plugin, *Live Color* escolhe como
reduzir o efeito a uma cor: **destaque** (padr�o - mostra o que foge do fundo, como a onda de um efeito de onda),
centro do teclado, m�dia das teclas ou cor dominante.

**Por que n�o d� onda tecla por tecla ao vivo:** o firmware original do X68 n�o tem comando para isso - o app oficial
tamb�m n�o faz. O comando de "anima��o por tecla" que existe no c�digo oficial � de outra fam�lia de teclados e �
ignorado pelo firmware desta plataforma (conferido desmontando um firmware RY5088), e a mem�ria de imagens s� comporta
as 5 imagens fixas. Cor por tecla ao vivo exigiria trocar o firmware, o que este app n�o faz. Alternativas poss�veis:
o efeito *Onda* do pr�prio teclado (onda real, cores do teclado), barras por coluna no modo m�sica (ao vivo, uma cor,
sem fundo - ainda n�o implementado) e a imagem fixa abaixo. Detalhes em [`docs/SIGNALRGB.md`](docs/SIGNALRGB.md).

**Imagem fixa do SignalRGB (v�rias cores):** em *Ilumina��o  Imagem fixa do SignalRGB*, o app grava **uma vez**, numa
das 5 imagens do teclado, a cor que o SignalRGB est� desenhando em cada tecla naquele momento, e liga o efeito
"Imagem". Fica parada (n�o anima), vale no cabo e no receptor, e a sincroniza��o ao vivo � desligada at� voc� escolher
SignalRGB de novo. O plugin 1.1 (com o mapa das teclas) precisa estar instalado e o SignalRGB reiniciado.

**Por que piscava:** o plugin padr�o do SignalRGB envia o comando de ilumina��o `0x07` (modo, brilho e cor) a cada
quadro. O teclado grava esse comando na mem�ria e reinicia o efeito a cada vez - ~30 grava��es por segundo. O
plugin novo usa o comando de "cor da tela" `0x0E`, que n�o � gravado na mem�ria e n�o reinicia nada.
Detalhes e medi��es em [`docs/SIGNALRGB.md`](docs/SIGNALRGB.md).

**Bandeja:** fechar a janela deixa o app no �cone da bandeja (perto do rel�gio), que mostra quem controla a
ilumina��o, a conex�o (cabo/receptor) e se as cores do SignalRGB est�o chegando; o ponto no �cone fica com a
cor atual. Pelo menu: alternar o controle, **Iniciar com o Windows** (abre s� na bandeja, ~25 MB e quase nada de
CPU, sem ler o teclado inteiro at� voc� abrir a janela), abrir ou sair.

> **Teclas vermelhas filtram a cor:** azul e verde aparecem fracos ou escurecidos sob keycaps vermelhas. Para
> conferir a cor real, olhe um switch sem tecla.

## Baixar

Pegue a �ltima vers�o em **[Releases](https://github.com/Juancjc/SharkBabyDrivers/releases/latest)**:
- `.-win-x64.zip` - leve, precisa do [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `.-win-x64-portable.zip` - n�o precisa instalar nada

## Vers�es

A vers�o fica em `Directory.Build.props` e segue [SemVer](https://semver.org/lang/pt-BR/) a partir dos commits
(`fix`  patch, `feat`  minor, `!`/`BREAKING CHANGE`  major). Para publicar:

```powershell
git tag v1.1.0
git push origin v1.1.0   # o workflow Release testa, compila e cria a release com os .zip
```

## Rodar a partir do c�digo

Requer Windows 10/11 e o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Para testar **sem instalar**, d� dois cliques em `Testar.cmd` na pasta do projeto. Ele compila a vers�o
atual e abre o app diretamente dessa pasta. Feche a janela do app antes de recompilar. Apenas uma c�pia
do SharkBabyDrivers pode ficar aberta por vez, para evitar disputa pelo dispositivo.

```powershell
dotnet run --project src/SharkBabyDrivers            # app
dotnet run --project src/SharkBabyDrivers -- --demo  # + um X68 simulado para conhecer as telas
dotnet run --project src/SharkBabyDrivers -- --preview # pr�via isolada de mouse + teclado simulados, sem acessar HID/SignalRGB
dotnet test                                          # testes de protocolo e do simulador do teclado
```

Instalar ou atualizar no pr�prio PC (por usu�rio, sem administrador), a partir do c�digo:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Install-Local.ps1   # publica, encerra a c�pia aberta, instala e abre
```

Instala em `%LOCALAPPDATA%\Programs\SharkBabyDrivers` com atalho no Menu Iniciar e desinstalador em
*Aplicativos instalados*. N�o mexe nos dados (`%LOCALAPPDATA%\SharkBabyDrivers`), no SignalRGB nem na escolha de
*Iniciar com o Windows* (s� atualiza o caminho se j� estiver ligado).

Gerar um execut�vel leve (depende do runtime, ~500 KB):

```powershell
dotnet publish src/SharkBabyDrivers -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

N�o precisa de administrador nem de driver: tudo � feito por HID padr�o do Windows.
Evite configurar o mesmo dispositivo simultaneamente em outro programa. Se houver disputa, pause apenas
o controle desse dispositivo no outro app durante a configura��o.

## Onde ficam os dados

`%LOCALAPPDATA%\SharkBabyDrivers\`
- `backups\<dispositivo>\original.json` - �nico backup original por dispositivo
- `saved-profiles\<modelo>\*.sbd.json` - configura��es salvas com nome na aba *Salvos e backup*
- `profiles\` - bot�es e macros do X11 (o mouse n�o deixa ler isso de volta)
- `settings.json` (idioma, quem controla a ilumina��o), `sharkbabydrivers.log`
- `lighting.log` - uma linha por mudan�a de estado da ponte com o SignalRGB (�til para pedir ajuda)

## Estrutura

| Pasta | Conte�do |
|---|---|
| `src/SharkBabyDrivers.Core` | HID (P/Invoke, sem depend�ncias), protocolos X11 e X68, backups, perfis |
| `src/SharkBabyDrivers` | App WPF (.NET 8): tema escuro vermelho, PT-BR/EN |
| `integrations/signalrgb` | Plugin do SignalRGB (embutido no app e instalado por ele) |
| `tools/Install-Local.ps1` | Instalador local por usu�rio (publica, instala, atalhos e desinstalador) |
| `tools/SharkBabyDrivers.Cli` | Diagn�stico: `list`, `dump`, `backup` (somente leitura) |
| `tests/SharkBabyDrivers.Tests` | xUnit: checksums, codecs, capturas reais do X11, ciclos completos no simulador do X68 |
| `docs/` | Protocolos documentados (engenharia reversa dos apps oficiais) |

## Aviso

Projeto independente, sem rela��o com a Attack Shark. Os protocolos foram obtidos por engenharia reversa
dos programas oficiais para interoperabilidade. Use por sua conta; os backups existem justamente para
poder voltar atr�s.
# Recupera��o de configura��es

Os bot�es **Resetar dispositivo** e **Resetar perfil atual** ficam no topo da p�gina do perif�rico; **Resetar todos os dispositivos** fica na barra lateral. A restaura��o exige que os dispositivos estejam prontos e tenham o backup original. Cada opera��o pede confirma��o e salva o estado lido e o perfil do editor em arquivos `before-reset-*` na pasta de backups, antes de gravar.

O reset usa a configura��o da primeira conex�o, n�o reinicia fisicamente o perif�rico e n�o � um reset de f�brica. No teclado, o reset do perfil restaura apenas as matrizes de teclas e as tabelas HE dispon�veis do perfil fisicamente ativo, preservando os outros perfis e os ajustes globais. No R5, restaura as configura��es no perfil ativo. No X11, restaura as configura��es leg�veis e os bot�es padr�o, limpando as personaliza��es locais. Os perfis nomeados salvos s�o mantidos. Se o backup original j� contiver o problema, essa restaura��o n�o o corrigir�.

### Atualiza��es pelo programa

O app consulta as releases est�veis de `Juancjc/SharkBabyDrivers` somente uma vez ao iniciar, sem consultas peri�dicas em segundo plano.
Quando h� uma vers�o nova, um aviso **? Atualiza��o dispon�vel** aparece no topo. Clique nele para atualizar; a instala��o pede confirma��o. O aviso usa o resultado j� consultado, sem repetir a busca. Tamb�m � poss�vel consultar manualmente pelo menu da bandeja. O download
verifica o SHA-256 publicado pelo GitHub e usa o pacote port�til x64. O programa fecha, substitui
seu execut�vel e abre novamente mostrando as notas da vers�o, uma �nica vez. Perfis e backups
permanecem na pasta de dados. Altera��es n�o salvas precisam ser confirmadas antes de sair.
Se a substitui��o falhar, o atualizador tenta restaurar o execut�vel anterior e informa o erro.
A atualiza��o interna requer a distribui��o instalada/port�til; sa�das de desenvolvimento com DLLs
separadas devem ser atualizadas pelo build. Sem conex�o, a checagem autom�tica apenas registra o erro.
As notas exibidas s�o o texto da release: descreva corre��es e novidades ao publicar cada vers�o.
