using System;
using System.Windows;
using GhostNotes.Interop;
using GhostNotes.Models;
using Application = System.Windows.Application;

namespace GhostNotes;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var gate = new VersionGate(Environment.OSVersion.Version.Build);
        var guard = new CaptureGuard(
            new NativeAffinityApi(),
            new ProcessWindowEnumerator(),
            gate,
            NativeMethods.GetCurrentProcessId());
        var window = new NoteWindow(new Note(), guard);
        window.Show();
    }
}
