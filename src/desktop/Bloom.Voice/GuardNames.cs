namespace Bloom.Voice;

/// <summary>Named kernel objects shared by bloom and Bloom.Guard (per user session).</summary>
public static class GuardNames
{
    /// <summary>Set by bloom right before a clean exit.</summary>
    public static string CleanEvent(int bloomProcessId) => $@"Local\bloom-guard-clean-{bloomProcessId}";

    /// <summary>Set by a new bloom start: a guard still recovering hands the journal over and exits.</summary>
    public const string HandoverEvent = @"Local\bloom-guard-handover";

    /// <summary>Held by a guard while it is recovering.</summary>
    public const string RecoveringMutex = @"Local\bloom-guard-recovering";
}
