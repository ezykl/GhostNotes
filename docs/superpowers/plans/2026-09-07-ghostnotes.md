# GhostNotes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows 11 WPF sticky-note app whose every window is structurally invisible to software screen capture (Teams/Zoom/Meet/Discord/OBS/screenshots) while fully visible on the physical monitor.

**Architecture:** Tray-resident single-process WPF app; one frameless top-level window per note. Every process window is flagged `WDA_EXCLUDEFROMCAPTURE` at creation and re-asserted by a `CaptureGuard` sweep (popup handler + 1s timer). Notes persist as per-note JSON files in `%APPDATA%\GhostNotes\notes`.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF + WinForms (`NotifyIcon`), Win32 P/Invoke, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-07-ghostnotes-design.md` — the plan argues from the spec; read both.

## Global Constraints

- TargetFramework `net8.0-windows` in BOTH projects; src has `<UseWPF>true</UseWPF>` AND `<UseWindowsForms>true</UseWindowsForms>`; tests have `<UseWPF>true</UseWPF>`. Installed SDK is 10.0.203 — it targets net8.0 fine (targeting pack restores from NuGet). If an offline environment ever blocks the targeting pack, bump both TFM values to `net10.0-windows` together.
- No NuGet packages in src. Tests use exactly: `Microsoft.NET.Test.Sdk` 17.11.1, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2.
- Win32 constants (verbatim): `WDA_NONE=0x00000000`, `WDA_MONITOR=0x00000001`, `WDA_EXCLUDEFROMCAPTURE=0x00000011`, `WS_EX_TOOLWINDOW=0x00000080` (do NOT use `WS_EX_NOACTIVATE` — spec §5: it breaks typing), `GWL_EXSTYLE=-20`, `WM_HOTKEY=0x0312`, `MOD_ALT=0x1`, `MOD_CONTROL=0x2`, `MOD_SHIFT=0x4`, `VK_N=0x4E`, `VK_S=0x53`, `ACCENT_ENABLE_ACRYLICBLURBEHIND=4`, `WCA_ACCENT_POLICY=19`.
- Capture-exclusion value chosen by `VersionGate` (build ≥ 19041 → `WDA_EXCLUDEFROMCAPTURE`, else `WDA_MONITOR`).
- Persistence: `%APPDATA%\GhostNotes\notes\{id}.json`, System.Text.Json, `PropertyNamingPolicy=CamelCase`, `WriteIndented=true`, case-insensitive read.
- UX constants: autosave debounce 500ms; sweep interval 1s; cascade offset 24px; min note 180×120; corner radius 12; font clamp 8–48; defaults — size 320×220, tint `#FFF59D`, opacity 0.85, font 14.
- Hotkeys: New Note `Ctrl+Alt+N`, Show/Hide `Ctrl+Alt+S`; fallbacks add `MOD_SHIFT`. Show/hide re-applies affinity BEFORE `Show()`.
- Every task: run tests/build from repo root; commit only the files listed.
- Tests must not create WPF windows or hit real HWNDs — desktop-bound code is covered by manual smoke steps.

## File Structure

```
/                                        (repo root — already a git repo)
├── GhostNotes.sln
├── src/GhostNotes/
│   ├── GhostNotes.csproj
│   ├── App.xaml / App.xaml.cs           (no StartupUri; tray composition lands in Task 10)
│   ├── NoteWindow.xaml / .cs            (the note: glass, grips, toolbar, context menu)
│   ├── Models/Note.cs                   (POCO, no WPF dependencies)
│   ├── Services/NoteRepository.cs       (file IO + corrupt recovery)
│   ├── Services/AutosaveScheduler.cs    (debounce timer)
│   ├── Services/Clamp.cs                (PositionClamp + FontZoom, pure)
│   ├── Services/HotkeySelection.cs      (pure selection) + HotkeyService.cs (RegisterHotKey wrapper)
│   ├── Services/SingleInstanceGuard.cs  (mutex + signal)
│   ├── Interop/NativeMethods.cs         (P/Invoke + constants)
│   ├── Interop/VersionGate.cs           (build check)
│   ├── Interop/CaptureGuard.cs          (IAffinityApi, IWindowEnumerator, CaptureGuard)
│   ├── NoteManager.cs                   (orchestrates windows + persistence)
│   └── TrayController.cs                (NotifyIcon)
├── tests/GhostNotes.Tests/
│   ├── GhostNotes.Tests.csproj
│   ├── NoteRepositoryTests.cs
│   ├── AutosaveSchedulerTests.cs
│   ├── ClampTests.cs
│   ├── VersionGateTests.cs
│   ├── CaptureGuardTests.cs
│   ├── HotkeySelectionTests.cs
│   └── SingleInstanceGuardTests.cs
└── README.md                             (Task 11)
```

Unit-tested units are pure (file IO, timers, clamps, selection logic, guard state machine via fakes). Window/tray/app code is verified by the smoke steps and the Task 11 checklist.

---

### Task 1: Solution scaffold + `Note` model + `NoteRepository`

**Files:**
- Create: `GhostNotes.sln`, `src/GhostNotes/*`, `tests/GhostNotes.Tests/*`
- Delete: template `src/GhostNotes/MainWindow.xaml`, `MainWindow.xaml.cs`
- Modify: `src/GhostNotes/App.xaml` (remove `StartupUri`)
- Create: `src/GhostNotes/Models/Note.cs`, `src/GhostNotes/Services/NoteRepository.cs`
- Test: `tests/GhostNotes.Tests/NoteRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing (first task)
- Produces:
  - `GhostNotes.Models.Note` — `string Id { get; set; }` (default `Guid.NewGuid().ToString("N")`), `string Rtf`, `double X`, `double Y`, `double Width` (=320), `double Height` (=220), `string Tint` (="#FFF59D"), `double Opacity` (=0.85), `int FontSize` (=14), `DateTime CreatedAt`, `DateTime UpdatedAt` (both UtcNow)
  - `GhostNotes.Services.NoteRepository` — `NoteRepository(string? directory = null)`, `static string DefaultDirectory`, `List<Note> LoadAll()`, `void Save(Note note)`, `void Delete(string id)`

- [ ] **Step 1: Scaffold the solution**

From repo root:

```powershell
dotnet new sln -n GhostNotes
dotnet new wpf -n GhostNotes -o src/GhostNotes
dotnet new xunit -n GhostNotes.Tests -o tests/GhostNotes.Tests
dotnet sln add src/GhostNotes/GhostNotes.csproj tests/GhostNotes.Tests/GhostNotes.Tests.csproj
dotnet add tests/GhostNotes.Tests/GhostNotes.Tests.csproj reference src/GhostNotes/GhostNotes.csproj
```

Delete `src/GhostNotes/MainWindow.xaml` and `src/GhostNotes/MainWindow.xaml.cs`. Replace `src/GhostNotes/GhostNotes.csproj` content with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>GhostNotes</AssemblyName>
    <RootNamespace>GhostNotes</RootNamespace>
  </PropertyGroup>
</Project>
```

Replace `tests/GhostNotes.Tests/GhostNotes.Tests.csproj` content with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GhostNotes\GhostNotes.csproj" />
  </ItemGroup>
</Project>
```

Replace `src/GhostNotes/App.xaml` with (removes `StartupUri`; app composes itself later):

```xml
<Application x:Class="GhostNotes.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Run `dotnet build` — expect success with zero warnings-as-errors issues.

- [ ] **Step 2: Write the model and the failing tests**

Create `src/GhostNotes/Models/Note.cs`:

```csharp
using System;

namespace GhostNotes.Models;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Rtf { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 220;
    public string Tint { get; set; } = "#FFF59D";
    public double Opacity { get; set; } = 0.85;
    public int FontSize { get; set; } = 14;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

Create `tests/GhostNotes.Tests/NoteRepositoryTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using GhostNotes.Models;
using GhostNotes.Services;
using Xunit;

