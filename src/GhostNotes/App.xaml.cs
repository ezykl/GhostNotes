using System;
using System.Windows;
using GhostNotes.Interop;
using GhostNotes.Services;
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
        var manager = new NoteManager(new NoteRepository(), guard);
        Manager = manager;
        manager.RestoreAll();
        if (manager.Windows.Count == 0) manager.CreateNote();
    }

    internal NoteManager? Manager { get; private set; }
}
