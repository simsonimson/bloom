# ADR-005: Intro als vorgerendertes Video

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Die Animation stammt aus bloom3d.py (Python, numpy, pygame). Nutzer sollen kein Python brauchen.

## Entscheidung

Bloom rendert bloom3d.py (MIT) in CI deterministisch mit 2× Auflösung und 60 fps zu einem Video und spielt es mit MediaPlayerElement ab. Repo-Medien (mp4, gif) werden nicht verwendet.

## Konsequenzen

Exakte Optik, kleine Datei, schneller Start. Fällt die Schärfe auf 4K durch, wird die native Zeichenraster-Wiedergabe vorgezogen.
