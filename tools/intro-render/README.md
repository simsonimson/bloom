# Intro-Render (Spike S3)

Erzeugt das Startintro von bloom aus `bloom3d.py` von Edward Jones (MIT, siehe `LICENSE` hier im Ordner).

## Was angepasst wurde

`bloom3d.py` ist eine Kopie des Originals mit wenigen Parametern (Kopfkommentar der Datei):

- `--scale`: Auflösung, Schrift, Projektion und Mittelpunkt skalieren mit.
- `--fps`: Bildrate für `--record`.
- Der Zufallsraster für den Glitch-Effekt und die Glow-Weichzeichnung arbeiten in Originalgröße. Dadurch bleiben Geometrie und Glow-Radius bei jeder Skalierung gleich.
- `--glow-gain`: Dünnere Schriftstriche bei höherer Auflösung erzeugen weniger Glow. `render.py` kalibriert den Faktor automatisch, bis die Helligkeit bei 6,8 s dem Original entspricht.

## Ausgabe (`output/`)

| Datei | Inhalt |
| --- | --- |
| `intro-full.mp4` | 0,3–8,6 s, 1920 × 1520, 60 fps, H.264 CRF 18 |
| `intro-short.mp4` | 4,5–8,6 s, gleiche Einstellungen |
| `intro-full-hevc10.mp4` | 10-Bit-HEVC-Variante zum Banding-Vergleich |
| `intro-still.png` | Standbild bei 6,8 s (reduzierte Bewegung) |
| `compare-*.png` | Original 1× links, 2× herunterskaliert rechts |
| `render-info.txt` | Kalibrierter Glow-Faktor, Dateigrößen, Laufzeiten |

## Lokal ausführen

```text
pip install -r requirements.txt
python render.py
```

Unter Windows wird Consolas verwendet (wie im Original). Unter Linux fällt pygame auf DejaVu Sans Mono zurück, die Optik weicht dann leicht ab. Maßgeblich ist der Build im Workflow „Intro render“.

## Messwerte (26.11.2026)

Linux (DejaVu): Glow-Faktor 1,13, `intro-full.mp4` 7,5 MB, `intro-short.mp4` 4,4 MB, Renderzeit ca. 4 min.
Windows-CI (Consolas): Glow-Faktor 0,93, `intro-full.mp4` 8,3 MB, `intro-short.mp4` 5,1 MB, HEVC 10 Bit 9,2 MB.
