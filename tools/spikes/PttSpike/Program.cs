// bloom · Push-to-Talk-Testprogramm (Spikes S1 + S2)
//
// Prüft zwei offene Fragen aus dem Blueprint:
//   S1  Wirkt ISimpleAudioVolume::SetMute auf Roblox' Aufnahme-Session (Strategie A)?
//       Bleibt der Mute über einen Roblox-Neustart gespeichert?
//   S2  Kommen Raw-Input-Ereignisse an, während Roblox im Vordergrund ist?
//
// Das Programm ändert nur Mute-Zustände und stellt sie beim Beenden wieder her.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Bloom.Spikes.Ptt;

internal enum MuteMode { Off = 0, Session = 1, Endpoint = 2 }

internal static class Program
{
    private const string RobloxProcessName = "RobloxPlayerBeta";
    private const string Version = "0.1.0";

    private static readonly object AudioLock = new();
    private static readonly BlockingCollection<InputEvent> Events = new(new ConcurrentQueue<InputEvent>(), 4096);
    private static readonly List<string> EventLog = new();
    private static readonly List<double> LatenciesMs = new();
    private static readonly Dictionary<string, bool> EndpointOriginalMute = new();
    private static readonly HashSet<string> TouchedSessions = new();

    private static StreamWriter? _log;
    private static string _logPath = "";
    private static volatile uint[] _robloxPids = Array.Empty<uint>();

    private static List<CaptureDevice> _devices = new();
    private static List<CaptureSession> _robloxSessions = new();
    private static List<CaptureSession> _otherSessions = new();
    private static CaptureDevice? _endpointTarget;

    private static MuteMode _mode = MuteMode.Session;
    private static bool _holdMode = true;
    private static int _pttVk = 0x56; // V
    private static bool _keyDown;
    private static bool _transmit;
    private static bool _quit;
    private static bool _restored;
    private static bool _skipRestore;

    private static int _watchdogCorrections;
    private static int _eventsWhileRobloxForeground;
    private static int _eventsTotal;
    private static bool _usedMuteAndUnmute;
    private static bool _sawMuted, _sawOpen;

    // Auto-Test: wechselt alle 3 s zwischen stumm und senden.
    private static long _autoTestUntil;
    private static long _autoTestNextFlip;

    // Pegelstatistik: Session-Pegel, während das Mikro Signal hat (Endpoint-Pegel > Schwelle).
    private static double _peakSumMuted, _peakSumOpen;
    private static int _peakCountMuted, _peakCountOpen;
    private static float _lastEndpointPeak, _lastSessionPeak;

    private static string? _persistMarkerPath;
    private static bool _persistCheckPending;

