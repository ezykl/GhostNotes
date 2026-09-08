using System.Windows.Documents;
using GhostNotes.Services;
using Xunit;

namespace GhostNotes.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void RendersHeadingsAndFormatting()
    {
        var md = "# Heading 1\n\nThis is **bold** and *italic* text.\n\n- Item 1\n- Item 2\n\n```\nvar x = 42;\n```";
        var doc = MarkdownRenderer.Render(md, 14);

        Assert.NotNull(doc);
        Assert.True(doc.Blocks.Count >= 4);

        // Heading block
        var h1 = doc.Blocks.FirstBlock as Paragraph;
        Assert.NotNull(h1);
        Assert.True(h1.FontSize > 14);
    }

    [Fact]
    public void HandlesEmptyMarkdownGracefully()
    {
        var doc = MarkdownRenderer.Render("", 14);
        Assert.NotNull(doc);
        Assert.Single(doc.Blocks);
    }
}
