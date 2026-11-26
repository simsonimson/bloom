# ADR-003: Push-to-Talk über Core Audio, fail-closed

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Roblox bietet kein globales PTT und keine Schnittstelle für das eigene Mikro.

## Entscheidung

Strategie-Interface: A = Mute der Roblox-Aufnahme-Session (ISimpleAudioVolume), B = Endpoint-Mute. Automatische Wahl nach Selbsttest. Zustands-Journal und Bloom.Guard stellen nach Absturz den sicheren Zustand her. Kein virtuelles Mikrofon in 1.0.

## Konsequenzen

Abhängig von Spike S1. Fällt S1 negativ aus, ist PTT in 1.0 systemweit und wird in der UI so gekennzeichnet.