public sealed class NoteRepositoryTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "gn_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var repo = new NoteRepository(_dir);
        var note = new Note
        {
            Rtf = "{\\rtf1\\ansi hello}",
            X = 12.5,
            Y = 40,
            Width = 300,
            Height = 400,
            Tint = "#BBDEFB",
            Opacity = 0.7,
            FontSize = 18
        };
        repo.Save(note);

        var loaded = repo.LoadAll();
        var n = Assert.Single(loaded);
        Assert.Equal(note.Id, n.Id);
        Assert.Equal("{\\rtf1\\ansi hello}", n.Rtf);
        Assert.Equal(12.5, n.X);
        Assert.Equal(40, n.Y);
        Assert.Equal(300, n.Width);
        Assert.Equal(400, n.Height);
        Assert.Equal("#BBDEFB", n.Tint);
        Assert.Equal(0.7, n.Opacity);
        Assert.Equal(18, n.FontSize);
    }

    [Fact]
    public void CorruptFile_RenamedToBad_AndOthersStillLoad()
    {
        Directory.CreateDirectory(_dir);
        var good = JsonSerializer.Serialize(new Note { Rtf = "keep" });
        File.WriteAllText(Path.Combine(_dir, "good.json"), good);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{{{ not json");

        var repo = new NoteRepository(_dir);
        var loaded = repo.LoadAll();

        var n = Assert.Single(loaded);
        Assert.Equal("keep", n.Rtf);
        Assert.True(File.Exists(Path.Combine(_dir, "bad.json.bad")));
        Assert.False(File.Exists(Path.Combine(_dir, "bad.json")));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var repo = new NoteRepository(_dir);
        var note = new Note();
        repo.Save(note);
        repo.Delete(note.Id);
        Assert.Empty(repo.LoadAll());
    }

    [Fact]
    public void LoadAll_MissingDirectory_ReturnsEmpty()
    {
        Assert.Empty(new NoteRepository(_dir).LoadAll());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `NoteRepository` does not exist.

- [ ] **Step 4: Implement the repository**

Create `src/GhostNotes/Services/NoteRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GhostNotes.Models;

namespace GhostNotes.Services;

public sealed class NoteRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GhostNotes", "notes");

    private readonly string _directory;

    public NoteRepository(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory;
    }

    public List<Note> LoadAll()
    {
        var notes = new List<Note>();
        if (!Directory.Exists(_directory)) return notes;
        foreach (var path in Directory.GetFiles(_directory, "*.json"))
        {
            Note? note = null;
            try
            {
                note = JsonSerializer.Deserialize<Note>(File.ReadAllText(path), JsonOpts);
            }
            catch (JsonException) { }
            if (note is null)
            {
                TryRenameCorrupt(path);
                continue;
            }
            notes.Add(note);
        }
        return notes;
    }

    public void Save(Note note)
    {
        note.UpdatedAt = DateTime.UtcNow;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(note.Id), JsonSerializer.Serialize(note, JsonOpts));
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");

    private void TryRenameCorrupt(string path)
    {
        try
        {
            var bad = path + ".bad";
            if (File.Exists(bad)) File.Delete(bad);
            File.Move(path, bad);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```powershell
git add GhostNotes.sln src tests
git commit -m "feat: solution scaffold, Note model, NoteRepository with corrupt-file recovery"
```

---

### Task 2: `AutosaveScheduler` — debounced persistence (TDD)

**Files:**
- Create: `src/GhostNotes/Services/AutosaveScheduler.cs`
- Test: `tests/GhostNotes.Tests/AutosaveSchedulerTests.cs`

**Interfaces:**
- Consumes: none
- Produces: `GhostNotes.Services.AutosaveScheduler` — `AutosaveScheduler(Action flush, TimeSpan delay)`, `void Trigger()`, `void FlushNow()`, `void Cancel()`, `void Dispose()` (Dispose flushes pending; Cancel discards pending)

- [ ] **Step 1: Write the failing tests**

Create `tests/GhostNotes.Tests/AutosaveSchedulerTests.cs`:

```csharp
using System;
using System.Threading;
using GhostNotes.Services;
using Xunit;

public sealed class AutosaveSchedulerTests
{
    [Fact]
    public void Trigger_FlushesAfterDelay()
    {
        using var done = new AutoResetEvent(false);
        using var scheduler = new AutosaveScheduler(() => done.Set(), TimeSpan.FromMilliseconds(50));
        scheduler.Trigger();
        Assert.True(done.WaitOne(2000));
    }

    [Fact]
    public void RepeatedTriggerWithinDelay_SingleFlush()
    {
        int count = 0;
        using var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        scheduler.Trigger();
        Thread.Sleep(20);
        scheduler.Trigger();
        Thread.Sleep(400);
        Assert.Equal(1, count);
    }

    [Fact]
    public void FlushNow_ImmediatelyFlushes_PendingNotRepeated()
    {
        int count = 0;
        using var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        scheduler.Trigger();
        scheduler.FlushNow();
        Assert.Equal(1, count);
        Thread.Sleep(400);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_FlushesPending()
    {
        int count = 0;
        var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        scheduler.Trigger();
        scheduler.Dispose();
        Assert.Equal(1, count);
    }

    [Fact]
    public void Cancel_DiscardsPending()
    {
        int count = 0;
        var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(50));
        scheduler.Trigger();
        scheduler.Cancel();
        scheduler.Dispose();
        Thread.Sleep(300);
        Assert.Equal(0, count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `AutosaveScheduler` does not exist.

- [ ] **Step 3: Implement**

Create `src/GhostNotes/Services/AutosaveScheduler.cs`:

```csharp
using System;
using System.Threading;

namespace GhostNotes.Services;

public sealed class AutosaveScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly Action _flush;
    private readonly TimeSpan _delay;
    private Timer? _timer;
    private bool _pending;
    private bool _disposed;

    public AutosaveScheduler(Action flush, TimeSpan delay)
    {
        _flush = flush;
        _delay = delay;
    }

    public void Trigger()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending = true;
            _timer?.Dispose();
            _timer = new Timer(_ => FlushOnce(), null, _delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void FlushNow()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _timer?.Dispose();
            _timer = null;
        }
        FlushOnce();
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _pending = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _timer?.Dispose();
            _timer = null;
        }
        FlushOnce();
        lock (_sync) { _disposed = true; }
    }

    private void FlushOnce()
    {
        bool run;
        lock (_sync) { run = _pending; _pending = false; }
        if (run) _flush();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 9 passed (4 + 5 new).

- [ ] **Step 5: Commit**

```powershell
git add src/GhostNotes/Services/AutosaveScheduler.cs tests/GhostNotes.Tests/AutosaveSchedulerTests.cs
git commit -m "feat: debounced AutosaveScheduler"
```

---

### Task 3: `PositionClamp` + `FontZoom` — pure helpers (TDD)

**Files:**
- Create: `src/GhostNotes/Services/Clamp.cs`
- Test: `tests/GhostNotes.Tests/ClampTests.cs`

**Interfaces:**
- Consumes: none
- Produces:
  - `static PositionClamp.Clamp(double x, double y, double w, double h, IReadOnlyList<(double X, double Y, double W, double H)> screens)` → `(double X, double Y, double W, double H)` — unchanged if the rect intersects any screen; otherwise moved to nearest screen origin + 24px margin, size shrunk to fit screen − 48px
  - `static FontZoom.Clamp(int current, int delta)` → int, clamped to `[FontZoom.Min=8, FontZoom.Max=48]`

- [ ] **Step 1: Write the failing tests**

Create `tests/GhostNotes.Tests/ClampTests.cs`:

```csharp
using System.Collections.Generic;
using GhostNotes.Services;
using Xunit;

public sealed class ClampTests
{
    private static readonly (double X, double Y, double W, double H)[] One =
        { (0, 0, 1920, 1080) };

    [Fact]
    public void IntersectingScreen_Unchanged()
    {
        var r = PositionClamp.Clamp(100, 100, 320, 220, One);
        Assert.Equal((100, 100, 320, 220), r);
    }

    [Fact]
    public void FullyOffScreenRight_MovedToPrimaryWithMargin()
    {
        var r = PositionClamp.Clamp(5000, 200, 320, 220, One);
        Assert.Equal((24, 24, 320, 220), r);
    }

    [Fact]
    public void NegativeCoordinates_MovedToPrimary()
    {
        var r = PositionClamp.Clamp(-500, -500, 320, 220, One);
        Assert.Equal((24, 24, 320, 220), r);
    }

    [Fact]
    public void NearestOfTwoScreens_Chosen()
    {
        var screens = new (double, double, double, double)[]
        {
            (0, 0, 1920, 1080),
            (1920, 0, 1920, 1080)
        };
        var r = PositionClamp.Clamp(4000, 200, 320, 220, screens);
        Assert.Equal(1944, r.X);
        Assert.Equal(24, r.Y);
    }

    [Fact]
    public void OversizedNote_ShrunkToScreenMinus48()
    {
        var r = PositionClamp.Clamp(5000, 200, 5000, 2000, One);
        Assert.Equal((24, 24, 1872, 1032), r);
    }

    [Theory]
    [InlineData(14, 2, 16)]
    [InlineData(48, 1, 48)]
    [InlineData(8, -1, 8)]
    [InlineData(10, -20, 8)]
    [InlineData(40, 20, 48)]
    public void FontZoom_ClampsBetween8And48(int current, int delta, int expected)
    {
        Assert.Equal(expected, FontZoom.Clamp(current, delta));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `PositionClamp`/`FontZoom` do not exist.

- [ ] **Step 3: Implement**

Create `src/GhostNotes/Services/Clamp.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace GhostNotes.Services;

public static class PositionClamp
{
    private const double Margin = 24;

    public static (double X, double Y, double W, double H) Clamp(
        double x, double y, double w, double h,
        IReadOnlyList<(double X, double Y, double W, double H)> screens)
    {
        var original = (x, y, w, h);
        if (screens.Count == 0) return original;

        foreach (var s in screens)
        {
            bool intersects =
                x < s.X + s.W && x + w > s.X &&
                y < s.Y + s.H && y + h > s.Y;
            if (intersects) return original;
        }

        var best = screens.OrderBy(s => DistanceToScreen(x, y, w, h, s)).First();
        double outW = System.Math.Min(w, System.Math.Max(best.W - 2 * Margin, 180));
        double outH = System.Math.Min(h, System.Math.Max(best.H - 2 * Margin, 120));
        return (best.X + Margin, best.Y + Margin, outW, outH);
    }

    private static double DistanceToScreen(
        double x, double y, double w, double h,
        (double X, double Y, double W, double H) s)
    {
        double dx = x < s.X ? s.X - (x + w) : x > s.X + s.W ? x - (s.X + s.W) : 0;
        double dy = y < s.Y ? s.Y - (y + h) : y > s.Y + s.H ? y - (s.Y + s.H) : 0;
        return dx * dx + dy * dy;
    }
}

public static class FontZoom
{
    public const int Min = 8;
    public const int Max = 48;

    public static int Clamp(int current, int delta) =>
        System.Math.Clamp(current + delta, Min, Max);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 19 passed (9 + 10 new; the FontZoom theory contributes 5 cases).

- [ ] **Step 5: Commit**

```powershell
git add src/GhostNotes/Services/Clamp.cs tests/GhostNotes.Tests/ClampTests.cs
git commit -m "feat: position clamping and font zoom helpers"
```

---

### Task 4: Win32 interop — `NativeMethods` + `VersionGate` (TDD for gate)

**Files:**
- Create: `src/GhostNotes/Interop/NativeMethods.cs`, `src/GhostNotes/Interop/VersionGate.cs`
- Test: `tests/GhostNotes.Tests/VersionGateTests.cs`

**Interfaces:**
- Consumes: none
- Produces:
  - `static NativeMethods` — all constants from Global Constraints plus P/Invokes: `SetWindowDisplayAffinity(IntPtr, uint)`, `GetWindowDisplayAffinity(IntPtr, out uint)`, `RegisterHotKey(IntPtr, int, uint, uint)`, `UnregisterHotKey(IntPtr, int)`, `GetWindowLongPtr(IntPtr, int)`, `SetWindowLongPtr(IntPtr, int, long)`, `EnumWindows(EnumWindowsProc, IntPtr)`, `GetWindowThreadProcessId(IntPtr, out uint)`, `IsWindowVisible(IntPtr)`, `GetCurrentProcessId()`, `SetWindowCompositionAttribute(IntPtr, ref WindowCompositionAttributeData)`, structs `AccentPolicy`/`WindowCompositionAttributeData`, delegate `EnumWindowsProc`
  - `VersionGate(int buildNumber)` — `bool SupportsExcludeFromCapture { get; }` (true iff build ≥ 19041)

- [ ] **Step 1: Write the failing gate tests**

Create `tests/GhostNotes.Tests/VersionGateTests.cs`:

```csharp
using GhostNotes.Interop;
using Xunit;

public sealed class VersionGateTests
{
    [Theory]
    [InlineData(19041, true)]
    [InlineData(19042, true)]
    [InlineData(26200, true)]
    [InlineData(19040, false)]
    [InlineData(7601, false)]
    public void ExcludeFromCapture_SupportMatchesBuildFloor(int build, bool expected)
    {
        Assert.Equal(expected, new VersionGate(build).SupportsExcludeFromCapture);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `VersionGate` does not exist.

- [ ] **Step 3: Implement**

Create `src/GhostNotes/Interop/VersionGate.cs`:

```csharp
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
```

Create `src/GhostNotes/Interop/NativeMethods.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace GhostNotes.Interop;

public static class NativeMethods
{
    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_MONITOR = 0x00000001;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;

    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint VK_N = 0x4E;
    public const uint VK_S = 0x53;

    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    public const int WCA_ACCENT_POLICY = 19;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern long SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentProcessId();

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public int AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 24 passed (19 + 5 new).

- [ ] **Step 5: Commit**

```powershell
git add src/GhostNotes/Interop tests/GhostNotes.Tests/VersionGateTests.cs
git commit -m "feat: Win32 interop — NativeMethods and VersionGate"
```

---

### Task 5: `CaptureGuard` — process-wide capture exclusion (TDD with fakes)

**Files:**
- Create: `src/GhostNotes/Interop/CaptureGuard.cs`
- Test: `tests/GhostNotes.Tests/CaptureGuardTests.cs`

**Interfaces:**
- Consumes: `NativeMethods` (Task 4), `VersionGate` (Task 4)
- Produces:
  - `interface IAffinityApi` — `bool SetAffinity(IntPtr hwnd, uint affinity)`, `bool TryGetAffinity(IntPtr hwnd, out uint affinity)`
  - `sealed NativeAffinityApi : IAffinityApi` (thin wrapper over `NativeMethods`)
  - `interface IWindowEnumerator` — `IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate()`
  - `sealed ProcessWindowEnumerator : IWindowEnumerator` (EnumWindows-based)
  - `sealed CaptureGuard(IAffinityApi api, IWindowEnumerator enumerator, VersionGate gate, uint processId)` — `void ApplyToWindow(IntPtr hwnd)`, `void Forget(IntPtr hwnd)`, `void Sweep()`, `bool IsProtected { get; }`, `event EventHandler<bool>? ProtectionStatusChanged`

- [ ] **Step 1: Write the failing tests**

Create `tests/GhostNotes.Tests/CaptureGuardTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using GhostNotes.Interop;
using Xunit;

public sealed class CaptureGuardTests
{
    private const uint Pid = 42;

    private sealed class FakeApi : IAffinityApi
    {
        public Dictionary<IntPtr, uint> State { get; } = new();
        public HashSet<IntPtr> FailFor { get; } = new();

        public bool SetAffinity(IntPtr hwnd, uint affinity)
        {
            if (FailFor.Contains(hwnd)) return false;
            State[hwnd] = affinity;
            return true;
        }

        public bool TryGetAffinity(IntPtr hwnd, out uint affinity) =>
            State.TryGetValue(hwnd, out affinity);
    }

    private sealed class FakeEnum : IWindowEnumerator
    {
        public List<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Windows { get; } = new();

        public IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate() =>
            Windows;
    }

    private static readonly IntPtr H1 = new(1);

    [Fact]
    public void Sweep_OurVisibleWindow_GetsExcludeFromCapture()
    {
        var api = new FakeApi();
        var windows = new FakeEnum { Windows = { (H1, Pid, true, NativeMethods.WDA_NONE) } };
        var guard = new CaptureGuard(api, windows, new VersionGate(26200), Pid);

        guard.Sweep();

        Assert.Equal(NativeMethods.WDA_EXCLUDEFROMCAPTURE, api.State[H1]);
        Assert.True(guard.IsProtected);
    }

    [Fact]
    public void Sweep_ForeignWindow_Untouched()
    {
        var api = new FakeApi();
        var windows = new FakeEnum { Windows = { (H1, 77, true, NativeMethods.WDA_NONE) } };
        var guard = new CaptureGuard(api, windows, new VersionGate(26200), Pid);

        guard.Sweep();

        Assert.False(api.State.ContainsKey(H1));
        Assert.True(guard.IsProtected);
    }

    [Fact]
    public void ApplyFailure_ReportsUnprotected_AndRaisesEvent()
    {
        var api = new FakeApi { FailFor = { H1 } };
        var windows = new FakeEnum();
        var guard = new CaptureGuard(api, windows, new VersionGate(26200), Pid);
        bool? lastEvent = null;
        guard.ProtectionStatusChanged += (_, on) => lastEvent = on;

        guard.ApplyToWindow(H1);

        Assert.False(guard.IsProtected);
        Assert.False(lastEvent);
    }

    [Fact]
    public void OldBuild_UsesMonitorFallback()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(19040), Pid);

        guard.ApplyToWindow(H1);

        Assert.Equal(NativeMethods.WDA_MONITOR, api.State[H1]);
    }

    [Fact]
    public void ApplyToWindow_AppliesImmediately()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(26200), Pid);

        guard.ApplyToWindow(H1);

        Assert.Equal(NativeMethods.WDA_EXCLUDEFROMCAPTURE, api.State[H1]);
    }

    [Fact]
    public void Sweep_ReassertsResetExplicitWindow()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(26200), Pid);
        guard.ApplyToWindow(H1);

        api.State[H1] = NativeMethods.WDA_NONE;
        guard.Sweep();

        Assert.Equal(NativeMethods.WDA_EXCLUDEFROMCAPTURE, api.State[H1]);
        Assert.True(guard.IsProtected);
    }

    [Fact]
    public void Forget_RemovesWindowFromWatch()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(26200), Pid);
        guard.ApplyToWindow(H1);
        api.State[H1] = NativeMethods.WDA_NONE;

        guard.Forget(H1);
        guard.Sweep();

        Assert.Equal(NativeMethods.WDA_NONE, api.State[H1]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `CaptureGuard`/interfaces do not exist.

- [ ] **Step 3: Implement**

Create `src/GhostNotes/Interop/CaptureGuard.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace GhostNotes.Interop;

public interface IAffinityApi
{
    bool SetAffinity(IntPtr hwnd, uint affinity);
    bool TryGetAffinity(IntPtr hwnd, out uint affinity);
}

public interface IWindowEnumerator
{
    IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate();
}

public sealed class NativeAffinityApi : IAffinityApi
{
    public bool SetAffinity(IntPtr hwnd, uint affinity) =>
        NativeMethods.SetWindowDisplayAffinity(hwnd, affinity);

    public bool TryGetAffinity(IntPtr hwnd, out uint affinity) =>
        NativeMethods.GetWindowDisplayAffinity(hwnd, out affinity);
}

public sealed class ProcessWindowEnumerator : IWindowEnumerator
{
    public IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate()
    {
        var result = new List<(IntPtr, uint, bool, uint)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            bool visible = NativeMethods.IsWindowVisible(hwnd);
            NativeMethods.GetWindowDisplayAffinity(hwnd, out uint affinity);
            result.Add((hwnd, pid, visible, affinity));
            return true;
        }, IntPtr.Zero);
        return result;
    }
}

public sealed class CaptureGuard
{
    private readonly IAffinityApi _api;
    private readonly IWindowEnumerator _enumerator;
    private readonly uint _affinityValue;
    private readonly uint _processId;
    private readonly List<IntPtr> _explicit = new();

    public event EventHandler<bool>? ProtectionStatusChanged;

    public bool IsProtected { get; private set; } = true;

    public CaptureGuard(IAffinityApi api, IWindowEnumerator enumerator, VersionGate gate, uint processId)
    {
        _api = api;
        _enumerator = enumerator;
        _processId = processId;
        _affinityValue = gate.SupportsExcludeFromCapture
            ? NativeMethods.WDA_EXCLUDEFROMCAPTURE
            : NativeMethods.WDA_MONITOR;
    }

    public void ApplyToWindow(IntPtr hwnd)
    {
        lock (_explicit)
        {
            if (!_explicit.Contains(hwnd)) _explicit.Add(hwnd);
        }
        SetStatus(_api.SetAffinity(hwnd, _affinityValue));
    }

    public void Forget(IntPtr hwnd)
    {
        lock (_explicit) { _explicit.Remove(hwnd); }
    }

    public void Sweep()
    {
        bool allOk = true;
        foreach (var (hwnd, pid, visible, affinity) in _enumerator.Enumerate())
        {
            if (pid != _processId || !visible) continue;
            if (affinity != _affinityValue && !_api.SetAffinity(hwnd, _affinityValue))
                allOk = false;
        }
        lock (_explicit)
        {
            foreach (var hwnd in _explicit)
            {
                if (!_api.TryGetAffinity(hwnd, out uint a) || a != _affinityValue)
                {
                    if (!_api.SetAffinity(hwnd, _affinityValue)) allOk = false;
                }
            }
        }
        SetStatus(allOk);
    }

    private void SetStatus(bool ok)
    {
        if (IsProtected == ok) return;
        IsProtected = ok;
        ProtectionStatusChanged?.Invoke(this, ok);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 31 passed (24 + 7 new).

- [ ] **Step 5: Commit**

```powershell
git add src/GhostNotes/Interop/CaptureGuard.cs tests/GhostNotes.Tests/CaptureGuardTests.cs
git commit -m "feat: CaptureGuard — process-wide capture exclusion with status"
```

---

### Task 6: Hotkeys — pure selection (TDD) + `HotkeyService` wrapper

**Files:**
- Create: `src/GhostNotes/Services/HotkeySelection.cs`, `src/GhostNotes/Services/HotkeyService.cs`
- Test: `tests/GhostNotes.Tests/HotkeySelectionTests.cs`

**Interfaces:**
- Consumes: `NativeMethods` (Task 4)
- Produces:
  - `sealed record HotkeySpec(uint Modifiers, uint VirtualKey, string Display)`
  - `static HotkeySelection.Select(HotkeySpec preferred, HotkeySpec fallback, Func<HotkeySpec, bool> isAvailable)` → `HotkeySpec?`
  - `sealed HotkeyService : IDisposable` — `(HotkeySpec NewNote, HotkeySpec Toggle) Register()`, `event EventHandler? NewNoteRequested`, `event EventHandler? ToggleVisibilityRequested`, `event EventHandler<string>? HotkeyNotice`, static specs `PreferredNewNote` (Ctrl+Alt+N), `PreferredToggle` (Ctrl+Alt+S), `FallbackNewNote` (Ctrl+Alt+Shift+N), `FallbackToggle` (Ctrl+Alt+Shift+S)

- [ ] **Step 1: Write the failing selection tests**

Create `tests/GhostNotes.Tests/HotkeySelectionTests.cs`:

```csharp
using System;
using GhostNotes.Services;
using Xunit;

public sealed class HotkeySelectionTests
{
    private static readonly HotkeySpec Preferred = new(2, 0x4E, "Ctrl+Alt+N");
    private static readonly HotkeySpec Fallback = new(6, 0x4E, "Ctrl+Alt+Shift+N");

    [Fact]
    public void PreferredAvailable_ReturnsPreferred()
    {
        Assert.Equal(Preferred, HotkeySelection.Select(Preferred, Fallback, _ => true));
    }

    [Fact]
    public void PreferredTaken_ReturnsFallback()
    {
        Assert.Equal(Fallback, HotkeySelection.Select(Preferred, Fallback, _ => false));
    }

    [Fact]
    public void BothTaken_ReturnsNull()
    {
        Assert.Null(HotkeySelection.Select(Preferred, Fallback, _ => false));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `HotkeySelection` does not exist.

- [ ] **Step 3: Implement selection + service**

Create `src/GhostNotes/Services/HotkeySelection.cs`:

```csharp
using System;
using GhostNotes.Interop;

namespace GhostNotes.Services;

public sealed record HotkeySpec(uint Modifiers, uint VirtualKey, string Display);

public static class HotkeySelection
{
    public static HotkeySpec? Select(
        HotkeySpec preferred,
        HotkeySpec fallback,
        Func<HotkeySpec, bool> isAvailable)
    {
        if (isAvailable(preferred)) return preferred;
        if (isAvailable(fallback)) return fallback;
        return null;
    }
}
```

Create `src/GhostNotes/Services/HotkeyService.cs`:

```csharp
using System;
using System.Windows.Interop;
using GhostNotes.Interop;

namespace GhostNotes.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyIdNewNote = 0xB001;
    private const int HotkeyIdToggle = 0xB002;

    public static readonly HotkeySpec PreferredNewNote =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_N, "Ctrl+Alt+N");
    public static readonly HotkeySpec PreferredToggle =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_S, "Ctrl+Alt+S");
    public static readonly HotkeySpec FallbackNewNote =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
            NativeMethods.VK_N, "Ctrl+Alt+Shift+N");
    public static readonly HotkeySpec FallbackToggle =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
            NativeMethods.VK_S, "Ctrl+Alt+Shift+S");

    public event EventHandler? NewNoteRequested;
    public event EventHandler? ToggleVisibilityRequested;
    public event EventHandler<string>? HotkeyNotice;

    private HwndSource? _source;

    public (HotkeySpec NewNote, HotkeySpec Toggle) Register()
    {
        _source = new HwndSource(new HwndSourceParameters("GhostNotesHotkeys")
        {
            Width = 0,
            Height = 0
        });
        _source.AddHook(WndProc);

        var specNew = HotkeySelection.Select(PreferredNewNote, FallbackNewNote,
            s => TryRegister(HotkeyIdNewNote, s));
        var specToggle = HotkeySelection.Select(PreferredToggle, FallbackToggle,
            s => TryRegister(HotkeyIdToggle, s));

        if (specNew is null)
            HotkeyNotice?.Invoke(this, "Could not register the New Note hotkey — both combos are taken.");
        if (specToggle is null)
            HotkeyNotice?.Invoke(this, "Could not register the Show/Hide hotkey — both combos are taken.");
        if (specNew is not null && specNew != PreferredNewNote)
            HotkeyNotice?.Invoke(this, $"New Note hotkey fell back to {specNew.Display}.");
        if (specToggle is not null && specToggle != PreferredToggle)
            HotkeyNotice?.Invoke(this, $"Show/Hide hotkey fell back to {specToggle.Display}.");

        return (specNew ?? PreferredNewNote, specToggle ?? PreferredToggle);
    }

    private bool TryRegister(int id, HotkeySpec spec) =>
        NativeMethods.RegisterHotKey(_source!.Handle, id, spec.Modifiers, spec.VirtualKey);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            switch (wParam.ToInt64())
            {
                case HotkeyIdNewNote:
                    NewNoteRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HotkeyIdToggle:
                    ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyIdNewNote);
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyIdToggle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 34 passed (31 + 3 new).

- [ ] **Step 5: Commit**

```powershell
git add src/GhostNotes/Services/HotkeySelection.cs src/GhostNotes/Services/HotkeyService.cs tests/GhostNotes.Tests/HotkeySelectionTests.cs
git commit -m "feat: hotkey selection and global hotkey service"
```

---

### Task 7: `NoteWindow` shell — glass, drag, resize, responsive text, protection

**Files:**
- Create: `src/GhostNotes/NoteWindow.xaml`, `src/GhostNotes/NoteWindow.xaml.cs`
- Modify: `src/GhostNotes/App.xaml.cs` (temporary smoke composition — replaced in Tasks 9/10)

**Interfaces:**
- Consumes: `Note` (Task 1), `CaptureGuard` (Task 5), `NativeMethods` (Task 4), `FontZoom` (Task 3)
- Produces: `NoteWindow(Note note, CaptureGuard guard)` — `Note Model { get; }`, `void ReapplyProtectionAndShow()`, events `ModelChanged`, `GeometryChanged`, `DeleteRequested`, `NewNoteRequested` (all `EventHandler`)

No unit tests (requires a real desktop). Verification is the smoke step.

- [ ] **Step 1: Write the XAML**

Create `src/GhostNotes/NoteWindow.xaml`:

```xml
<Window x:Class="GhostNotes.NoteWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize"
        MinWidth="180"
        MinHeight="120"
        Title="GhostNote">
    <Window.Resources>
        <Style x:Key="Grip" TargetType="Thumb">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Thumb">
                        <Rectangle Fill="Transparent"/>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Grid>
        <Border x:Name="Glass" CornerRadius="12" BorderBrush="#80FFFFFF" BorderThickness="1">
            <Border.Effect>
                <DropShadowEffect BlurRadius="16" ShadowDepth="2" Opacity="0.35"/>
            </Border.Effect>
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="26"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <Border x:Name="DragStrip" Grid.Row="0" Cursor="SizeAll" CornerRadius="12,12,0,0"
                        MouseLeftButtonDown="OnDragStripDown"/>
                <RichTextBox x:Name="Body" Grid.Row="1"
                             AcceptsReturn="True"
                             AcceptsTab="True"
                             Background="Transparent"
                             BorderThickness="0"
                             Padding="8,4,8,8"
                             HorizontalScrollBarVisibility="Hidden"
                             VerticalScrollBarVisibility="Auto"
                             TextChanged="OnBodyTextChanged"/>
            </Grid>
        </Border>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition/>
                <ColumnDefinition Width="8"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="8"/>
                <RowDefinition/>
                <RowDefinition Height="8"/>
            </Grid.RowDefinitions>
            <Thumb Grid.Row="0" Grid.Column="0" Style="{StaticResource Grip}" Cursor="SizeNWSE" Tag="NW" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="0" Grid.Column="1" Style="{StaticResource Grip}" Cursor="SizeNS" Tag="N" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="0" Grid.Column="2" Style="{StaticResource Grip}" Cursor="SizeNESW" Tag="NE" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="1" Grid.Column="0" Style="{StaticResource Grip}" Cursor="SizeWE" Tag="W" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="1" Grid.Column="2" Style="{StaticResource Grip}" Cursor="SizeWE" Tag="E" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="2" Grid.Column="0" Style="{StaticResource Grip}" Cursor="SizeNESW" Tag="SW" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="2" Grid.Column="1" Style="{StaticResource Grip}" Cursor="SizeNS" Tag="S" DragDelta="OnResizeDelta"/>
            <Thumb Grid.Row="2" Grid.Column="2" Style="{StaticResource Grip}" Cursor="SizeNWSE" Tag="SE" DragDelta="OnResizeDelta"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Write the code-behind**

Create `src/GhostNotes/NoteWindow.xaml.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;

namespace GhostNotes;

public partial class NoteWindow : Window
{
    private readonly CaptureGuard _guard;
    private IntPtr _accentPtr = IntPtr.Zero;
    private bool _suppressTextEvents;

    public Note Model { get; }

    public event EventHandler? ModelChanged;
    public event EventHandler? GeometryChanged;
    public event EventHandler? DeleteRequested;
    public event EventHandler? NewNoteRequested;

    public NoteWindow(Note note, CaptureGuard guard)
    {
        InitializeComponent();
        Model = note;
        _guard = guard;
        Left = note.X;
        Top = note.Y;
        Width = note.Width;
        Height = note.Height;
        Body.FontSize = Math.Clamp(note.FontSize, FontZoom.Min, FontZoom.Max);
        LoadRtf(note.Rtf);
        ApplyGlassBackground();
        LocationChanged += (_, _) => SyncGeometry();
        SizeChanged += (_, _) => SyncGeometry();
        SourceInitialized += OnSourceInitialized;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW);
        _guard.ApplyToWindow(hwnd);
        TryAcrylic(hwnd);
    }

    public void ReapplyProtectionAndShow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) _guard.ApplyToWindow(hwnd);
        Show();
    }

    private void SyncGeometry()
    {
        Model.X = Left;
        Model.Y = Top;
        Model.Width = Width;
        Model.Height = Height;
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragStripDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        if (!ctrl) return;
        e.Handled = true;
        int next = FontZoom.Clamp((int)Body.FontSize, e.Delta > 0 ? 1 : -1);
        if (next == (int)Body.FontSize) return;
        Body.FontSize = next;
        Model.FontSize = next;
        Body.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)next);
        Model.Rtf = SaveRtf();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBodyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressTextEvents) return;
        Model.Rtf = SaveRtf();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not string dir) return;
        double left = Left, top = Top, w = Width, h = Height;
        if (dir.Contains('W'))
        {
            w = Math.Max(Width - e.HorizontalChange, MinWidth);
            left = Left + Width - w;
        }
        if (dir.Contains('E')) w = Math.Max(Width + e.HorizontalChange, MinWidth);
        if (dir.Contains('N'))
        {
            h = Math.Max(Height - e.VerticalChange, MinHeight);
            top = Top + Height - h;
        }
        if (dir.Contains('S')) h = Math.Max(Height + e.VerticalChange, MinHeight);
        Left = left;
        Top = top;
        Width = w;
        Height = h;
    }

    private void ApplyGlassBackground()
    {
        var color = (Color)ColorConverter.ConvertFromString(Model.Tint);
        byte alpha = (byte)Math.Round(Model.Opacity * 255);
        Glass.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private void TryAcrylic(IntPtr hwnd)
    {
        if (_accentPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_accentPtr);
            _accentPtr = IntPtr.Zero;
        }
        int size = Marshal.SizeOf(typeof(NativeMethods.AccentPolicy));
        var accent = new NativeMethods.AccentPolicy
        {
            AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = TintToAbgr(Model.Tint, 0x66)
        };
        _accentPtr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(accent, _accentPtr, false);
        var data = new NativeMethods.WindowCompositionAttributeData
        {
            Attribute = NativeMethods.WCA_ACCENT_POLICY,
            Data = _accentPtr,
            SizeOfData = size
        };
        if (NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) != 0)
        {
            Marshal.FreeHGlobal(_accentPtr);
            _accentPtr = IntPtr.Zero;
        }
    }

    private static uint TintToAbgr(string hex, byte alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return (uint)((alpha << 24) | (c.B << 16) | (c.G << 8) | c.R);
    }

    private void LoadRtf(string rtf)
    {
        _suppressTextEvents = true;
        try
        {
            var range = new TextRange(Body.Document.ContentStart, Body.Document.ContentEnd);
            if (string.IsNullOrEmpty(rtf))
            {
                range.Text = "";
            }
            else
            {
                using var ms = new MemoryStream(Encoding.Default.GetBytes(rtf));
                range.Load(ms, DataFormats.Rtf);
            }
        }
        catch (ArgumentException)
        {
            var range = new TextRange(Body.Document.ContentStart, Body.Document.ContentEnd);
            range.Text = "";
        }
        finally
        {
            _suppressTextEvents = false;
        }
    }

    private string SaveRtf()
    {
        var range = new TextRange(Body.Document.ContentStart, Body.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        return Encoding.Default.GetString(ms.ToArray());
    }
}
```

- [ ] **Step 3: Add temporary smoke composition**

Replace `src/GhostNotes/App.xaml.cs` with:

```csharp
using System;
using System.Windows;
using GhostNotes.Interop;
using GhostNotes.Models;

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
```

Run `dotnet build` — expect success.

- [ ] **Step 4: Smoke test**

Run: `dotnet run --project src/GhostNotes`
Verify:
1. A translucent yellow rounded note appears, floating above other windows
2. Drag via the top strip moves it; all 8 edges/corners resize (min 180×120)
3. Text wraps and reflows live while resizing; no horizontal scrollbar
4. Ctrl+wheel grows/shrinks font and stops at 8 and 48
5. OBS (or Snipping Tool preview) does NOT show the note; physical monitor does
6. Alt-Tab does not list it; taskbar does not show it
7. Stop with Ctrl+C in the terminal (default `ShutdownMode` exits when the window closes)

If OBS shows the note: stop and debug `OnSourceInitialized` (affinity call return value) before continuing.

- [ ] **Step 5: Commit**

```powershell
git add src/GhostNotes/NoteWindow.xaml src/GhostNotes/NoteWindow.xaml.cs src/GhostNotes/App.xaml.cs
git commit -m "feat: NoteWindow shell — glass, drag, resize, zoom, capture protection"
```

---

### Task 8: Rich text toolbar + context menu

**Files:**
- Modify: `src/GhostNotes/NoteWindow.xaml` (insert toolbar row)
- Modify: `src/GhostNotes/NoteWindow.xaml.cs` (toolbar handlers, context menu, tint/opacity)

**Interfaces:**
- Consumes: Task 7's `NoteWindow` (events `ModelChanged`/`DeleteRequested`/`NewNoteRequested` are raised here)
- Produces: toolbar (bold/italic/underline/text color/note color) and context menu (New Note, Delete Note, Opacity slider, Reset Font Size) on `NoteWindow`; no new public members

- [ ] **Step 1: Insert the toolbar into the XAML**

In `src/GhostNotes/NoteWindow.xaml`, replace the inner Grid row definitions and add the toolbar between `DragStrip` and `Body`:

Replace:

```xml
                <Grid.RowDefinitions>
                    <RowDefinition Height="26"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <Border x:Name="DragStrip" Grid.Row="0" Cursor="SizeAll" CornerRadius="12,12,0,0"
                        MouseLeftButtonDown="OnDragStripDown"/>
                <RichTextBox x:Name="Body" Grid.Row="1"
```

With:

```xml
                <Grid.RowDefinitions>
                    <RowDefinition Height="26"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <Border x:Name="DragStrip" Grid.Row="0" Cursor="SizeAll" CornerRadius="12,12,0,0"
                        MouseLeftButtonDown="OnDragStripDown"/>
                <ToolBarTray Grid.Row="1" IsLocked="True" Background="Transparent">
                    <ToolBar Band="0" BandIndex="0" Background="Transparent">
                        <ToggleButton x:Name="BtnBold" Content="B" FontWeight="Bold"
                                      Click="OnToggleBold"/>
                        <ToggleButton x:Name="BtnItalic" Content="I" FontStyle="Italic"
                                      Click="OnToggleItalic"/>
                        <ToggleButton x:Name="BtnUnderline" Content="U"
                                      TextDecorations="Underline" Click="OnToggleUnderline"/>
                        <Separator/>
                        <ComboBox x:Name="TextColors" Width="70" ToolTip="Text color"
                                  SelectionChanged="OnTextColorSelected">
                            <ComboBoxItem Content="Black" Tag="#DD000000" IsSelected="True"/>
                            <ComboBoxItem Content="White" Tag="#FFFFFFFF"/>
                            <ComboBoxItem Content="Red" Tag="#DDCC0000"/>
                            <ComboBoxItem Content="Blue" Tag="#DD1A5FB4"/>
                            <ComboBoxItem Content="Green" Tag="#DD2E7D32"/>
                        </ComboBox>
                        <ComboBox x:Name="Tints" Width="80" ToolTip="Note color"
                                  SelectionChanged="OnTintSelected">
                            <ComboBoxItem Content="Yellow" Tag="#FFF59D" IsSelected="True"/>
                            <ComboBoxItem Content="Blue" Tag="#BBDEFB"/>
                            <ComboBoxItem Content="Green" Tag="#C8E6C9"/>
                            <ComboBoxItem Content="Pink" Tag="#F8BBD0"/>
                            <ComboBoxItem Content="Purple" Tag="#D1C4E9"/>
                            <ComboBoxItem Content="White" Tag="#FFFFFF"/>
                        </ComboBox>
                    </ToolBar>
                </ToolBarTray>
                <RichTextBox x:Name="Body" Grid.Row="2"
```

Keep the remaining `RichTextBox` attributes exactly as in Task 7.

- [ ] **Step 2: Add handlers to the code-behind**

Append to `src/GhostNotes/NoteWindow.xaml.cs` (inside the class):

```csharp
    private void OnToggleBold(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleBold.Execute(null, Body);

    private void OnToggleItalic(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleItalic.Execute(null, Body);

    private void OnToggleUnderline(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleUnderline.Execute(null, Body);

    private void OnTextColorSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TextColors.SelectedItem is ComboBoxItem item && item.Tag is string hex)
        {
            Body.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, BrushFrom(hex));
            Model.Rtf = SaveRtf();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTintSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Tints.SelectedItem is ComboBoxItem item && item.Tag is string hex)
        {
            Model.Tint = hex;
            ApplyGlassBackground();
            RefreshAcrylic();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshAcrylic()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) TryAcrylic(hwnd);
    }

    private static Brush BrushFrom(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var newNote = new MenuItem { Header = "New Note" };
        newNote.Click += (_, _) => NewNoteRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(newNote);

        var delete = new MenuItem { Header = "Delete Note" };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(delete);

        var opacityItem = new MenuItem { StaysOpenOnClick = true };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = "Opacity  ", VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { MinWidth = 120, Minimum = 0.3, Maximum = 1.0, Value = Model.Opacity };
        slider.ValueChanged += (_, e2) =>
        {
            Model.Opacity = Math.Round(e2.NewValue, 2);
            ApplyGlassBackground();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        };
        panel.Children.Add(slider);
        opacityItem.Header = panel;
        menu.Items.Add(opacityItem);

        var resetFont = new MenuItem { Header = "Reset Font Size" };
        resetFont.Click += (_, _) =>
        {
            Body.FontSize = 14;
            Model.FontSize = 14;
            Body.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
            Model.Rtf = SaveRtf();
            ModelChanged?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(resetFont);

        return menu;
    }
```

And in the constructor, after `PreviewMouseWheel += OnPreviewMouseWheel;` add:

```csharp
        ContextMenu = BuildContextMenu();
        Body.ContextMenu = null;
```

(`Body.ContextMenu = null` removes the RichTextBox default clipboard menu so right-click always opens ours.)

- [ ] **Step 3: Build and smoke test**

Run: `dotnet build` then `dotnet run --project src/GhostNotes`
Verify:
1. Bold/italic/underline toggle on selected text; toggle buttons reflect the caret's current formatting after moving the caret
2. Text color and note color combos work; note color changes glass tint live; opacity slider changes translucency live
3. Right-click anywhere (including inside text) opens the GhostNotes menu; its items are enabled
4. Formatting survives: close the note (app exits), re-run — the note reloads (persistence lands in Task 9; formatting of RTF in-model is already saved via `ModelChanged` plumbing)
5. **Capture check:** while OBS preview is open, open the context menu and the two combos — none of these popups may appear in the preview (Task 10's popup handler hardens this; the 1s sweep is not wired yet, so this smoke checks the popup HWNDs get flagged when Task 10 lands — if popups DO appear now, that is expected to be fixed by Task 10, not here)

If popups leak in OBS before Task 10, proceed — Task 10's popup handler plus sweep covers it; final verification is the Task 11 checklist.

- [ ] **Step 4: Commit**

```powershell
git add src/GhostNotes/NoteWindow.xaml src/GhostNotes/NoteWindow.xaml.cs
git commit -m "feat: rich text toolbar and context menu"
```

---

### Task 9: `NoteManager` — orchestration, restore, cascade, delete, toggle, autosave

**Files:**
- Create: `src/GhostNotes/NoteManager.cs`
- Modify: `src/GhostNotes/App.xaml.cs` (smoke composition with persistence)

**Interfaces:**
- Consumes: `NoteWindow` events (Task 7/8), `NoteRepository` (Task 1), `AutosaveScheduler` (Task 2), `PositionClamp` (Task 3), `CaptureGuard` (Task 5)
- Produces: `sealed NoteManager(NoteRepository repo, CaptureGuard guard) : IDisposable` — `IReadOnlyList<NoteWindow> Windows`, `bool AnyVisible`, `void RestoreAll()`, `NoteWindow CreateNote()`, `void DeleteNote(NoteWindow window)`, `void ToggleVisibility()`, `void SaveAll()`

- [ ] **Step 1: Implement the manager**

Create `src/GhostNotes/NoteManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using GhostNotes.Interop;
using GhostNotes.Models;
using GhostNotes.Services;

namespace GhostNotes;

public sealed class NoteManager : IDisposable
{
    private const double CascadeOffset = 24;

    private readonly NoteRepository _repo;
    private readonly CaptureGuard _guard;
    private readonly List<NoteWindow> _windows = new();
    private readonly Dictionary<string, AutosaveScheduler> _schedulers = new();
    private Point _nextCascade = new(96, 96);

    public IReadOnlyList<NoteWindow> Windows => _windows;

    public bool AnyVisible
    {
        get
        {
            foreach (var w in _windows)
                if (w.IsVisible) return true;
            return false;
        }
    }

    public NoteManager(NoteRepository repo, CaptureGuard guard)
    {
        _repo = repo;
        _guard = guard;
    }

    public void RestoreAll()
    {
        foreach (var note in _repo.LoadAll())
        {
            var c = PositionClamp.Clamp(note.X, note.Y, note.Width, note.Height, ScreenRects());
            note.X = c.X;
            note.Y = c.Y;
            note.Width = c.W;
            note.Height = c.H;
            Attach(new NoteWindow(note, _guard));
        }
    }

    public NoteWindow CreateNote()
    {
        var note = new Note { X = _nextCascade.X, Y = _nextCascade.Y };
        _repo.Save(note);
        var window = Attach(new NoteWindow(note, _guard));
        _nextCascade = new Point(
            (_nextCascade.X + CascadeOffset) % SystemParameters.WorkArea.Width,
            (_nextCascade.Y + CascadeOffset) % SystemParameters.WorkArea.Height);
        window.Activate();
        return window;
    }

    public void DeleteNote(NoteWindow window)
    {
        _repo.Delete(window.Model.Id);
        if (_schedulers.Remove(window.Model.Id, out var scheduler))
        {
            scheduler.Cancel();
            scheduler.Dispose();
        }
        _windows.Remove(window);
        window.Close();
    }

    public void ToggleVisibility()
    {
        if (AnyVisible)
        {
            foreach (var w in _windows) w.Hide();
        }
        else
        {
            foreach (var w in _windows) w.ReapplyProtectionAndShow();
        }
    }

    public void SaveAll()
    {
        foreach (var scheduler in _schedulers.Values) scheduler.FlushNow();
    }

    public void Dispose() => SaveAll();

    private NoteWindow Attach(NoteWindow window)
    {
        _windows.Add(window);
        window.ModelChanged += (_, _) => OnNoteModelChanged(window.Model);
        window.GeometryChanged += (_, _) => OnNoteModelChanged(window.Model);
        window.DeleteRequested += (_, _) => DeleteNote(window);
        window.NewNoteRequested += (_, _) => CreateNote();
        window.Show();
        return window;
    }

    private void OnNoteModelChanged(Note note)
    {
        if (!_schedulers.TryGetValue(note.Id, out var scheduler))
        {
            scheduler = new AutosaveScheduler(() => _repo.Save(note), TimeSpan.FromMilliseconds(500));
            _schedulers[note.Id] = scheduler;
        }
        scheduler.Trigger();
    }

    private static (double X, double Y, double W, double H)[] ScreenRects()
    {
        var list = new List<(double, double, double, double)>();
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
            list.Add((s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height));
        return list.ToArray();
    }
}
```

- [ ] **Step 2: Rewire the smoke composition**

Replace `src/GhostNotes/App.xaml.cs` with:

```csharp
using System;
using System.Windows;
using GhostNotes.Interop;
using GhostNotes.Services;

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
```

Run `dotnet build` — expect success. Run all tests: `dotnet test tests/GhostNotes.Tests` — 34 passed (no new tests; orchestration is desktop-bound).

- [ ] **Step 3: Smoke test**

Run: `dotnet run --project src/GhostNotes`
Verify:
1. First run: one note appears near (96,96) and `%APPDATA%\GhostNotes\notes\` contains one JSON file
2. Context menu → New Note: a second note appears offset 24px; its JSON file exists immediately
3. Type text, move, resize, wait ~1s: JSON reflects content/position/size (open the file to check)
4. Close the note window → app exits → re-run: both notes restore at their saved positions with formatted content
5. Delete Note: window closes and its JSON file is gone; re-run: only the other note returns
6. Off-screen clamp: edit one note's JSON `"x": 50000`, re-run → note appears at the primary screen's top-left with margin
7. Abort test: type text, `taskkill /IM GhostNotes.exe /F` within the debounce window, re-run → last text within ~0.5s survives; older text always survives

- [ ] **Step 4: Commit**

```powershell
git add src/GhostNotes/NoteManager.cs src/GhostNotes/App.xaml.cs
git commit -m "feat: NoteManager — restore, cascade, delete, toggle, autosave"
```

---

### Task 10: Single-instance guard (TDD) + tray + full app composition

**Files:**
- Create: `src/GhostNotes/Services/SingleInstanceGuard.cs`, `src/GhostNotes/TrayController.cs`
- Modify: `src/GhostNotes/App.xaml` (`ShutdownMode="OnExplicitShutdown"`), `src/GhostNotes/App.xaml.cs` (final composition)
- Test: `tests/GhostNotes.Tests/SingleInstanceGuardTests.cs`

**Interfaces:**
- Consumes: `NoteManager` (Task 9), `HotkeyService` (Task 6), `CaptureGuard` (Task 5), `SingleInstanceGuard`, `TrayController`
- Produces:
  - `sealed SingleInstanceGuard(string mutexName, string signalName = "GhostNotes_ShowSignal") : IDisposable` — `bool IsFirst`, `bool WaitSignal(int ms)`, `static void SignalFirstInstance(string signalName = "GhostNotes_ShowSignal")`, consts `DefaultMutexName = "GhostNotes_SingleInstance_Mutex"`, `DefaultSignalName = "GhostNotes_ShowSignal"`
  - `sealed TrayController(NoteManager manager, HotkeyService hotkeys, CaptureGuard guard) : IDisposable` — `void UpdateTooltip(bool protectionOn)`, `void ShowWarning(string message)`

- [ ] **Step 1: Write the failing single-instance tests**

Create `tests/GhostNotes.Tests/SingleInstanceGuardTests.cs`:

```csharp
using System;
using System.Threading;
using GhostNotes.Services;
using Xunit;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void FirstAcquires_SecondDenied_ReleasedThenReacquired()
    {
        string name = "GhostNotes_T_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name, name + "_sig");
        Assert.True(first.IsFirst);

        using (var second = new SingleInstanceGuard(name, name + "_sig"))
            Assert.False(second.IsFirst);

        first.Dispose();
        using var third = new SingleInstanceGuard(name, name + "_sig");
        Assert.True(third.IsFirst);
    }

    [Fact]
    public void Signal_WakesFirstInstance()
    {
        string name = "GhostNotes_T_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name, name + "_sig");
        SingleInstanceGuard.SignalFirstInstance(name + "_sig");
        Assert.True(first.WaitSignal(2000));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: compile failure — `SingleInstanceGuard` does not exist.

- [ ] **Step 3: Implement the guard, tray, and final app**

Create `src/GhostNotes/Services/SingleInstanceGuard.cs`:

```csharp
using System;
using System.Threading;

namespace GhostNotes.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = "GhostNotes_SingleInstance_Mutex";
    public const string DefaultSignalName = "GhostNotes_ShowSignal";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _signal;

    public bool IsFirst { get; }

    public SingleInstanceGuard(string mutexName, string signalName = DefaultSignalName)
    {
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        IsFirst = createdNew;
        _signal = IsFirst
            ? new EventWaitHandle(false, EventResetMode.AutoReset, signalName)
            : null;
    }

    public static void SignalFirstInstance(string signalName = DefaultSignalName)
    {
        if (EventWaitHandle.TryOpenExisting(signalName, out EventWaitHandle? handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    public bool WaitSignal(int millisecondsTimeout) =>
        _signal!.WaitOne(millisecondsTimeout);

    public void Dispose()
    {
        _signal?.Dispose();
        try
        {
            if (IsFirst) _mutex.ReleaseMutex();
        }
        catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
```

Create `src/GhostNotes/TrayController.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GhostNotes.Interop;
using GhostNotes.Services;
using Application = System.Windows.Application;

namespace GhostNotes;

public sealed class TrayController : IDisposable
{
    private readonly NoteManager _manager;
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayController(NoteManager manager, HotkeyService hotkeys, CaptureGuard guard)
    {
        _manager = manager;
        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "GhostNotes",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        hotkeys.HotkeyNotice += (_, message) => ShowWarning(message);
        guard.ProtectionStatusChanged += (_, on) => UpdateTooltip(on);
        UpdateTooltip(guard.IsProtected);
    }

    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 245, 157));
            g.FillRectangle(brush, 1, 1, 13, 13);
            using var pen = new Pen(Color.FromArgb(90, 70, 0), 2);
            g.DrawRectangle(pen, 1, 1, 13, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("New Note", null, (_, _) => _manager.CreateNote()));
        menu.Items.Add(new ToolStripMenuItem("Show/Hide All Notes", null, (_, _) => _manager.ToggleVisibility()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()));
        return menu;
    }

    public void UpdateTooltip(bool protectionOn)
    {
        _icon.Text =
            $"GhostNotes — {_manager.Windows.Count} notes — Capture protection: {(protectionOn ? "ON" : "OFF")}";
    }

    public void ShowWarning(string message) =>
        _icon.ShowBalloonTip(5000, "GhostNotes", message, ToolTipIcon.Warning);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

Replace `src/GhostNotes/App.xaml` with:

```xml
<Application x:Class="GhostNotes.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Replace `src/GhostNotes/App.xaml.cs` with:

```csharp
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using GhostNotes.Interop;
using GhostNotes.Services;

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
            typeof(Popup),
            Popup.OpenedEvent,
            new RoutedEventHandler(OnAnyPopupOpened));
    }

    private void OnAnyPopupOpened(object sender, RoutedEventArgs e)
    {
        var guard = _captureGuard;
        if (sender is not Popup popup || popup.Child is null || guard is null) return;

        void Flag(object? child)
        {
            if (child is Visual v && PresentationSource.FromVisual(v) is HwndSource src)
                guard.ApplyToWindow(src.Handle);
        }

        if (popup.Child.IsLoaded) Flag(popup.Child);
        popup.Child.Loaded += (_, _) => Flag(popup.Child);
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
```

- [ ] **Step 4: Run tests and build**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 36 passed (34 + 2 new). Run `dotnet build` — expect success.

- [ ] **Step 5: Smoke test**

Run: `dotnet run --project src/GhostNotes`
Verify:
1. Tray icon (yellow square) with tooltip "GhostNotes — N notes — Capture protection: ON"
2. Tray menu: New Note / Show/Hide All Notes / Exit all work
3. `Ctrl+Alt+N` creates a note; `Ctrl+Alt+S` hides all, presses again to restore (notes never flash into an OBS preview on restore)
4. Launch a second instance (`dotnet run --project src/GhostNotes` from another terminal): it exits immediately and the first instance toggles note visibility
5. OBS preview while right-clicking a note: the context menu does NOT appear in the preview
6. Tray → Exit: app exits cleanly (no lingering process, tray icon gone)

- [ ] **Step 6: Commit**

```powershell
git add src/GhostNotes/Services/SingleInstanceGuard.cs src/GhostNotes/TrayController.cs src/GhostNotes/App.xaml src/GhostNotes/App.xaml.cs tests/GhostNotes.Tests/SingleInstanceGuardTests.cs
git commit -m "feat: tray, single-instance, hotkey wiring, app composition"
```

---

### Task 11: Publish, README, verification checklist

**Files:**
- Create: `README.md`
- No source changes expected

**Interfaces:**
- Consumes: everything (this task verifies the shipped artifact)
- Produces: `publish/GhostNotes.exe` (gitignored) and a completed verification report in chat

- [ ] **Step 1: Publish the single-file exe**

```powershell
dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Expect `publish/GhostNotes.exe` (self-contained, no runtime needed).

- [ ] **Step 2: Write the README**

Create `README.md`:

```markdown
# GhostNotes

Sticky notes for Windows that are always invisible to software screen capture —
web-conference sharing (Teams, Zoom, Google Meet, Discord), recorders (OBS,
Xbox Game Bar), and screenshots (Print Screen, Snipping Tool) — while fully
visible and editable on your own monitor.

## Run

    dotnet run --project src/GhostNotes

## Publish a single exe

    dotnet publish src/GhostNotes/GhostNotes.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish

## Use

- Tray menu (yellow square): New Note / Show/Hide All Notes / Exit
- Ctrl+Alt+N — new note
- Ctrl+Alt+S — show/hide all notes (your view only; capture exclusion is always on)
- Toolbar: bold / italic / underline / text color / note color
- Right-click: New Note / Delete Note / Opacity / Reset Font Size
- Drag the top strip to move; drag any edge or corner to resize
- Ctrl+mouse wheel changes font size (8–48)
- Notes autosave to %APPDATA%\GhostNotes\notes and survive restarts

## How it works

Every window the app creates is flagged with
`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`. The Windows
compositor (DWM) renders those windows to the physical display but omits them
from every software capture surface. A background guard re-asserts the flag
every second and on every popup, and the tray tooltip reports live protection
status. Notes are also `WS_EX_TOOLWINDOW`, so they never appear in Alt-Tab or
the taskbar.

## What this does NOT protect against (honest limits)

- A phone, camera, or person viewing your physical monitor
- An HDMI capture card between the GPU and monitor
- The process listing in Task Manager (it is there, by name)
- The tray icon if you share your entire screen including the taskbar

This uses a Microsoft-documented content-protection API, not DRM; it is a
privacy convenience, not a security boundary.

## Verification checklist (run per release)

1. OBS preview open → note absent; desktop behind shows through
2. Teams/Zoom share viewed from a second device → note absent
3. Snipping Tool and Print Screen → note absent
4. Physical monitor → note visible and editable
5. Alt-Tab and taskbar → no GhostNotes entries
6. Right-click a note while OBS records → context menu absent from recording
7. Ctrl+Alt+S twice → notes hide and return; still absent from OBS
8. `taskkill /IM GhostNotes.exe /F` mid-edit → restart restores last ~0.5s of edits
```

- [ ] **Step 3: Execute the verification checklist against the published exe**

Run `publish\GhostNotes.exe` and perform all 8 checklist items from the README (OBS, a phone-watched Teams/Zoom share, Snipping Tool, Print Screen, Alt-Tab, context menu during recording, hotkey round-trip, taskkill/restore). Record any failure as a bug — do not ship with a failed item 1, 2, 3, or 6.

- [ ] **Step 4: Run the full test suite once more**

Run: `dotnet test tests/GhostNotes.Tests`
Expected: 36 passed.

- [ ] **Step 5: Commit**

```powershell
git add README.md
git commit -m "docs: README with usage and honest limits; release verified"
```

---

## Self-Review (completed during planning)

- **Spec coverage:** goals §2 → Tasks 1,2,3,7,8,9,10,11; mechanism §4/§5 → Tasks 4,5,7,10; components §6 → Tasks 1–10; UX §7 → Tasks 7,8,9,10; data §8 → Tasks 1,2,9; errors §9 → Tasks 1 (corrupt), 2 (debounce), 3 (clamp), 4 (version gate), 5 (status), 6 (hotkey fallback), 7 (acrylic fallback), 10 (single-instance); testing §10 → all unit-test tasks + Task 11 checklist; §12 deferred items intentionally absent.
- **Type consistency:** `NoteRepository(string?)`, `AutosaveScheduler(Action, TimeSpan)` + `Cancel()`, `PositionClamp.Clamp` tuple shapes, `VersionGate(int)`, `CaptureGuard(IAffinityApi, IWindowEnumerator, VersionGate, uint)` + `ApplyToWindow/Forget/Sweep/IsProtected/ProtectionStatusChanged`, `HotkeySpec`/`HotkeySelection.Select`, `HotkeyService.Register()` tuple, `NoteWindow(Note, CaptureGuard)` + four events + `ReapplyProtectionAndShow()`, `NoteManager(NoteRepository, CaptureGuard)` members, `SingleInstanceGuard` consts, `TrayController(manager, hotkeys, guard)` — all cross-task references match.
- **Known deviations:** none vs. spec (spec §5 amended pre-plan: `WS_EX_NOACTIVATE` removed because it breaks typing).
