using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using GhostNotes.Interop;
using GhostNotes.Services;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ToolTip = System.Windows.Controls.ToolTip;

namespace GhostNotes;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private NoteManager? _manager;
    private TrayController? _tray;
    private HotkeyService? _hotkeys;
    private CaptureGuard? _captureGuard;
    private Thread? _signalThread;

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhostNotes");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"), $"[Dispatcher] {DateTime.UtcNow}: {e.Exception}\n\n");
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhostNotes");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"), $"[AppDomain] {DateTime.UtcNow}: {e.ExceptionObject}\n\n");
            }
            catch { }
        };
    }

    private static void Log(string msg)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhostNotes");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "startup.log"), $"[{DateTime.UtcNow:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("OnStartup started (Pure Floating Notes Architecture)");

        _instanceGuard = new SingleInstanceGuard(SingleInstanceGuard.DefaultMutexName);
        if (!_instanceGuard.IsFirst)
        {
            Log("Not first instance -> signaling first instance and exiting");
            SingleInstanceGuard.SignalFirstInstance();
            Shutdown();
            return;
        }

        var gate = new VersionGate(Environment.OSVersion.Version.Build);
        Log($"VersionGate: Build={Environment.OSVersion.Version.Build}, SupportsExcludeFromCapture={gate.SupportsExcludeFromCapture}");
        _captureGuard = new CaptureGuard(
            new NativeAffinityApi(),
            new ProcessWindowEnumerator(),
            gate,
            NativeMethods.GetCurrentProcessId());

        _manager = new NoteManager(new NoteRepository(), _captureGuard);
        _manager.RestoreAll();
        Log($"RestoreAll() executed, Active Windows = {_manager.Windows.Count}");

        // Hotkeys
        _hotkeys = new HotkeyService();
        _hotkeys.NewNoteRequested += (_, _) => Dispatcher.Invoke(() => _manager.CreateNote());
        _hotkeys.ToggleVisibilityRequested += (_, _) => Dispatcher.Invoke(() => _manager.ToggleVisibility());
        _hotkeys.Register();
        Log("Hotkeys registered (Message-Only Window)");

        // Tray controller with GhostNotes logo and context menu
        _tray = new TrayController(_manager, _hotkeys, _captureGuard);
        Log("TrayController initialized");

        if (!gate.SupportsExcludeFromCapture)
            _tray.ShowWarning(
                "This Windows build does not support WDA_EXCLUDEFROMCAPTURE; " +
                "notes will appear as black boxes in captures (WDA_MONITOR fallback).");

        AttachPopupHandler();

        // Capture protection sweep timer — ensures all sticky notes, pills, and popups stay invisible to OBS
        var sweep = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        sweep.Tick += (_, _) => _captureGuard.Sweep();
        sweep.Start();
        Log("Capture sweep timer active");

        // Listen for signal from secondary launches
        var manager = _manager;
        _signalThread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    if (_instanceGuard.WaitSignal(1000))
                    {
                        Log("Received signal from secondary launch -> Bring notes to front or toggle");
                        Dispatcher.Invoke(() =>
                        {
                            if (manager.Windows.Count == 0)
                            {
                                var noteToRestore = manager.Notes
                                    .Where(n => !NoteManager.IsDefaultTemplate(n.Markdown))
                                    .OrderByDescending(n => n.UpdatedAt)
                                    .FirstOrDefault()
                                    ?? manager.Notes.OrderByDescending(n => n.UpdatedAt).FirstOrDefault();

                                if (noteToRestore != null)
                                {
                                    manager.ReopenNote(noteToRestore);
                                }
                                else
                                {
                                    manager.CreateNote();
                                }
                            }
                            else
                            {
                                foreach (var w in manager.Windows)
                                {
                                    w.ReapplyProtectionAndShow();
                                    w.Activate();
                                }
                            }
                        });
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log($"SignalThread exception: {ex}"); }
        })
        {
            IsBackground = true
        };
        _signalThread.Start();
        Log("Signal thread started");
    }

    private void AttachPopupHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnAnyPopupOpened));
        EventManager.RegisterClassHandler(
            typeof(ToolTip),
            ToolTip.OpenedEvent,
            new RoutedEventHandler(OnAnyPopupOpened));
        EventManager.RegisterClassHandler(
            typeof(Popup),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPopupLoaded));
    }

    private void OnPopupLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Popup popup)
        {
            popup.Opened -= OnPopupOpened;
            popup.Opened += OnPopupOpened;
        }
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is Popup popup && popup.Child != null)
            ProtectVisual(popup.Child);
    }

    private void OnAnyPopupOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Popup popup && popup.Child != null)
            ProtectVisual(popup.Child);
        else if (sender is Visual v)
            ProtectVisual(v);
    }

    private void ProtectVisual(object child)
    {
        var guard = _captureGuard;
        if (guard is null) return;

        void Flag(object? obj)
        {
            if (obj is Visual v && PresentationSource.FromVisual(v) is HwndSource src)
                guard.ApplyToWindow(src.Handle);
        }

        if (child is FrameworkElement fe)
        {
            if (fe.IsLoaded) Flag(fe);
            fe.Loaded += (_, _) => Flag(fe);
        }
        else if (child is Visual v)
        {
            Flag(v);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _manager?.SaveAll();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit triggered with exit code {e.ApplicationExitCode}");
        _manager?.Dispose();
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
