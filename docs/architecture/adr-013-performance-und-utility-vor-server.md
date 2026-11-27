# ADR-013: Performance & Utility vor dem Community-Server

- Status: angenommen
- Datum: 2026-11-27

## Kontext

Der Projektinhaber will bloom stärker als Performance- und Utility-Werkzeug für Roblox. Ausgewählt wurden: Leistungsmodus, Grafik-/FPS-Presets (nur erlaubte FastFlags), Leistungsanzeige, Aufräumen, Server-Infos & Rejoin, Server-Browser, Spielprofile und App-Hotkeys.

## Entscheidung

- Phase 4 (Bloom Connect Desktop: C1–C5) bleibt wie geplant.
- Neue Phase 5: Performance & Utility (die Liste oben). E4 Server-Region, E5 Spielprofile und E6 App-Hotkeys rücken von 1.1/1.2 in diese Phase vor.
- Bloom.Server mit Discord-Bot (C6–C10) wird Phase 6, die Härtung Phase 7.

## Leitplanken

Alles läuft außerhalb des Roblox-Prozesses: keine Injection, keine geänderten Roblox-Dateien, keine Umgehung von Schutzmechanismen, nur FastFlags von der offiziellen Allowlist. Nicht geplant bleiben Mehrfach-Instanzen, FPS-Freischaltung über nicht erlaubte Flags und Automatisierung wie Auto-Farm oder Anti-AFK.
