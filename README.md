<p align="center">
  <img src="assets/banner.png" alt="SharkBabyDrivers — mouse e teclado Attack Shark num app só" width="100%">
</p>

<p align="center">
  <a href="https://github.com/Juancjc/SharkBabyDrivers/releases/latest"><img src="https://img.shields.io/github/v/release/Juancjc/SharkBabyDrivers?color=e8323c&label=vers%C3%A3o" alt="versão"></a>
  <a href="https://github.com/Juancjc/SharkBabyDrivers/actions/workflows/ci.yml"><img src="https://github.com/Juancjc/SharkBabyDrivers/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-e8323c" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8-e8323c" alt=".NET 8">
</p>

# SharkBabyDrivers

Um app só, leve e em português (ou inglês), para configurar o **mouse Attack Shark X11** e o
**teclado Attack Shark X68 PRO HE** — substitui os dois programas oficiais.

- Barra lateral com cada periférico conectado (detecta ligar/desligar na hora).
- **Backup antes de tudo:** ao conectar, o app lê o que está salvo *dentro* do dispositivo e guarda um
  único backup **ORIGINAL** por dispositivo, sem substituí-lo nas conexões seguintes. Nada é aplicado antes disso.
  Pelo receptor 2.4G, o backup do teclado inclui Hall Effect somente do perfil ativo.
- Na aba **Salvos e backup**, salve configurações com nome, carregue, importe e exporte perfis, ou restaure e
  exporte o backup original. Os perfis salvos ficam agrupados por modelo e podem ser usados pelo cabo ou receptor.
- Grava só o que mudou, ao clicar em **Aplicar no dispositivo** (com *Descartar alterações*).

## O que dá para configurar

**Mouse X11** (cabo ou receptor 2.4G)
- 8 estágios de DPI (50–22 000) com cor de cada um e estágio em uso
- Taxa de polling 125/250/500/1000 Hz, angle snap, ripple control, debounce (4–50 ms)
- Função dos 6 botões: cliques, DPI, mídia, navegador, atalhos prontos, atalho personalizado, macro
- Iluminação (7 efeitos, cor, brilho, velocidade), sono e sono profundo
- Macros gravadas pelo teclado, com repetição; bateria em tempo real; reset de fábrica

**Teclado X68 PRO HE** (cabo USB ou receptor 2.4G, com as limitações abaixo)
- 4 perfis da memória do teclado; camadas Normal, Fn Windows e Fn Mac
- Remapeamento de qualquer tecla: tecla/combinação, mouse, mídia, energia, macro, troca de perfil
- Hall Effect por tecla ou em grupo: acionamento, soltura, Rapid Trigger (sensibilidades), zonas mortas
  superior/inferior, tipo de switch, DKS, Mod-Tap, Toggle, Snap Tap/SOCD, calibração
- Iluminação (19 efeitos com variações, cor/arco-íris, brilho, velocidade)
- Polling até 8000 Hz, debounce, detecção de SO, anti-toque acidental, estabilidade do RT, troca WASD↔setas,
  timers de sono; macros. O reset de fábrica do teclado fica desativado por segurança, inclusive pelo cabo.

> **Teclado pelo receptor 2.4G (`3151:5026`):** permite ler e aplicar configurações, incluindo iluminação e
> Hall Effect do **perfil ativo**. A troca de perfil e a calibração ficam desativadas sem fio; use o cabo para
> essas operações e para ler o Hall Effect dos quatro perfis. As matrizes de teclas dos quatro perfis podem ser
> lidas sem trocar o perfil ativo. Após aplicar pelo receptor, o app relê os dados e tenta novamente o que não
> foi confirmado. Antes de gravar, também confere o perfil ativo no teclado e interrompe a aplicação se ele
> mudou desde a leitura. A leitura é mais lenta que pelo cabo.
>
> Aperte uma tecla antes de conectar para acordar o teclado; enquanto ele dorme, o app tenta novamente a cada
> 3 segundos. **Desative o controle do teclado no SignalRGB ou feche-o**: o tráfego contínuo de iluminação pode
> fazer as respostas se perderem. O app avisa quando detecta esse tráfego.

## Baixar

Pegue a última versão em **[Releases](https://github.com/Juancjc/SharkBabyDrivers/releases/latest)**:
- `…-win-x64.zip` — leve, precisa do [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `…-win-x64-portable.zip` — não precisa instalar nada

## Versões

A versão fica em `Directory.Build.props` e segue [SemVer](https://semver.org/lang/pt-BR/) a partir dos commits
(`fix` → patch, `feat` → minor, `!`/`BREAKING CHANGE` → major). Para publicar:

```powershell
git tag v1.1.0
git push origin v1.1.0   # o workflow Release testa, compila e cria a release com os .zip
```

## Rodar a partir do código

Requer Windows 10/11 e o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/SharkBabyDrivers            # app
dotnet run --project src/SharkBabyDrivers -- --demo  # + um X68 simulado para conhecer as telas
dotnet test                                          # testes de protocolo e do simulador do teclado
```

Gerar um executável leve (depende do runtime, ~500 KB):

```powershell
dotnet publish src/SharkBabyDrivers -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Não precisa de administrador nem de driver: tudo é feito por HID padrão do Windows.
**Feche os programas oficiais** antes de usar (dois apps falando com o mesmo dispositivo se atrapalham).

## Onde ficam os dados

`%LOCALAPPDATA%\SharkBabyDrivers\`
- `backups\<dispositivo>\original.json` — único backup original por dispositivo
- `saved-profiles\<modelo>\*.sbd.json` — configurações salvas com nome na aba *Salvos e backup*
- `profiles\` — botões e macros do X11 (o mouse não deixa ler isso de volta)
- `settings.json`, `sharkbabydrivers.log`

## Estrutura

| Pasta | Conteúdo |
|---|---|
| `src/SharkBabyDrivers.Core` | HID (P/Invoke, sem dependências), protocolos X11 e X68, backups, perfis |
| `src/SharkBabyDrivers` | App WPF (.NET 8): tema escuro vermelho, PT-BR/EN |
| `tools/SharkBabyDrivers.Cli` | Diagnóstico: `list`, `dump`, `backup` (somente leitura) |
| `tests/SharkBabyDrivers.Tests` | xUnit: checksums, codecs, capturas reais do X11, ciclos completos no simulador do X68 |
| `docs/` | Protocolos documentados (engenharia reversa dos apps oficiais) |

## Aviso

Projeto independente, sem relação com a Attack Shark. Os protocolos foram obtidos por engenharia reversa
dos programas oficiais para interoperabilidade. Use por sua conta; os backups existem justamente para
poder voltar atrás.
