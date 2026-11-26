# ADR-001: Desktop-Stack: .NET 10 + WinUI 3

- Status: angenommen
- Datum: 2026-11-26

## Kontext

Bloom braucht Core Audio, Raw Input und eine native, ruhige Oberfläche in einer Sprache.

## Entscheidung

.NET 10 (LTS), WinUI 3 / Windows App SDK, unpackaged und self-contained. MVVM mit CommunityToolkit.Mvvm, DI über Microsoft.Extensions.Hosting.

## Konsequenzen

Kein .NET-Install beim Nutzer. Logik bleibt UI-unabhängig, damit eine Portierung der Shell möglich bleibt. Verworfen: WPF (Wartungsmodus), Tauri/Electron (zweite Sprache, höherer RAM).
