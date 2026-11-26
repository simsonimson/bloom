# ADR-003: Push-to-Talk über Core Audio, fail-closed

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Roblox bietet kein globales PTT und keine Schnittstelle für das eigene Mikro.

## Entscheidung

Strategie-Interface: A = Mute der Roblox-Aufnahme-Session (ISimpleAudioVolume), B = Endpoint-Mute. Automatische Wahl nach Selbsttest. Zustands-Journal und Bloom.Guard stellen nach Absturz den sicheren Zustand her. Kein virtuelles Mikrofon in 1.0.

## Konsequenzen

Abhängig von Spike S1. Fällt S1 negativ aus, ist PTT in 1.0 systemweit und wird in der UI so gekennzeichnet.

## Nachtrag 26.11.2026 (Phase 3)

- Spike S1 wurde vom Projektinhaber als „funktioniert nicht“ gemeldet (ohne Log). Gemäß der Konsequenz oben ist **Systemweit (B)** die Standardmethode und in der UI so gekennzeichnet, inklusive einmaliger Bestätigung („betrifft auch Discord“). Die automatische Wahl von A nach Selbsttest entfällt, bis S1 bestätigt ist.
- **Nur Roblox (A)** bleibt als experimentelle, wählbare Methode erhalten. Weil Windows einen Session-Mute für die nächste Roblox-Sitzung merken kann, hebt bloom einen von ihm gesetzten Session-Mute beim nächsten Roblox-Start wieder auf, falls A dann nicht aktiv ist (Journal-Flag `PendingSessionUnmute`).
- **Roblox-Mikroknopf (D)** ist umgesetzt (Blueprint 6.5): nur Umschalten, Kalibrierung per Klick, Position in 96-dpi-Einheiten relativ zur Client-Fläche. Klicks per `SendInput`, kein Eingriff in den Roblox-Prozess. Klickt nicht, wenn Roblox den Mauszeiger ausblendet.
- Bloom.Guard ist eine eigene Mini-Exe neben `bloom.exe`. Signale: Event `Local\bloom-guard-clean-<pid>` (sauberes Ende), Event `Local\bloom-guard-handover` + Mutex `Local\bloom-guard-recovering` (neuer bloom-Start übernimmt das Journal).
