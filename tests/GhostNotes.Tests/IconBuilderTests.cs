using System.IO;
using GhostNotes.Services;
using Xunit;

namespace GhostNotes.Tests;

public class IconBuilderTests
{
    [Fact]
    public void GeneratesValidIcoFile()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), "GhostNotes_test.ico");
        if (File.Exists(targetPath)) File.Delete(targetPath);

        IconBuilder.EnsureIcon(targetPath);

        Assert.True(File.Exists(targetPath));
        var fi = new FileInfo(targetPath);
        Assert.True(fi.Length > 1000);

        var cur = new DirectoryInfo(AppContext.BaseDirectory);
        while (cur != null && !File.Exists(Path.Combine(cur.FullName, "GhostNotes.sln")))
        {
            cur = cur.Parent;
        }
        Assert.NotNull(cur);
        var projIco = Path.Combine(cur.FullName, "src", "GhostNotes", "Assets", "GhostNotes.ico");
        IconBuilder.EnsureIcon(projIco);
        Assert.True(File.Exists(projIco));
    }
}
