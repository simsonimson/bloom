# ADR-009: Bloom.Server: ein selbst hostbarer Prozess

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Bloom ist Open Source, es gibt kein zentrales Hosting.

## Entscheidung

Bot und Verifizierungs-Webseite in einem ASP.NET-Core-Prozess, ausgeliefert als Docker-Image mit docker-compose (PostgreSQL, Caddy). Verifizierung per OAuth oder Bio-Code.

## Konsequenzen

Jede Community betreibt ihre eigene Instanz. Bio-Code umgeht das 10-Nutzer-Limit nicht reviewter OAuth-Apps.
