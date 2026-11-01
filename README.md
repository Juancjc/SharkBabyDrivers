# SharkBabyDrivers

Um app só, leve e em português (ou inglês), para configurar o **mouse Attack Shark X11** e o
**teclado Attack Shark X68 PRO HE** — substitui os dois programas oficiais.

- Barra lateral com cada periférico conectado (detecta ligar/desligar na hora).
- **Backup antes de tudo:** ao conectar, o app lê o que está salvo *dentro* do dispositivo e guarda um
  backup. O primeiro fica marcado como **ORIGINAL** e nunca é apagado. Nada é gravado antes disso.
  Restaurar, exportar e importar backups pela aba *Backup*.
- Grava só o que mudou, ao clicar em **Aplicar no dispositivo** (com *Descartar alterações*).

## O que dá para configurar

**Mouse X11** (cabo ou receptor 2.4G)
- 8 estágios de DPI (50–22 000) com cor de cada um e estágio em uso
- Taxa de polling 125/250/500/1000 Hz, angle snap, ripple control, debounce (4–50 ms)
- Função dos 6 botões: cliques, DPI, mídia, navegador, atalhos prontos, atalho personalizado, macro
- Iluminação (7 efeitos, cor, brilho, velocidade), sono e sono profundo
- Macros gravadas pelo teclado, com repetição; bateria em tempo real; reset de fábrica

**Teclado X68 PRO HE** (pelo cabo USB)
- 4 perfis da memória do teclado; camadas Normal, Fn Windows e Fn Mac
- Remapeamento de qualquer tecla: tecla/combinação, mouse, mídia, energia, macro, troca de perfil
- Hall Effect por tecla ou em grupo: acionamento, soltura, Rapid Trigger (sensibilidades), zonas mortas
  superior/inferior, tipo de switch, DKS, Mod-Tap, Toggle, Snap Tap/SOCD, calibração
- Iluminação (19 efeitos com variações, cor/arco-íris, brilho, velocidade)
- Polling até 8000 Hz, debounce, detecção de SO, anti-toque acidental, estabilidade do RT, troca WASD↔setas,
  timers de sono; macros; reset de fábrica

> **Teclado pelo receptor 2.4G:** aparece na barra lateral, mas a configuração é feita **pelo cabo USB-C**.
> O protocolo do receptor não está confirmado e o app não arrisca o teclado. O que for gravado pelo cabo
> fica salvo no teclado e vale também no modo sem fio.

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
- `backups\<dispositivo>\original.json` e backups com data
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
