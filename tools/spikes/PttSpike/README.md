# PTT-Test (Spikes S1 + S2)

Wegwerf-Prototyp aus Phase 0. Er klärt, ob Bloom Roblox gezielt stummschalten kann, ohne das ganze Mikrofon zu muten.

| Spike | Frage |
| --- | --- |
| S1 | Bekommt Roblox Stille, wenn Bloom nur Roblox' Aufnahme-Session stummschaltet? Merkt sich Windows diesen Mute über einen Roblox-Neustart? |
| S2 | Kommen Tasten- und Maus-Ereignisse bei Bloom an, während Roblox im Vordergrund ist? |

## Download

`bloom-ptt-test.exe` aus dem Pre-Release **ptt-test** auf GitHub oder als Artefakt des Workflows „Spikes“.
Die Datei ist (noch) nicht signiert, Windows SmartScreen fragt deshalb nach: „Weitere Informationen“ → „Trotzdem ausführen“.

## Test 1 – Stummschalten wirkt? (ca. 5 Minuten)

1. Roblox starten und einem Spiel mit Voice Chat beitreten. Roblox-Mikro im Spiel **eingeschaltet** lassen.
2. `bloom-ptt-test.exe` starten (nicht als Administrator).
3. Push-to-Talk-Taste drücken (z. B. V oder Maus 4).
4. Zu Roblox wechseln. Oben im Test-Fenster muss „Voice-Session auf …“ stehen.
5. Durchgehend sprechen und die PTT-Taste immer wieder halten und loslassen. Dabei auf das eigene Mikro-Symbol in Roblox achten:
   - Taste losgelassen (STUMM): Symbol bewegt sich **nicht**.
   - Taste gehalten (SENDET): Symbol bewegt sich.
6. Alternativ im Test-Fenster `A` drücken: 30 s lang wechselt Bloom automatisch alle 3 s, du sprichst einfach durchgehend.
7. Optional `2` drücken und dasselbe mit „Systemweit“ wiederholen (Vergleich zu Strategie B).
8. `Q` drücken, die zwei Fragen beantworten.

## Test 2 – Merkt sich Windows den Mute? (ca. 3 Minuten)

1. Während Roblox mit Voice läuft, im Test-Fenster `P` drücken. Das Programm schließt sich, Roblox bleibt stumm.
2. Roblox komplett beenden, neu starten, wieder einem Voice-Spiel beitreten.
3. Test-Programm erneut starten. Es meldet automatisch „Persistenz-Test: JA/NEIN“ und hebt den Mute danach auf.

## Ergebnis zurückgeben

Neben der Exe entsteht `bloom-ptt-test-log.txt`. Diese Datei in den Chat hochladen. Sie enthält keine persönlichen Daten, nur Gerätenamen, Zustände, Latenzen und deine zwei Antworten.

## Sicherheit

- Das Programm ändert nur Mute-Zustände und stellt sie beim Beenden wieder her (auch bei Strg+C oder Schließen des Fensters).
- Es liest Tasten nur mit (Raw Input), blockiert nichts und greift nicht in Roblox ein.
- Falls Roblox nach einem Absturz des Tests stumm bleibt: Test erneut starten, `0` drücken, `Q` drücken.
