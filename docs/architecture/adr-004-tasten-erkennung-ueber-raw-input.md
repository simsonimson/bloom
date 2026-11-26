# ADR-004: Tasten-Erkennung über Raw Input

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Low-Level-Hooks werden bei Timeout ab Windows 7 stillschweigend entfernt.

## Entscheidung

Raw Input mit RIDEV_INPUTSINK auf eigenem Thread, plus 50-ms-Watchdog über GetAsyncKeyState.

## Konsequenzen

Kein stilles Ausfallen, keine blockierten Tasten. Abhängig von Spike S2 (Verhalten bei Roblox im Vordergrund und mit Adminrechten).
