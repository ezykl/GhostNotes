namespace GhostNotes.Interop;

public sealed class VersionGate
{
    private readonly int _build;

    public VersionGate(int buildNumber)
    {
        _build = buildNumber;
    }

    public bool SupportsExcludeFromCapture => _build >= 19041;
}
