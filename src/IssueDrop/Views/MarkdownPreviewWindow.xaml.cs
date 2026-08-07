using System.Windows;
using System.Windows.Documents;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace IssueDrop.Views;

public partial class MarkdownPreviewWindow : Window
{
    public MarkdownPreviewWindow(string title, string markdown)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Untitled issue" : title;
        Viewer.Document = Render(markdown);
    }

    private static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new MediaFontFamily("Segoe UI Variable Text"), FontSize = 14 };
        var inCode = false;
        var code = new List<string>();
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    var codeBlock = new Paragraph(new Run(string.Join(Environment.NewLine, code)))
                    { FontFamily = new MediaFontFamily("Cascadia Mono, Consolas"), Padding = new Thickness(10) };
                    codeBlock.SetResourceReference(TextElement.BackgroundProperty, "CodeBackgroundBrush");
                    document.Blocks.Add(codeBlock);
                    code.Clear();
                }
                inCode = !inCode; continue;
            }
            if (inCode) { code.Add(line); continue; }
            if (line.StartsWith("### ")) document.Blocks.Add(Heading(line[4..], 16));
            else if (line.StartsWith("## ")) document.Blocks.Add(Heading(line[3..], 19));
            else if (line.StartsWith("# ")) document.Blocks.Add(Heading(line[2..], 22));
            else if (line.StartsWith("- [ ] ")) document.Blocks.Add(new Paragraph(new Run("☐ " + line[6..])) { Margin = new Thickness(8, 2, 0, 2) });
            else if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) document.Blocks.Add(new Paragraph(new Run("☑ " + line[6..])) { Margin = new Thickness(8, 2, 0, 2) });
            else if (line.StartsWith("- ")) document.Blocks.Add(new Paragraph(new Run("• " + line[2..])) { Margin = new Thickness(8, 2, 0, 2) });
            else document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 2, 0, 5) });
        }
        return document;
    }

    private static Paragraph Heading(string text, double size) => new(new Run(text)) { FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 5) };
}
