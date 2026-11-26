# ADR-007: Desktop ohne Bloom-Konto

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Launcher und Voice sollen offline funktionieren.

## Entscheidung

Der Desktop-Client spricht mit keinem Bloom-Server. Roblox-Login läuft direkt gegen Roblox (öffentlicher Client, PKCE, bloom://).

## Konsequenzen

Keine Betriebskosten, weniger Daten. Funktionen, die ein Konto bräuchten, sind P2.
