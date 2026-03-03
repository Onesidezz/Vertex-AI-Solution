using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Custom iText7 extraction strategy that reads font size and bold flag per text chunk.
/// Preserves headings that SimpleTextExtractionStrategy and LocationTextExtractionStrategy miss
/// when bold text is rendered with a differently-named embedded font (e.g. Arial-BoldMT).
/// Tags bold or large-font text with [HEADING] so the RAG chunker can treat it as a section marker.
/// </summary>
public class FontAwareTextExtractionStrategy : ITextExtractionStrategy
{
    // Minimum rendered height (points) to be considered a heading regardless of bold flag
    private const float HeadingFontSizeThreshold = 13f;

    private readonly record struct TextItem(
        float Y,
        float X,
        string Text,
        bool IsBold,
        float RenderedHeight);

    private readonly List<TextItem> _items = new();

    // -----------------------------------------------------------------------
    // IEventListener implementation
    // -----------------------------------------------------------------------

    public ICollection<EventType> GetSupportedEvents() =>
        new HashSet<EventType> { EventType.RENDER_TEXT };

    public void EventOccurred(IEventData data, EventType type)
    {
        if (type != EventType.RENDER_TEXT || data is not TextRenderInfo renderInfo)
            return;

        var text = renderInfo.GetText();
        if (string.IsNullOrEmpty(text))
            return;

        // --- Determine if text is bold ---
        var font = renderInfo.GetFont();
        var fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? string.Empty;
        bool isBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                   || fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
                   || fontName.Contains("Black", StringComparison.OrdinalIgnoreCase)
                   || fontName.Contains("Demi",  StringComparison.OrdinalIgnoreCase);

        // --- Determine rendered font height ---
        // Use ascent/descent lines for the actual rendered size in page coordinates
        float renderedHeight;
        try
        {
            var ascent  = renderInfo.GetAscentLine().GetStartPoint();
            var descent = renderInfo.GetDescentLine().GetStartPoint();
            renderedHeight = Math.Abs(ascent.Get(1) - descent.Get(1));
        }
        catch
        {
            // Fall back to GraphicsState font size if line geometry is unavailable
            renderedHeight = Math.Abs(renderInfo.GetGraphicsState().GetFontSize());
        }

        // --- Position for sort order ---
        var baseline = renderInfo.GetBaseline().GetStartPoint();

        _items.Add(new TextItem(
            Y: baseline.Get(1),
            X: baseline.Get(0),
            Text: text,
            IsBold: isBold,
            RenderedHeight: renderedHeight));
    }

    // -----------------------------------------------------------------------
    // ITextExtractionStrategy implementation
    // -----------------------------------------------------------------------

    public string GetResultantText()
    {
        if (_items.Count == 0)
            return string.Empty;

        // Sort top-to-bottom, then left-to-right (PDF Y origin is bottom-left)
        var sorted = _items
            .OrderByDescending(i => i.Y)
            .ThenBy(i => i.X)
            .ToList();

        var result      = new StringBuilder();
        var lineBuffer  = new StringBuilder();
        float lastY     = float.MaxValue;
        bool lineIsBold = false;
        float lineMaxH  = 0f;

        void FlushLine()
        {
            var lineText = lineBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(lineText))
                return;

            bool isHeading = lineIsBold || lineMaxH >= HeadingFontSizeThreshold;
            result.AppendLine(isHeading ? $"[HEADING] {lineText}" : lineText);
        }

        foreach (var item in sorted)
        {
            // New line when Y shifts by more than ~4pt.
            // 4pt is chosen deliberately:
            //   • Typical line spacing for 11-12pt body text is ≥ 13pt, so 4pt will
            //     never accidentally merge two real visual lines.
            //   • Some PDFs render individual glyphs (e.g. "C", "E", "O" of "CEO")
            //     as separate text objects whose baselines differ by 2-3pt due to
            //     font-metric rounding.  A 2pt threshold splits those into separate
            //     lines; 4pt keeps them together, restoring "CEO", "IBM", "FY26", etc.
            if (Math.Abs(item.Y - lastY) > 4f && lastY != float.MaxValue)
            {
                FlushLine();
                lineBuffer.Clear();
                lineIsBold = false;
                lineMaxH   = 0f;
            }

            lineBuffer.Append(item.Text);
            if (item.IsBold)    lineIsBold = true;
            if (item.RenderedHeight > lineMaxH) lineMaxH = item.RenderedHeight;
            lastY = item.Y;
        }

        FlushLine(); // flush the last line
        return result.ToString();
    }
}
