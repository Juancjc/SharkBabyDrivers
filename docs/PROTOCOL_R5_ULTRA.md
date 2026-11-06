# Attack Shark R5 Ultra

Fonte: `ATTACKSHARKR5.zip` da pasta Downloads. SHA-256:
`C18D4CC35E5082BF8300DF72C0B61B378E2C294EE8C9FE2E414B744ACB6384F4`.
O instalador NSIS contém Electron, `resources/app.asar`, configuração
`web/Config/env-models.json` (AppVer 1.0.0.2) e o helper `am` em `index-678780e8.js`.
Extração com 7-Zip e leitura local do ASAR; nenhum instalador ou firmware foi executado.

## Identificação

| Conexão | VID:PID |
|---|---|
| Cabo | 373E:0046 |
| Receptor 8K | 373E:0047 |

O oficial seleciona uma coleção com feature report de 64 bytes de payload.
No Windows são 65 bytes, incluindo o report ID zero. Bootloaders B046/B047 não são aceitos.
Não se deve usar o protocolo do X11 neste modelo.

## Protocolo derivado do helper oficial

Offsets abaixo são do payload WebHID (sem o byte extra de report ID do Windows).
Requests têm 64 bytes: `[0, 0, device=2, length, group, command, profile, data...]`.
O firmware pode devolver header com ou sem o primeiro zero; `hidIndex` no oficial normaliza essa diferença.
A resposta normalizada tem status A1 em 1, comando em 6 e dados de perfil em 7/8.
O driver verifica status, device, grupo e comando; respostas não correspondentes não são aceitas.
A correspondência completa do header deve ser confirmada no hardware.

| Ajuste | Grupo | Leitura | Escrita | length |
|---|---:|---:|---:|---:|
| Firmware | 0 | 81 | — | 16 |
| Perfil ativo | 0 | 85 | não usado | 1 |
| DPI X/Y | 1 | 81 | 01 | 10 / 26 |
| Estágio ativo | 1 | 82 | 02 | 2 |
| Polling | 1 | 80 | 00 | 2 |
| Debounce | 0 | 88 | 08 | 2 |
| Suspensão | 0 | 87 | 07 | 3 |
| LOD | 1 | 88 | 08 | 2 |
| Angle Snap | 1 | 84 | 04 | 2 |
| Motion Sync | 1 | 89 | 09 | 2 |
| Ripple | 1 | 8A | 0A | 2 |
| Tracking | 1 | 93 | 13 | 2 |
| Botão | 3 | 80 | 00 | 5 + tamanho da função |

Valores de DPI e suspensão são big endian. Polling: 08=125, 04=250, 02=500,
01=1000, 20=2000, 40=4000, 80=8000 Hz. LOD: 87=0,7 mm, 01=1 mm, 02=2 mm.
DPI: resposta contém quantidade em 8 e pares X/Y de 16 bits a partir de 9.
Botão: request contém perfil, número (1–5), zero, tipo de função, tamanho, dados.
Resposta contém tipo em 10, tamanho em 11 e dados em 12. Atribuições desconhecidas são preservadas.

Backup consulta somente o perfil ativo, sem trocar perfil. Guarda relatórios brutos e configuração decodificada.
Aplicar consulta o estado atual, interrompe se o perfil mudou, envia somente diferenças e relê todos os ajustes.
Falhas mantêm o rascunho. Novas atribuições de clique esquerdo são enviadas antes de remover a anterior.
Dados de macros e cores não fazem parte deste backup. Transferência de atribuições de macros é bloqueada
antes de qualquer gravação; macros já presentes e não alteradas são preservadas.

## Validação e primeiro teste físico

Testes usam emulador local, não constituem captura de tráfego de um R5 real.

1. Abra SharkBabyDrivers, conecte por cabo e aguarde o backup ORIGINAL. Compare DPI,
   polling, debounce e sensor com o programa oficial (feche-o antes de aplicar no SharkBabyDrivers).
2. Exporte o backup. Altere somente o primeiro DPI e aplique. Confirme o resultado por releitura.
3. Teste um botão lateral com atalho, sem remover o último clique esquerdo. Restaure os ajustes.
4. Repita pelo receptor 8K, incluindo suspensão/acordar e polling até 8000 Hz.
5. Se houver timeout, preserve `sharkbabydrivers.log` e pare antes de tentar comandos alternativos.

O hardware não estava disponível durante a implementação. Não há garantia de funcionamento físico
até concluir esse roteiro; nenhuma atualização de firmware é necessária ou oferecida.
