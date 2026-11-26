# ADR-002: Eigener Bootstrapper mit Fallback

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Bloom soll Roblox selbst laden und starten. Die dafür nötigen Roblox-Endpunkte sind nicht dokumentiert.

## Entscheidung

Bloom registriert sich opt-in für roblox-player/roblox, lädt den unveränderten offiziellen Client, prüft Prüfsummen und startet ihn. Bei jedem Fehler übergibt Bloom an die offizielle Installation. Nach 3 Fehlschlägen pausiert der Bootstrapper.

## Konsequenzen

Volle Kontrolle über Start und Updates. Roblox-Änderungen machen Bloom nicht unbenutzbar. Risiko R15 bleibt und wird beobachtet.
