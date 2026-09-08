using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfBrush = System.Windows.Media.Brush;
using WpfBlock = System.Windows.Documents.Block;
using MdBlock = Markdig.Syntax.Block;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace GhostNotes.Services;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static FlowDocument Render(string markdown, double baseFontSize = 13, WpfBrush? foreground = null)
    {
        foreground ??= new SolidColorBrush(Color.FromArgb(220, 20, 20, 20));
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, -apple-system, system-ui, sans-serif"),
            FontSize = baseFontSize,
            Foreground = foreground,
            PagePadding = new Thickness(10, 6, 10, 10),
            LineHeight = baseFontSize * 1.45
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            var emptyP = new Paragraph(new Run("Empty note. Double-click or open Manager to edit."))
            {
                Foreground = new SolidColorBrush(Color.FromArgb(140, 60, 60, 60)),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 4)
            };
            doc.Blocks.Add(emptyP);
            return doc;
        }

        var mdDoc = Markdown.Parse(markdown, Pipeline);

        foreach (var block in mdDoc)
        {
            var element = RenderBlock(block, baseFontSize, foreground);
            if (element != null) doc.Blocks.Add(element);
        }

        return doc;
    }

    private static WpfBlock? RenderBlock(MdBlock block, double baseFontSize, WpfBrush foreground)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var p = new Paragraph
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foreground,
                    Margin = new Thickness(0, heading.Level == 1 ? 8 : 6, 0, 4)
                };
                p.FontSize = heading.Level switch
                {
                    1 => baseFontSize + 6,
                    2 => baseFontSize + 4,
                    3 => baseFontSize + 2,
                    _ => baseFontSize + 1
                };
                if (heading.Inline != null) RenderInlines(heading.Inline, p.Inlines, foreground);
                return p;
            }

            case ParagraphBlock pb:
            {
                var p = new Paragraph
                {
                    FontSize = baseFontSize,
                    Foreground = foreground,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                if (pb.Inline != null) RenderInlines(pb.Inline, p.Inlines, foreground);
                return p;
            }

            case ListBlock listBlock:
            {
                var list = new List
                {
                    MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(12, 2, 0, 4),
                    Padding = new Thickness(0),
                    FontSize = baseFontSize
                };
                foreach (var item in listBlock)
                {
                    if (item is ListItemBlock lib)
                    {
                        var li = new ListItem();
                        foreach (var sub in lib)
                        {
                            var b = RenderBlock(sub, baseFontSize, foreground);
                            if (b != null) li.Blocks.Add(b);
                        }
                        list.ListItems.Add(li);
                    }
                }
                return list;
            }

            case QuoteBlock qb:
            {
                var section = new Section
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(180, 56, 189, 248)),
                    Padding = new Thickness(8, 2, 0, 2),
                    Margin = new Thickness(4, 4, 0, 4)
                };
                foreach (var sub in qb)
                {
                    var b = RenderBlock(sub, baseFontSize, foreground);
                    if (b is Paragraph p)
                    {
                        p.FontStyle = FontStyles.Italic;
                        p.Foreground = new SolidColorBrush(Color.FromArgb(180, 40, 40, 40));
                    }
                    if (b != null) section.Blocks.Add(b);
                }
                return section;
            }

            case FencedCodeBlock fcb:
            {
                var codeText = string.Join("\n", fcb.Lines);
                var p = new Paragraph(new Run(codeText))
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    FontSize = Math.Max(10, baseFontSize - 1.5),
                    Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                return p;
            }

            case CodeBlock cb:
            {
                var codeText = string.Join("\n", cb.Lines);
                var p = new Paragraph(new Run(codeText))
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    FontSize = Math.Max(10, baseFontSize - 1.5),
                    Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                return p;
            }

            case ThematicBreakBlock:
            {
                var hr = new BlockUIContainer(new System.Windows.Shapes.Line
                {
                    Stretch = Stretch.Fill,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    StrokeThickness = 1,
                    Margin = new Thickness(0, 6, 0, 6)
                });
                return hr;
            }

            default:
                return null;
        }
    }

    private static void RenderInlines(ContainerInline container, InlineCollection target, WpfBrush foreground)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    target.Add(new Run(lit.Content.ToString()) { Foreground = foreground });
                    break;

                case EmphasisInline emp:
                {
                    var span = new Span();
                    if (emp.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                    else span.FontStyle = FontStyles.Italic;
                    span.Foreground = foreground;
                    RenderInlines(emp, span.Inlines, foreground);
                    target.Add(span);
                    break;
                }

                case CodeInline code:
                {
                    var run = new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        Background = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)),
                        FontSize = target.Count > 0 ? (target.LastInline?.FontSize ?? 12) * 0.92 : 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(230, 15, 23, 42))
                    };
                    target.Add(run);
                    break;
                }

                case LinkInline link:
                {
                    var hl = new Hyperlink { ToolTip = link.Url, Foreground = new SolidColorBrush(Color.FromArgb(240, 2, 132, 199)) };
                    RenderInlines(link, hl.Inlines, hl.Foreground);
                    target.Add(hl);
                    break;
                }

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;

                case ContainerInline nested:
                    RenderInlines(nested, target, foreground);
                    break;
            }
        }
    }
}
