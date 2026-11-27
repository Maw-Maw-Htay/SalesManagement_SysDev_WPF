using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace SalesManagement_SysDev;

/// <summary>Embeds TrueType subsets and Unicode mappings for searchable Japanese report text.</summary>
internal sealed class OrderPdfText(PdfDocumentWriter pdf)
{
    private static readonly string[] FontFamilies = ["Meiryo", "Yu Gothic", "MS Gothic", "Yu Mincho", "Noto Sans JP", "Segoe UI Symbol"];
    private readonly List<FontFace> fonts = [];
    private readonly Dictionary<Rune, EncodedGlyph> glyphs = [];
    private int nextFont;

    internal sealed record Line(string Text, double Width);
    private sealed record EncodedGlyph(FontFace Font, ushort Code, ushort Glyph, int Width, Rune Character);
    private sealed record FontFace(GlyphTypeface Typeface, string Resource, string Name)
    {
        public int Id { get; set; }
        public List<EncodedGlyph> Glyphs { get; } = [];
    }

    internal string FontResources => $"/Font << {string.Join(' ', fonts.Where(font => font.Id > 0).Select(font => $"/{font.Resource} {pdf.Reference(font.Id)}"))} >>";

    internal double Measure(string value) => value.EnumerateRunes().Sum(character => Glyph(character).Width) / 1000d;

    internal IReadOnlyList<Line> Wrap(string value, double fontSize, double maxWidth)
    {
        var lines = new List<Line>();
        var current = new StringBuilder();
        var width = 0d;
        Span<char> utf16 = stackalloc char[2];
        foreach (var character in value.ReplaceLineEndings(" ").Replace('\t', ' ').EnumerateRunes())
        {
            if (Rune.IsControl(character)) continue;
            var advance = Glyph(character).Width / 1000d;
            if (current.Length > 0 && (width + advance) * fontSize > maxWidth)
            {
                lines.Add(new Line(current.ToString(), width));
                current.Clear();
                width = 0;
            }
            current.Append(utf16[..character.EncodeToUtf16(utf16)]);
            width += advance;
        }
        lines.Add(new Line(current.ToString(), width));
        return lines;
    }

    internal void Draw(StringBuilder content, string value, double size, double x, double y, string color)
    {
        if (value.Length == 0) return;
        content.Append(FormattableString.Invariant($"{color} rg BT {x:0.###} {y:0.###} Td "));
        FontFace? active = null;
        Span<char> hex = stackalloc char[4];
        foreach (var character in value.EnumerateRunes())
        {
            var glyph = Glyph(character);
            if (!ReferenceEquals(active, glyph.Font))
            {
                if (active != null) content.Append("> Tj ");
                active = glyph.Font;
                content.Append(FormattableString.Invariant($"/{active.Resource} {size:0.###} Tf <"));
            }
            glyph.Code.TryFormat(hex, out _, "X4");
            content.Append(hex);
        }
        content.AppendLine("> Tj ET");
    }

    private EncodedGlyph Glyph(Rune character)
    {
        if (glyphs.TryGetValue(character, out var existing)) return existing;
        foreach (var font in fonts)
            if (font.Typeface.CharacterToGlyphMap.TryGetValue(character.Value, out var id) && id != 0)
                return Register(font, id, character);

        while (nextFont < FontFamilies.Length)
        {
            var family = FontFamilies[nextFont++];
            var typeface = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var face)
                || face.EmbeddingRights is not (FontEmbeddingRight.Editable or FontEmbeddingRight.Installable)
                || fonts.Any(font => font.Typeface.FontUri == face.FontUri)) continue;

            // Separate report character IDs from font glyph indices. Some Unicode characters share a glyph.
            var resource = $"F{fonts.Count + 1}";
            var font = new FontFace(face, resource, $"SALES{(char)('A' + fonts.Count)}+{family.Replace(" ", "")}");
            fonts.Add(font);
            if (face.CharacterToGlyphMap.TryGetValue(character.Value, out var id) && id != 0)
                return Register(font, id, character);
        }

        if (character.Value is '受' or '?' || fonts.Count == 0)
            throw new OrderExportException("PDFの出力に使用できる日本語フォントが見つかりません。");
        // Retain the original Unicode text for copying even if Windows has no glyph for it.
        var fallback = Glyph(new Rune('?'));
        return Register(fallback.Font, fallback.Glyph, character);
    }

    private EncodedGlyph Register(FontFace font, ushort id, Rune character)
    {
        if (font.Id == 0) font.Id = pdf.ReserveObject();
        var code = checked((ushort)(font.Glyphs.Count + 1));
        var glyph = new EncodedGlyph(font, code, id, (int)Math.Round(font.Typeface.AdvanceWidths[id] * 1000), character);
        font.Glyphs.Add(glyph);
        glyphs.Add(character, glyph);
        return glyph;
    }

    internal void WriteFonts(CancellationToken token)
    {
        foreach (var font in fonts.Where(font => font.Id > 0))
        {
            token.ThrowIfCancellationRequested();
            var fontFile = pdf.ReserveObject();
            var descriptor = pdf.ReserveObject();
            var cidFont = pdf.ReserveObject();
            var cidToGlyph = pdf.ReserveObject();
            var toUnicode = pdf.ReserveObject();
            var mappings = font.Glyphs;
            byte[] subset;
            try { subset = font.Typeface.ComputeSubset(mappings.Select(glyph => glyph.Glyph).Append((ushort)0).Distinct().ToArray()); }
            catch (FileFormatException) { throw new OrderExportException("日本語フォントをPDFに埋め込めませんでした。"); }
            token.ThrowIfCancellationRequested();
            pdf.WriteStream(fontFile, $"/Length1 {subset.Length}", subset);
            pdf.WriteObject(descriptor, $"<< /Type /FontDescriptor /FontName /{font.Name} /Flags 4 /FontBBox [-1000 -500 3000 1500] /Ascent 900 /Descent -250 /CapHeight 730 /ItalicAngle 0 /StemV 80 /FontFile2 {pdf.Reference(fontFile)} >>");

            var glyphMap = new byte[(mappings.Count + 1) * 2];
            foreach (var glyph in mappings) BinaryPrimitives.WriteUInt16BigEndian(glyphMap.AsSpan(glyph.Code * 2, 2), glyph.Glyph);
            pdf.WriteStream(cidToGlyph, "", glyphMap);
            pdf.WriteObject(cidFont, $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{font.Name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {pdf.Reference(descriptor)} /CIDToGIDMap {pdf.Reference(cidToGlyph)} /W [1 [{string.Join(' ', mappings.Select(glyph => glyph.Width))}]] >>");
            pdf.WriteStream(toUnicode, "", UnicodeMap(mappings));
            pdf.WriteObject(font.Id, $"<< /Type /Font /Subtype /Type0 /BaseFont /{font.Name} /Encoding /Identity-H /DescendantFonts [{pdf.Reference(cidFont)}] /ToUnicode {pdf.Reference(toUnicode)} >>");
        }
    }

    private static byte[] UnicodeMap(List<EncodedGlyph> mappings)
    {
        var map = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /OrderExportUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var group in mappings.Chunk(100))
        {
            map.Append(group.Length).AppendLine(" beginbfchar");
            foreach (var entry in group)
                map.AppendLine($"<{entry.Code:X4}> <{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(entry.Character.ToString()))}>");
            map.AppendLine("endbfchar");
        }
        return Encoding.ASCII.GetBytes(map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString());
    }
}
