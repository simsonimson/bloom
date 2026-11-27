# ADR-006: Rich Presence über IPC hinter Interface

- Status: angenommen (Bibliothekswahl durch ADR-012 ersetzt)
- Datum: 2026-11-26

## Kontext

Das offizielle Discord Social SDK gibt es nur für C++, Unity und Unreal.

## Entscheidung

IPresenceProvider mit Implementierung über DiscordRichPresence (MIT, IPC).

## Konsequenzen

Austausch gegen das Social SDK bleibt möglich, ohne den Rest der App anzufassen.
