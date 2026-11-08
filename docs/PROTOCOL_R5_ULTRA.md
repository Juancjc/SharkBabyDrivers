# Attack Shark R5 Ultra

Fonte: `ATTACKSHARKR5.zip` da pasta Downloads. SHA-256:
`C18D4CC35E5082BF8300DF72C0B61B378E2C294EE8C9FE2E414B744ACB6384F4`.
O instalador NSIS cont�m Electron, `resources/app.asar`, configura��o
`web/Config/env-models.json` (AppVer 1.0.0.2) e o helper `am` em `index-678780e8.js`.
Extra��o com 7-Zip e leitura local do ASAR; nenhum instalador ou firmware foi executado.

## Identifica��o

| Conex�o | VID:PID |
|---|---|
| Cabo | 373E:0046 |
| Receptor 8K | 373E:0047 |

O oficial seleciona uma cole��o com feature report de 64 bytes de payload.
No Windows s�o 65 bytes, incluindo o report ID zero. Bootloaders B046/B047 n�o s�o aceitos.
N�o se deve usar o protocolo do X11 neste modelo.

## Protocolo derivado do helper oficial

Offsets abaixo s�o do payload WebHID (sem o byte extra de report ID do Windows).
Requests t�m 64 bytes: `[0, 0, device=2, length, group, command, profile, data...]`.
O firmware pode devolver header com ou sem o primeiro zero; `hidIndex` no oficial normaliza essa diferen�a.
A resposta normalizada tem status A1 em 1, comando em 6 e dados de perfil em 7/8.
O driver verifica status, device, grupo e comando; respostas n�o correspondentes n�o s�o aceitas.
A correspond�ncia completa do header deve ser confirmada no hardware.

| Ajuste | Grupo | Leitura | Escrita | length |
|---|---:|---:|---:|---:|
| Firmware | 0 | 81 | - | 16 |
| Perfil ativo | 0 | 85 | n�o usado | 1 |
| DPI X/Y | 1 | 81 | 01 | 10 / 26 |
| Est�gio ativo | 1 | 82 | 02 | 2 |
| Polling | 1 | 80 | 00 | 2 |
| Debounce | 0 | 88 | 08 | 2 |
| Suspens�o | 0 | 87 | 07 | 3 |
| LOD | 1 | 88 | 08 | 2 |
| Angle Snap | 1 | 84 | 04 | 2 |
| Motion Sync | 1 | 89 | 09 | 2 |
| Ripple | 1 | 8A | 0A | 2 |
| Tracking | 1 | 93 | 13 | 2 |
| Bot�o | 3 | 80 | 00 | 5 + tamanho da fun��o |

Valores de DPI e suspens�o s�o big endian. Polling: 08=125, 04=250, 02=500,
01=1000, 20=2000, 40=4000, 80=8000 Hz. LOD: 87=0,7 mm, 01=1 mm, 02=2 mm.
DPI: resposta cont�m quantidade em 8 e pares X/Y de 16 bits a partir de 9.
Bot�o: request cont�m perfil, n�mero (1-5), zero, tipo de fun��o, tamanho, dados.
Resposta cont�m tipo em 10, tamanho em 11 e dados em 12. Atribui��es desconhecidas s�o preservadas.

Backup consulta somente o perfil ativo, sem trocar perfil. Guarda relat�rios brutos e configura��o decodificada.
Aplicar consulta o estado atual, interrompe se o perfil mudou, envia somente diferen�as e rel� todos os ajustes.
Falhas mant�m o rascunho. Novas atribui��es de clique esquerdo s�o enviadas antes de remover a anterior.
Dados de macros e cores n�o fazem parte deste backup. Transfer�ncia de atribui��es de macros � bloqueada
antes de qualquer grava��o; macros j� presentes e n�o alteradas s�o preservadas.

## Valida��o e primeiro teste f�sico

Testes usam emulador local, n�o constituem captura de tr�fego de um R5 real.

1. Abra SharkBabyDrivers, conecte por cabo e aguarde o backup ORIGINAL. Compare DPI,
   polling, debounce e sensor com o programa oficial (feche-o antes de aplicar no SharkBabyDrivers).
2. Exporte o backup. Altere somente o primeiro DPI e aplique. Confirme o resultado por releitura.
3. Teste um bot�o lateral com atalho, sem remover o �ltimo clique esquerdo. Restaure os ajustes.
4. Repita pelo receptor 8K, incluindo suspens�o/acordar e polling at� 8000 Hz.
5. Se houver timeout, preserve `sharkbabydrivers.log` e pare antes de tentar comandos alternativos.

O hardware n�o estava dispon�vel durante a implementa��o. N�o h� garantia de funcionamento f�sico
at� concluir esse roteiro; nenhuma atualiza��o de firmware � necess�ria ou oferecida.
