# ADR-008: Keine Roblox-Tokens auf dem Server

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Für die Verifizierung reicht die Roblox-Identität.

## Entscheidung

Bloom.Server verwirft Roblox-Tokens nach userinfo. Gespeichert wird nur Discord-ID ↔ Roblox-ID.

## Konsequenzen

Kleineres Leak-Risiko. Periodische Abfragen laufen über Gruppen-API-Keys oder öffentliche Daten.
