using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GhostNotes.Interop;
using GhostNotes.Services;
using GhostNotes.Views;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ToolTip = System.Windows.Controls.ToolTip;

namespace GhostNotes;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private NoteManager? _manager;
    private ManagerWindow? _managerWindow;
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
        Log("OnStartup started");

        _instanceGuard = new SingleInstanceGuard(SingleInstanceGuard.DefaultMutexName);
        if (!_instanceGuard.IsFirst)
        {
            Log("Not first instance -> signaling and shutting down");
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
        if (_manager.Notes.Count == 0)
        {
            _manager.CreateNote();
        }

        // Initialize v2 Manager Window
        _managerWindow = new ManagerWindow(_manager, _captureGuard);
        TrySetWindowIcon(_managerWindow);

        _manager.OpenManagerForNoteRequested += (_, note) =>
        {
            Dispatcher.Invoke(() => _managerWindow.SelectNote(note));
        };

        // Hotkeys
        _hotkeys = new HotkeyService();
        _hotkeys.NewNoteRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            var win = _manager.CreateNote();
            _managerWindow.SelectNote(win.Model);
        });
        _hotkeys.ToggleVisibilityRequested += (_, _) => Dispatcher.Invoke(() => _manager.ToggleVisibility());
        _hotkeys.Register();
        Log("Hotkeys registered");

        // Tray controller with GhostNotes logo & Open Manager action
        _tray = new TrayController(_manager, _hotkeys, _captureGuard, () =>
        {
            Dispatcher.Invoke(() =>
            {
                _managerWindow.Show();
                _managerWindow.WindowState = WindowState.Normal;
                _managerWindow.Activate();
            });
        });
        Log("TrayController initialized");

        if (!gate.SupportsExcludeFromCapture)
            _tray.ShowWarning(
                "This Windows build does not support WDA_EXCLUDEFROMCAPTURE; " +
                "notes will appear as black boxes in captures (WDA_MONITOR fallback).");

        AttachPopupHandler();

        // Capture protection sweep timer — ensures all windows and dropdowns stay invisible to OBS
        var sweep = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        sweep.Tick += (_, _) => _captureGuard.Sweep();
        sweep.Start();
        Log("Capture sweep timer active");

        // Show Manager window on first launch
        _managerWindow.Show();
        _managerWindow.Activate();

        // Listen for signal from secondary launches
        var mgrWin = _managerWindow;
        _signalThread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    if (_instanceGuard.WaitSignal(1000))
                    {
                        Log("Received show signal -> Opening Manager");
                        Dispatcher.Invoke(() =>
                        {
                            mgrWin.Show();
                            mgrWin.WindowState = WindowState.Normal;
                            mgrWin.Activate();
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

    private static void TrySetWindowIcon(Window window)
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var iconPath = Path.Combine(appDir, "Assets", "GhostNotes.ico");
            if (File.Exists(iconPath))
            {
                window.Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }
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
