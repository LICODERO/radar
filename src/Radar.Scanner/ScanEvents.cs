namespace Radar.Scanner;

public sealed record ScanCounters(int Repos, int Agents, int Skills, int Gaps);

/// <summary>Progress events emitted by the scanner; the server streams them to the UI.</summary>
public abstract record ScanEvent;

public sealed record ScanStarted(string ScanRoot) : ScanEvent;

/// <param name="Phase">init | discovery | analysis | linking | done</param>
/// <param name="Percent">null while the total is still unknown (directory walk)</param>
public sealed record ScanPhase(string Phase, string Label, int? Percent, int? Workflows = null) : ScanEvent;

public sealed record RepoFound(int Found, string Name) : ScanEvent;

public sealed record RepoScanned(
    int Index, int Total, string Id, string Name, bool HasClaudeMd, int Agents, int Skills, int Coverage,
    int Percent, ScanCounters Counters) : ScanEvent;

public sealed record ScanCompleted(long DurationMs, SummaryInfo Summary) : ScanEvent;

public sealed record ScanCancelled : ScanEvent;

public sealed record ScanFailed(string Message) : ScanEvent;