    [MTAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "bloom · PTT-Test";
        OpenLog();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _quit = true; };
        using var closeReg = PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => SafeRestore("Fenster geschlossen"));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log($"ABSTURZ: {e.ExceptionObject}");
            SafeRestore("Absturz");
        };

        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Log($"FEHLER: {ex}");
            Console.WriteLine();
            Console.WriteLine($"Fehler: {ex.Message}");
            Console.WriteLine($"Details stehen in: {_logPath}");
            return 1;
        }
        finally
        {
            SafeRestore("Programmende");
            _log?.Flush();
        }
    }

    private static int Run()
    {
        Log($"=== bloom PTT-Test {Version} gestartet {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        Log($"Windows: {Environment.OSVersion.VersionString}, 64-Bit: {Environment.Is64BitOperatingSystem}, Admin: {IsCurrentProcessElevated()}");

        CheckPersistenceMarker();

        var listener = new RawInputListener(Events, pid => Array.IndexOf(_robloxPids, pid) >= 0);
        listener.Start();
        if (listener.StartupError is { } err)
        {
            Log($"Raw Input konnte nicht gestartet werden: {err}");
            Console.WriteLine($"Raw Input konnte nicht gestartet werden: {err}");
            return 2;
        }
        Log("Raw Input aktiv (RIDEV_INPUTSINK, Tastatur + Maus).");

        RefreshDevices();
        LogDevices();

        ChoosePttKey();

        var swWatchdog = Stopwatch.StartNew();
        var swSessions = Stopwatch.StartNew();
        var swDevices = Stopwatch.StartNew();
        var swRender = Stopwatch.StartNew();
        RefreshSessions();
        Console.Clear();

        while (!_quit)
        {
            if (Events.TryTake(out var ev, 5))
            {
                HandleInput(ev);
                while (Events.TryTake(out ev)) HandleInput(ev);
            }

            HandleConsoleKeys();

            if (swWatchdog.ElapsedMilliseconds >= 50)
            {
                swWatchdog.Restart();
                Watchdog();
                AutoTestTick();
                SampleMeters();
            }
            if (swSessions.ElapsedMilliseconds >= 250)
            {
                swSessions.Restart();
                RefreshSessions();
                Apply(null);
            }
            if (swDevices.ElapsedMilliseconds >= 3000)
            {
                swDevices.Restart();
                RefreshDevices();
            }
            if (swRender.ElapsedMilliseconds >= 150)
            {
                swRender.Restart();
                Render();
            }
        }

        if (_skipRestore)
        {
            Console.Clear();
            Console.WriteLine("Persistenz-Test vorbereitet. Roblox' Aufnahme-Session ist jetzt STUMM und bleibt es.");
            Console.WriteLine();
            Console.WriteLine("1. Roblox komplett schließen.");
            Console.WriteLine("2. Roblox neu starten und einem Spiel mit Voice Chat beitreten.");
            Console.WriteLine("3. Dieses Programm erneut starten. Es meldet dann, ob Windows den Mute gespeichert hat.");
            Console.WriteLine();
            Console.WriteLine("Enter zum Schließen.");
            Console.ReadLine();
            return 0;
        }

        AskObservations();
        SafeRestore("Beenden");
        WriteSummary();
        Console.WriteLine();
        Console.WriteLine($"Ergebnisse gespeichert in: {_logPath}");
        Console.WriteLine("Bitte diese Datei in den Chat hochladen. Enter zum Schließen.");
        Console.ReadLine();
        return 0;
    }

    // ---------------------------------------------------------------- Eingabe

    private static void ChoosePttKey()
    {
        Console.Clear();
        Console.WriteLine("bloom · Push-to-Talk-Test");
        Console.WriteLine();
        Console.WriteLine("Drücke jetzt die Taste, die du als Push-to-Talk benutzen willst.");
        Console.WriteLine("Erlaubt: jede Tastaturtaste, mittlere Maustaste, Maus 4, Maus 5.");
        Console.WriteLine("Esc = Standard (V).");

        while (Events.Count > 0) Events.TryTake(out _);
        while (true)
        {
            if (!Events.TryTake(out var ev, 100)) continue;
            if (!ev.Down || ev.VirtualKey is 0x01 or 0x02) continue;
            if (ev.VirtualKey != 0x1B) _pttVk = ev.VirtualKey;
            break;
        }
        // Loslassen der Auswahltaste abwarten, damit sie nicht sofort sendet.
        var sw = Stopwatch.StartNew();
        while (RawInputListener.IsPhysicallyDown(_pttVk) && sw.ElapsedMilliseconds < 3000) Thread.Sleep(10);
        while (Events.Count > 0) Events.TryTake(out _);
        while (Console.KeyAvailable) Console.ReadKey(true);
        Log($"PTT-Taste: {RawInputListener.KeyName(_pttVk)} (VK 0x{_pttVk:X2})");
    }

    private static void HandleInput(InputEvent ev)
    {
        _eventsTotal++;
        if (ev.RobloxInForeground) _eventsWhileRobloxForeground++;
        if (ev.VirtualKey != _pttVk) return;

        if (ev.Down)
        {
            if (_keyDown) return; // Tastenwiederholung
            _keyDown = true;
            _transmit = _holdMode || !_transmit;
        }
        else
        {
            if (!_keyDown) return;
            _keyDown = false;
            if (_holdMode) _transmit = false;
        }
        AddEvent($"{RawInputListener.KeyName(ev.VirtualKey)} {(ev.Down ? "gedrückt" : "losgelassen")}" +
                 $"{(ev.RobloxInForeground ? " (Roblox im Vordergrund)" : "")} → {(_transmit ? "SENDET" : "STUMM")}");
        Apply(ev.Timestamp);
    }

    private static void HandleConsoleKeys()
    {
        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.D1 or ConsoleKey.NumPad1: SwitchMode(MuteMode.Session); break;
                case ConsoleKey.D2 or ConsoleKey.NumPad2: SwitchMode(MuteMode.Endpoint); break;
                case ConsoleKey.D0 or ConsoleKey.NumPad0: SwitchMode(MuteMode.Off); break;
                case ConsoleKey.T:
                    _holdMode = !_holdMode;
                    _transmit = false;
                    AddEvent($"Modus: {(_holdMode ? "Halten" : "Umschalten")}");
                    Apply(null);
                    break;
                case ConsoleKey.A:
                    var now = Stopwatch.GetTimestamp();
                    _autoTestUntil = now + Stopwatch.Frequency * 30;
                    _autoTestNextFlip = now;
                    _transmit = false;
                    AddEvent("Auto-Test gestartet: 30 s lang alle 3 s Wechsel. Durchgehend sprechen!");
                    break;
                case ConsoleKey.P:
                    StartPersistenceTest();
                    break;
                case ConsoleKey.Q or ConsoleKey.Escape:
                    _quit = true;
                    break;
            }
        }
    }

    private static void Watchdog()
    {
        if (!_keyDown || !_holdMode) return;
        if (RawInputListener.IsPhysicallyDown(_pttVk)) return;
        _keyDown = false;
        _transmit = false;
        _watchdogCorrections++;
        AddEvent("Watchdog: Loslassen nicht gemeldet, Taste nicht mehr gedrückt → STUMM");
        Apply(null);
    }

    private static void AutoTestTick()
    {
        if (_autoTestUntil == 0) return;
        var now = Stopwatch.GetTimestamp();
        if (now >= _autoTestUntil)
        {
            _autoTestUntil = 0;
            _transmit = false;
            AddEvent("Auto-Test beendet → STUMM");
            Apply(null);
            return;
        }
        if (now < _autoTestNextFlip) return;
        _autoTestNextFlip = now + Stopwatch.Frequency * 3;
        _transmit = !_transmit;
        AddEvent($"Auto-Test → {(_transmit ? "SENDET" : "STUMM")}");
        Apply(null);
    }

    // ---------------------------------------------------------------- Audio

    private static void RefreshDevices()
    {
        lock (AudioLock)
        {
            try
            {
                _devices = CoreAudio.GetActiveCaptureDevices();
            }
            catch (Exception ex)
            {
                Log($"Geräteliste fehlgeschlagen: {ex.Message}");
            }
        }
    }

    private static void RefreshSessions()
    {
        lock (AudioLock)
        {
            var roblox = new List<CaptureSession>();
            var others = new List<CaptureSession>();
            foreach (var device in _devices)
            {
                try
                {
                    foreach (var s in CoreAudio.GetSessions(device))
                    {
                        if (s.State == 2) continue; // expired
                        if (string.Equals(s.ProcessName, RobloxProcessName, StringComparison.OrdinalIgnoreCase))
                            roblox.Add(s);
                        else
                            others.Add(s);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Sessions auf \"{device.Name}\" nicht lesbar: {ex.Message}");
                }
            }

            foreach (var s in roblox.Where(s => _robloxSessions.All(o => o.InstanceId != s.InstanceId)))
            {
                var mutedAtDiscovery = SafeGetMute(s);
                Log($"Roblox-Session gefunden: PID {s.ProcessId}, Gerät \"{s.Device.Name}\", Zustand {StateName(s.State)}, " +
                    $"beim Finden stumm: {mutedAtDiscovery}");
                if (_persistCheckPending)
                {
                    _persistCheckPending = false;
                    var verdict = mutedAtDiscovery == true
                        ? "JA – Windows hat den Session-Mute über den Roblox-Neustart gespeichert (Risiko R2 tritt ein)."
                        : "NEIN – nach dem Roblox-Neustart war die Session nicht mehr stumm (R2 tritt nicht ein).";
                    Log($"PERSISTENZ-TEST: {verdict}");
                    AddEvent($"Persistenz-Test: {verdict}");
                    try { s.SetMute(false); } catch { /* ignore */ }
                    try { if (_persistMarkerPath is not null) File.Delete(_persistMarkerPath); } catch { /* ignore */ }
                    _mode = MuteMode.Session;
                }
            }

            _robloxSessions = roblox;
            _otherSessions = others;
            var processes = Process.GetProcessesByName(RobloxProcessName);
            _robloxPids = processes.Select(p => (uint)p.Id).ToArray();
            foreach (var p in processes) p.Dispose();
            _endpointTarget = roblox.FirstOrDefault()?.Device ?? DefaultCommunicationsDevice();
        }
    }

    private static CaptureDevice? DefaultCommunicationsDevice()
    {
        var id = CoreAudio.GetDefaultCaptureId(ERole.Communications);
        return _devices.FirstOrDefault(d => d.Id == id) ?? _devices.FirstOrDefault();
    }

    private static void SwitchMode(MuteMode mode)
    {
        if (mode == _mode) return;
        lock (AudioLock)
        {
            if (_mode == MuteMode.Session) UnmuteRobloxSessions();
            if (_mode == MuteMode.Endpoint) RestoreEndpoints();
            _mode = mode;
            _transmit = false;
        }
        AddEvent($"Methode: {ModeName(mode)}");
        Apply(null);
    }

    /// <summary>Setzt den gewünschten Zustand. eventTimestamp != null misst die Latenz.</summary>
    private static void Apply(long? eventTimestamp)
    {
        if (_persistCheckPending) return;
        lock (AudioLock)
        {
            var wantMuted = !_transmit;
            var changed = false;
            try
            {
                switch (_mode)
                {
                    case MuteMode.Session:
                        foreach (var s in _robloxSessions)
                        {
                            if (SafeGetMute(s) == wantMuted) continue;
                            s.SetMute(wantMuted);
                            TouchedSessions.Add(s.InstanceId);
                            changed = true;
                        }
                        break;
                    case MuteMode.Endpoint when _endpointTarget is { } dev:
                        if (!EndpointOriginalMute.ContainsKey(dev.Id))
                            EndpointOriginalMute[dev.Id] = dev.GetEndpointMute();
                        if (dev.GetEndpointMute() != wantMuted)
                        {
                            dev.SetEndpointMute(wantMuted);
                            changed = true;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AddEvent($"FEHLER beim Stummschalten: {ex.Message}");
                Log($"Apply-Fehler: {ex}");
            }

            if (changed)
            {
                if (wantMuted) _sawMuted = true; else _sawOpen = true;
                if (_sawMuted && _sawOpen) _usedMuteAndUnmute = true;
                if (eventTimestamp is long ts)
                    LatenciesMs.Add((Stopwatch.GetTimestamp() - ts) * 1000.0 / Stopwatch.Frequency);
            }
        }
    }

    private static void SampleMeters()
    {
        lock (AudioLock)
        try
        {
            var session = _robloxSessions.FirstOrDefault();
            var device = session?.Device ?? _endpointTarget;
            _lastEndpointPeak = device?.EndpointPeak() ?? 0f;
            _lastSessionPeak = session?.Peak() ?? 0f;
            if (session is null || _lastEndpointPeak < 0.05f) return;

            var muted = SafeGetMute(session) == true || (_mode == MuteMode.Endpoint && session.Device.GetEndpointMute());
            if (muted) { _peakSumMuted += _lastSessionPeak; _peakCountMuted++; }
            else { _peakSumOpen += _lastSessionPeak; _peakCountOpen++; }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Gerät oder Session gerade verschwunden.
        }
    }

    private static void UnmuteRobloxSessions()
    {
        foreach (var s in _robloxSessions)
        {
            try { if (SafeGetMute(s) == true) s.SetMute(false); } catch { /* ignore */ }
        }
    }

    private static void RestoreEndpoints()
    {
        foreach (var (id, original) in EndpointOriginalMute)
        {
            var dev = _devices.FirstOrDefault(d => d.Id == id);
            try { dev?.SetEndpointMute(original); } catch { /* ignore */ }
        }
        EndpointOriginalMute.Clear();
    }

    private static void SafeRestore(string reason)
    {
        if (_restored || _skipRestore) return;
        lock (AudioLock)
        {
            if (_restored) return;
            _restored = true;
            try { UnmuteRobloxSessions(); } catch { /* ignore */ }
            try { RestoreEndpoints(); } catch { /* ignore */ }
            Log($"Ursprünglicher Zustand wiederhergestellt ({reason}).");
        }
    }

    private static bool? SafeGetMute(CaptureSession s)
    {
        try { return s.GetMute(); } catch { return null; }
    }

    // ---------------------------------------------------------------- Persistenz-Test

    private static void CheckPersistenceMarker()
    {
        _persistMarkerPath = Path.Combine(Path.GetDirectoryName(_logPath)!, "bloom-ptt-persistenz-test.json");
        if (!File.Exists(_persistMarkerPath)) return;
        _persistCheckPending = true;
        _mode = MuteMode.Off;
        Log("Persistenz-Test-Marker gefunden: prüfe den Mute-Zustand der nächsten Roblox-Session, bevor etwas verändert wird.");
    }

    private static void StartPersistenceTest()
    {
        lock (AudioLock)
        {
            if (_robloxSessions.Count == 0)
            {
                AddEvent("Persistenz-Test: keine Roblox-Voice-Session gefunden. Erst Voice in Roblox verbinden.");
                return;
            }
            foreach (var s in _robloxSessions)
            {
                try { s.SetMute(true); } catch { /* ignore */ }
            }
            RestoreEndpoints();
            File.WriteAllText(_persistMarkerPath!, JsonSerializer.Serialize(new { created = DateTime.Now, version = Version }));
            Log("PERSISTENZ-TEST gestartet: Roblox-Session stumm geschaltet, Programm endet ohne Wiederherstellung.");
            _skipRestore = true;
            _quit = true;
        }
    }

    // ---------------------------------------------------------------- Anzeige

    private static void Render()
    {
        try
        {
            var width = Math.Max(60, Console.WindowWidth - 1);
            var lines = new List<string>
            {
                $"bloom · Push-to-Talk-Test {Version}   (Log: {Path.GetFileName(_logPath)})",
                new string('─', Math.Min(width, 90)),
                $"Taste:    {RawInputListener.KeyName(_pttVk)}  ·  {(_holdMode ? "Halten zum Senden" : "Drücken zum Umschalten")}",
                $"Methode:  {ModeName(_mode)}",
                $"Roblox:   {RobloxLine()}",
                $"Zustand:  {StateLine()}",
                "",
                $"Mikro-Pegel (Windows): {Bar(_lastEndpointPeak)}",
                $"Roblox-Session-Pegel:  {Bar(_lastSessionPeak)}",
                "",
                $"Latenz p50/p95/max: {LatencyStats()}  ·  Watchdog-Korrekturen: {_watchdogCorrections}",
                $"Tastenereignisse gesamt: {_eventsTotal}  ·  davon mit Roblox im Vordergrund: {_eventsWhileRobloxForeground}",
                $"Andere Apps mit Mikro: {OtherSessionsLine()}",
                "",
                "[1] Nur Roblox (Session)  [2] Systemweit (Mikro)  [0] PTT aus  [T] Halten/Umschalten",
                "[A] Auto-Test 30 s  [P] Persistenz-Test  [Q] Beenden + Ergebnis",
                new string('─', Math.Min(width, 90)),
            };
            lock (EventLog) lines.AddRange(EventLog.TakeLast(10));

            var sb = new StringBuilder();
            foreach (var line in lines)
                sb.Append(line.Length > width ? line[..width] : line.PadRight(width)).Append('\n');
            for (var i = lines.Count; i < 32; i++) sb.Append(new string(' ', width)).Append('\n');
            Console.SetCursorPosition(0, 0);
            Console.Write(sb.ToString());
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or COMException)
        {
            // Konsole nicht verfügbar, Fenster zu klein oder Gerät gerade verschwunden.
        }
    }

    private static string RobloxLine()
    {
        if (_robloxPids.Length == 0) return "nicht gestartet";
        if (_robloxSessions.Count == 0) return $"läuft (PID {string.Join(", ", _robloxPids)}), keine Voice-Session – Voice im Spiel verbinden";
        var s = _robloxSessions[0];
        var elevated = IsProcessElevated(s.ProcessId) switch { true => ", ACHTUNG: läuft als Admin", null => ", Adminstatus unbekannt", _ => "" };
        return $"Voice-Session auf \"{s.Device.Name}\" ({StateName(s.State)}){elevated}";
    }

    private static string StateLine()
    {
        if (_persistCheckPending) return "Persistenz-Test: warte auf Roblox-Voice-Session …";
        if (_mode == MuteMode.Off) return "PTT aus – Bloom verändert nichts";
        if (_mode == MuteMode.Session && _robloxSessions.Count == 0) return "warte auf Roblox-Voice-Session";
        var actual = _mode == MuteMode.Session
            ? SafeGetMute(_robloxSessions[0])
            : _endpointTarget?.GetEndpointMute();
        var label = actual switch { true => "STUMM", false => "SENDET", null => "unbekannt" };
        var auto = _autoTestUntil != 0 ? "  (Auto-Test läuft)" : "";
        return $"{label}{auto}";
    }

    private static string OtherSessionsLine()
    {
        var names = _otherSessions.Select(s => s.ProcessName).Distinct().ToList();
        return names.Count == 0 ? "keine" : string.Join(", ", names);
    }

    private static string Bar(float peak)
    {
        var filled = (int)Math.Round(Math.Clamp(peak, 0f, 1f) * 30);
        return new string('█', filled) + new string('░', 30 - filled) + $" {peak:0.00}";
    }

    private static string LatencyStats()
    {
        if (LatenciesMs.Count == 0) return "–";
        var sorted = LatenciesMs.OrderBy(x => x).ToList();
        double P(double q) => sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(q * sorted.Count))];
        return $"{P(0.5):0.0}/{P(0.95):0.0}/{sorted[^1]:0.0} ms";
    }

    private static string ModeName(MuteMode m) => m switch
    {
        MuteMode.Session => "1 · Nur Roblox (Session-Mute, Strategie A)",
        MuteMode.Endpoint => "2 · Systemweit (ganzes Mikro, Strategie B)",
        _ => "0 · PTT aus",
    };

    private static string StateName(int state) => state switch { 0 => "inaktiv", 1 => "aktiv", 2 => "abgelaufen", _ => $"#{state}" };

    // ---------------------------------------------------------------- Ergebnis

    private static void AskObservations()
    {
        Console.Clear();
        if (!_usedMuteAndUnmute)
        {
            Log("Beobachtung: nicht abgefragt (es wurde nicht zwischen stumm und senden gewechselt).");
            return;
        }
        Console.WriteLine("Kurze Auswertung (bezieht sich auf das Mikro-Symbol in Roblox, während du gesprochen hast):");
        Console.WriteLine();
        var mutedMoved = AskYesNo("Hat sich das Mikro-Symbol bewegt, während bloom STUMM geschaltet hatte? (j/n): ");
        var openMoved = AskYesNo("Hat sich das Mikro-Symbol bewegt, während bloom SENDET angezeigt hat? (j/n): ");
        Log($"Beobachtung Nutzer: bewegt bei STUMM = {mutedMoved}, bewegt bei SENDET = {openMoved}, Methode zuletzt = {ModeName(_mode)}");

        string verdict = (mutedMoved, openMoved) switch
        {
            (false, true) => "S1 BESTANDEN – Stummschalten wirkt auf Roblox.",
            (true, _) => "S1 NICHT BESTANDEN – Roblox hat trotz Stummschaltung Ton bekommen.",
            (false, false) => "UNKLAR – das Symbol hat sich auch beim Senden nicht bewegt (Voice verbunden? Roblox-Mikro an?).",
        };
        Log($"ERGEBNIS: {verdict}");
        Console.WriteLine();
        Console.WriteLine(verdict);
    }

    private static bool AskYesNo(string question)
    {
        while (true)
        {
            Console.Write(question);
            var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (answer is "j" or "ja" or "y" or "yes") return true;
            if (answer is "n" or "nein" or "no") return false;
        }
    }

    private static void WriteSummary()
    {
        Log("--- Zusammenfassung ---");
        Log($"PTT-Taste: {RawInputListener.KeyName(_pttVk)}; Modus zuletzt: {(_holdMode ? "Halten" : "Umschalten")}");
        Log($"Latenz (Taste → Mute gesetzt) p50/p95/max: {LatencyStats()} bei {LatenciesMs.Count} Messungen");
        Log($"Watchdog-Korrekturen: {_watchdogCorrections}");
        Log($"S2: Tastenereignisse gesamt {_eventsTotal}, davon mit Roblox im Vordergrund {_eventsWhileRobloxForeground}");
        Log(_peakCountOpen > 0 || _peakCountMuted > 0
            ? $"Session-Pegel beim Sprechen: offen Ø {Avg(_peakSumOpen, _peakCountOpen):0.000} ({_peakCountOpen} Proben), " +
              $"stumm Ø {Avg(_peakSumMuted, _peakCountMuted):0.000} ({_peakCountMuted} Proben)"
            : "Session-Pegel: keine Proben mit Sprachsignal");
        Log("=== Ende ===");
    }

    private static double Avg(double sum, int count) => count == 0 ? 0 : sum / count;

    // ---------------------------------------------------------------- Hilfen

    private static void OpenLog()
    {
        var dir = AppContext.BaseDirectory;
        try
        {
            _logPath = Path.Combine(dir, "bloom-ptt-test-log.txt");
            _log = new StreamWriter(_logPath, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            _logPath = Path.Combine(Path.GetTempPath(), "bloom-ptt-test-log.txt");
            _log = new StreamWriter(_logPath, append: true, Encoding.UTF8) { AutoFlush = true };
        }
    }

    private static void LogDevices()
    {
        var defaultComm = CoreAudio.GetDefaultCaptureId(ERole.Communications);
        var defaultConsole = CoreAudio.GetDefaultCaptureId(ERole.Console);
        foreach (var d in _devices)
        {
            var tags = new List<string>();
            if (d.Id == defaultConsole) tags.Add("Standard");
            if (d.Id == defaultComm) tags.Add("Standard-Kommunikation");
            bool muted;
            try { muted = d.GetEndpointMute(); } catch { muted = false; }
            Log($"Mikrofon: \"{d.Name}\"{(tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "")}, Windows-Mute: {muted}");
        }
        if (_devices.Count == 0) Log("Keine aktiven Mikrofone gefunden.");
    }

    private static void AddEvent(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {text}";
        lock (EventLog)
        {
            EventLog.Add(line);
            if (EventLog.Count > 200) EventLog.RemoveRange(0, 100);
        }
        Log(text);
    }

    private static void Log(string text)
    {
        try { _log?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {text}"); }
        catch { /* ignore */ }
    }

    // ---------------------------------------------------------------- Rechte

    private static bool IsCurrentProcessElevated() => IsProcessElevated((uint)Environment.ProcessId) == true;

    private static readonly Dictionary<uint, bool?> ElevationCache = new();

    private static bool? IsProcessElevated(uint pid)
    {
        if (ElevationCache.TryGetValue(pid, out var cached)) return cached;
        bool? result = null;
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process != IntPtr.Zero)
        {
            try
            {
                if (OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token))
                {
                    try
                    {
                        if (GetTokenInformation(token, 20 /* TokenElevation */, out var elevation, 4, out _))
                            result = elevation != 0;
                    }
                    finally { CloseHandle(token); }
                }
                else
                {
                    // Zugriff verweigert auf das Token eines Prozesses des gleichen Nutzers
                    // bedeutet praktisch immer: der Prozess läuft mit höheren Rechten.
                    result = Marshal.GetLastWin32Error() == 5 ? true : null;
                }
            }
            finally { CloseHandle(process); }
        }
        ElevationCache[pid] = result;
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
