# ADR-012: Eigener Discord-IPC-Client statt DiscordRichPresence

- Status: angenommen (ersetzt die Bibliothekswahl aus ADR-006, nicht die Entscheidung für IPC)
- Datum: 2026-11-27

## Kontext

ADR-006 sah die Bibliothek DiscordRichPresence (MIT) vor. bloom braucht davon nur Handshake, `SET_ACTIVITY`, Ping/Pong und die Benutzerdaten aus `READY` (C3). Das Protokoll ist klein: Named Pipe `discord-ipc-0…9`, Frame = Opcode + Länge (je int32, little endian) + UTF-8-JSON.

## Entscheidung

`Bloom.Connect.Discord` enthält einen eigenen, rund 150 Zeilen großen IPC-Client (`DiscordIpcConnection`) und den `DiscordPresenceService`. Der Service ist die Austauschstelle aus ADR-006: Ein späterer Wechsel auf das Social SDK betrifft nur dieses Projekt.

## Konsequenzen

- Keine zusätzliche Abhängigkeit, vollständig testbar (Frames über einen Stream).
- Wir pflegen das Protokoll selbst. Ändert Discord es, betrifft das nur `DiscordIpcConnection`.
- Aktivitäts-Updates sind auf eins pro 4 s begrenzt (Discord erlaubt etwa 5 pro 20 s).
