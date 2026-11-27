using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SalesManagement_SysDev;

internal static class OrderExportWorkbook
{
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const int SheetRowLimit = 1_048_576;
    private static readonly int[] Widths = [16, 15, 15, 32, 11, 20, 22, 26, 14];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static async Task WriteAsync(Stream output, IAsyncEnumerable<OrderExportRow> rows, CancellationToken token)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await using var enumerator = rows.GetAsyncEnumerator(token);
        var hasRow = await enumerator.MoveNextAsync().ConfigureAwait(false);
        var sheet = 0;
        do
        {
            token.ThrowIfCancellationRequested();
            using var entry = archive.CreateEntry($"xl/worksheets/sheet{++sheet}.xml", CompressionLevel.Fastest).Open();
            using var xml = XmlWriter.Create(entry, new XmlWriterSettings { Encoding = Utf8, NewLineHandling = NewLineHandling.Entitize });
            StartSheet(xml);
            var lastRow = 1;
            while (hasRow && lastRow < SheetRowLimit)
            {
                token.ThrowIfCancellationRequested();
                WriteRow(xml, enumerator.Current, ++lastRow);
                hasRow = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            xml.WriteEndElement(); // sheetData
            xml.WriteStartElement("autoFilter", Spreadsheet);
            xml.WriteAttributeString("ref", $"A1:I{lastRow}");
            xml.WriteEndElement();
            xml.WriteEndElement(); // worksheet
        } while (hasRow);

        token.ThrowIfCancellationRequested();
        WritePackageParts(archive, sheet);
    }

    private static void StartSheet(XmlWriter xml)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("worksheet", Spreadsheet);
        xml.WriteRaw("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView></sheetViews>");
        xml.WriteStartElement("cols", Spreadsheet);
        for (var i = 0; i < Widths.Length; i++)
        {
            xml.WriteStartElement("col", Spreadsheet);
            var column = (i + 1).ToString(CultureInfo.InvariantCulture);
            xml.WriteAttributeString("min", column);
            xml.WriteAttributeString("max", column);
            xml.WriteAttributeString("width", Widths[i].ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("customWidth", "1");
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteStartElement("sheetData", Spreadsheet);
        xml.WriteStartElement("row", Spreadsheet);
        xml.WriteAttributeString("r", "1");
        for (var column = 0; column < OrderExportService.Headers.Count; column++)
            TextCell(xml, (char)('A' + column), "1", OrderExportService.Headers[column], "1");
        xml.WriteEndElement();
    }

    private static void WriteRow(XmlWriter xml, OrderExportRow row, int index)
    {
        xml.WriteStartElement("row", Spreadsheet);
        var number = index.ToString(CultureInfo.InvariantCulture);
        xml.WriteAttributeString("r", number);
        TextCell(xml, 'A', number, row.Number);
        DateCell(xml, 'B', number, row.OrderDate, row.OrderDateText);
        DateCell(xml, 'C', number, row.DeliveryDate, row.DeliveryDateText);
        TextCell(xml, 'D', number, row.Customer, "5");
        NumberCell(xml, 'E', number, row.LineCount, "4");
        TextCell(xml, 'F', number, row.EmployeeName, "5");
        TextCell(xml, 'G', number, row.Office, "5");
        NumberCell(xml, 'H', number, row.Total, "3");
        TextCell(xml, 'I', number, row.Status);
        xml.WriteEndElement();
    }

    private static void DateCell(XmlWriter xml, char column, string row, DateTime? value, string label)
    {
        if (value is not DateTime date || date.Year < 1900)
        {
            TextCell(xml, column, row, label);
            return;
        }
        // Excel's 1900 calendar includes the fictitious 1900/02/29.
        var serial = (int)date.Date.ToOADate() - (date < new DateTime(1900, 3, 1) ? 1 : 0);
        NumberCell(xml, column, row, serial, "2");
    }

    private static void NumberCell(XmlWriter xml, char column, string row, decimal value, string style)
    {
        xml.WriteStartElement("c", Spreadsheet);
        xml.WriteAttributeString("r", column + row);
        xml.WriteAttributeString("s", style);
        xml.WriteElementString("v", Spreadsheet, value.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement();
    }

    private static void TextCell(XmlWriter xml, char column, string row, string value, string? style = null)
    {
        xml.WriteStartElement("c", Spreadsheet);
        xml.WriteAttributeString("r", column + row);
        xml.WriteAttributeString("t", "inlineStr");
        if (style != null) xml.WriteAttributeString("s", style);
        xml.WriteStartElement("is", Spreadsheet);
        xml.WriteStartElement("t", Spreadsheet);
        xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
        xml.WriteString(SpreadsheetText(value));
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static string SpreadsheetText(string value)
    {
        StringBuilder? escaped = null;
        var next = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsSurrogatePair(value, i)) { i++; continue; }
            var ch = value[i];
            var literalEscape = ch == '_' && i + 6 < value.Length && value[i + 1] == 'x' && value[i + 6] == '_'
                && ushort.TryParse(value.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
            if (!literalEscape && XmlConvert.IsXmlChar(ch)) continue;
            escaped ??= new StringBuilder(value.Length + 8);
            escaped.Append(value, next, i - next);
            escaped.Append(char.IsSurrogate(ch) ? "\uFFFD" : $"_x{(int)ch:X4}_");
            next = i + 1;
        }
        return escaped == null ? value : escaped.Append(value, next, value.Length - next).ToString();
    }

    private static void WritePackageParts(ZipArchive archive, int sheetCount)
    {
        var types = new StringBuilder("""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            """);
        var workbook = new StringBuilder($"<workbook xmlns=\"{Spreadsheet}\" xmlns:r=\"{OfficeRelationships}\"><bookViews><workbookView/></bookViews><sheets>");
        var workbookRels = new StringBuilder($"<Relationships xmlns=\"{Relationships}\"><Relationship Id=\"styles\" Type=\"{OfficeRelationships}/styles\" Target=\"styles.xml\"/>");
        for (var i = 1; i <= sheetCount; i++)
        {
            var name = i == 1 ? "受注一覧" : $"受注一覧 {i}";
            types.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            workbook.Append($"<sheet name=\"{name}\" sheetId=\"{i}\" r:id=\"sheet{i}\"/>");
            workbookRels.Append($"<Relationship Id=\"sheet{i}\" Type=\"{OfficeRelationships}/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        }
        WritePart("[Content_Types].xml", types.Append("</Types>").ToString());
        WritePart("_rels/.rels", $"<Relationships xmlns=\"{Relationships}\"><Relationship Id=\"workbook\" Type=\"{OfficeRelationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        WritePart("xl/workbook.xml", workbook.Append("</sheets></workbook>").ToString());
        WritePart("xl/_rels/workbook.xml.rels", workbookRels.Append("</Relationships>").ToString());
        WritePart("xl/styles.xml", Styles);

        void WritePart(string name, string value)
        {
            using var part = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), Utf8);
            part.Write(value);
        }
    }

    private const string Styles = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="2"><numFmt numFmtId="164" formatCode="yyyy/mm/dd"/><numFmt numFmtId="165" formatCode="&quot;¥&quot;#,##0"/></numFmts>
          <fonts count="2"><font><sz val="11"/><name val="Yu Gothic"/></font><font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Yu Gothic"/></font></fonts>
          <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF061A3D"/><bgColor indexed="64"/></patternFill></fill></fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="6">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
            <xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
            <xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
