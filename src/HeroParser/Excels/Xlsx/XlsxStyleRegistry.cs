using HeroParser.Excels.Core;

namespace HeroParser.Excels.Xlsx;

internal sealed class XlsxStyleRegistry
{
    private readonly List<ExcelFont> fonts = [];
    private readonly List<ExcelFill> fills = [];
    private readonly List<ExcelBorder> borders = [];
    private readonly List<string> numberFormats = []; // Custom format codes (numFmtId >= 164)
    private readonly List<XfRecord> xfs = [];

    public IReadOnlyList<ExcelFont> Fonts => fonts;
    public IReadOnlyList<ExcelFill> Fills => fills;
    public IReadOnlyList<ExcelBorder> Borders => borders;
    public IReadOnlyList<string> NumberFormats => numberFormats;
    public IReadOnlyList<XfRecord> Xfs => xfs;

    public XlsxStyleRegistry()
    {
        // Default required styles according to the Excel OpenXML specification:
        // Font 0: Default Calibri 11
        fonts.Add(new ExcelFont());

        // Fill 0: none, Fill 1: gray125
        fills.Add(new ExcelFill { PatternType = "none" });
        fills.Add(new ExcelFill { PatternType = "gray125" });

        // Border 0: empty border
        borders.Add(new ExcelBorder());

        // xfs: default style index 0 (xfId="0", fontId="0", fillId="0", borderId="0")
        xfs.Add(new XfRecord { FontId = 0, FillId = 0, BorderId = 0, NumFmtId = 0 });

        // style index 1: built-in date format numFmtId 14
        xfs.Add(new XfRecord { FontId = 0, FillId = 0, BorderId = 0, NumFmtId = 14 });
    }

    public int RegisterStyle(ExcelStyle style)
    {
        int fontId = ResolveFont(style.Font);
        int fillId = ResolveFill(style.Fill);
        int borderId = ResolveBorder(style.Border);
        int numFmtId = ResolveNumberFormat(style.NumberFormat);

        var xf = new XfRecord
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            NumFmtId = numFmtId,
            Alignment = style.Alignment
        };

        for (int i = 0; i < xfs.Count; i++)
        {
            if (xfs[i] == xf)
                return i;
        }

        xfs.Add(xf);
        return xfs.Count - 1;
    }

    private int ResolveFont(ExcelFont? font)
    {
        if (font is null)
            return 0;

        for (int i = 0; i < fonts.Count; i++)
        {
            if (fonts[i] == font)
                return i;
        }

        fonts.Add(font);
        return fonts.Count - 1;
    }

    private int ResolveFill(ExcelFill? fill)
    {
        if (fill is null)
            return 0;

        for (int i = 0; i < fills.Count; i++)
        {
            if (fills[i] == fill)
                return i;
        }

        fills.Add(fill);
        return fills.Count - 1;
    }

    private int ResolveBorder(ExcelBorder? border)
    {
        if (border is null)
            return 0;

        for (int i = 0; i < borders.Count; i++)
        {
            if (borders[i] == border)
                return i;
        }

        borders.Add(border);
        return borders.Count - 1;
    }

    private int ResolveNumberFormat(string? format)
    {
        if (string.IsNullOrEmpty(format))
            return 0;

        if (builtInNumFmts.TryGetValue(format, out int builtInId))
            return builtInId;

        for (int i = 0; i < numberFormats.Count; i++)
        {
            if (numberFormats[i] == format)
                return 164 + i;
        }

        numberFormats.Add(format);
        return 164 + (numberFormats.Count - 1);
    }

    private static readonly Dictionary<string, int> builtInNumFmts = new(StringComparer.OrdinalIgnoreCase)
    {
        { "General", 0 },
        { "0", 1 },
        { "0.00", 2 },
        { "#,##0", 3 },
        { "#,##0.00", 4 },
        { "0%", 9 },
        { "0.00%", 10 },
        { "0.00E+00", 11 },
        { "# ?/?", 12 },
        { "# ??/??", 13 },
        { "m/d/yyyy", 14 },
        { "d-mmm-yy", 15 },
        { "d-mmm", 16 },
        { "mmm-yy", 17 },
        { "h:mm AM/PM", 18 },
        { "h:mm:ss AM/PM", 19 },
        { "h:mm", 20 },
        { "h:mm:ss", 21 },
        { "m/d/yyyy h:mm", 22 }
    };

    internal readonly record struct XfRecord
    {
        public int FontId { get; init; }
        public int FillId { get; init; }
        public int BorderId { get; init; }
        public int NumFmtId { get; init; }
        public ExcelAlignment? Alignment { get; init; }
    }
}
