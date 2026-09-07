using System;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceGuard = new SingleInstanceGuard(SingleInstanceGuard.DefaultMutexName);
        if (!_instanceGuard.IsFirst)
        {
            SingleInstanceGuard.SignalFirstInstance();
            Shutdown();
            return;
        }

        var gate = new VersionGate(Environment.OSVersion.Version.Build);
        _captureGuard = new CaptureGuard(
            new NativeAffinityApi(),
            new ProcessWindowEnumerator(),
            gate,
            NativeMethods.GetCurrentProcessId());

        _manager = new NoteManager(new NoteRepository(), _captureGuard);
        _manager.RestoreAll();
        if (_manager.Windows.Count == 0) _manager.CreateNote();

        _hotkeys = new HotkeyService();
        _hotkeys.NewNoteRequested += (_, _) => _manager.CreateNote();
        _hotkeys.ToggleVisibilityRequested += (_, _) => _manager.ToggleVisibility();
        _hotkeys.Register();

        _tray = new TrayController(_manager, _hotkeys, _captureGuard);

        if (!gate.SupportsExcludeFromCapture)
            _tray.ShowWarning(
                "This Windows build does not support WDA_EXCLUDEFROMCAPTURE; " +
                "notes will appear as black boxes in captures (WDA_MONITOR fallback).");

        AttachPopupHandler();

        var sweep = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        sweep.Tick += (_, _) => _captureGuard.Sweep();
        sweep.Start();

        var manager = _manager;
        _signalThread = new Thread(() =>
        {
            try
            {
                while (_instanceGuard.WaitSignal(500))
                    Dispatcher.Invoke(() => manager.ToggleVisibility());
            }
            catch (ObjectDisposedException) { }
        })
        {
            IsBackground = true
        };
        _signalThread.Start();
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
        _manager?.Dispose();
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
