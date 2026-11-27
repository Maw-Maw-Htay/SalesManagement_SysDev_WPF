using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SalesManagement_SysDev;

/// <summary>Writes seekable PDF output one object at a time. Only object offsets are kept in memory.</summary>
internal sealed class PdfDocumentWriter
{
    private readonly Stream output;
    private readonly List<long> offsets = [0];

    internal PdfDocumentWriter(Stream output)
    {
        this.output = output;
        WriteAscii("%PDF-1.7\n");
        output.Write([0x25, 0xe2, 0xe3, 0xcf, 0xd3, 0x0a]);
    }

    internal int ReserveObject()
    {
        offsets.Add(0);
        return offsets.Count - 1;
    }

    internal string Reference(int id) => $"{id} 0 R";

    internal void WriteObject(int id, string body)
    {
        StartObject(id);
        WriteAscii(body);
        WriteAscii("\nendobj\n");
    }

    internal void WriteStream(int id, string dictionary, ReadOnlySpan<byte> bytes)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) deflate.Write(bytes);
        StartObject(id);
        WriteAscii($"<< /Length {compressed.Length} /Filter /FlateDecode {dictionary} >>\nstream\n");
        compressed.WriteTo(output);
        WriteAscii("\nendstream\nendobj\n");
    }

    internal void Finish(int pagesId, string title)
    {
        var catalog = ReserveObject();
        WriteObject(catalog, $"<< /Type /Catalog /Pages {Reference(pagesId)} /Lang (ja-JP) >>");
        var info = ReserveObject();
        WriteObject(info, $"<< /Title <FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(title))}> >>");
        var start = output.Position;
        if (start > 9_999_999_999L) throw new OrderExportException("PDFのファイルサイズが上限を超えました。");
        WriteAscii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            if (offset == 0) throw new InvalidOperationException("A reserved PDF object was not written.");
            WriteAscii($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }
        WriteAscii($"trailer\n<< /Size {offsets.Count} /Root {Reference(catalog)} /Info {Reference(info)} >>\nstartxref\n{start}\n%%EOF\n");
    }

    private void StartObject(int id)
    {
        offsets[id] = output.Position;
        WriteAscii($"{id} 0 obj\n");
    }

    private void WriteAscii(string value) => output.Write(Encoding.ASCII.GetBytes(value));
}
