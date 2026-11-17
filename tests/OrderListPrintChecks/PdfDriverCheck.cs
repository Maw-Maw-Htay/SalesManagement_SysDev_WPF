using System.IO;
using System.IO.Packaging;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Windows.Xps.Packaging;

internal static class PdfDriverCheck
{
    // Test the Windows driver using the report's XPS pages and validated ticket, without a Save As prompt.
    internal static void Print(DocumentPaginator paginator, PrintTicket ticket, string output)
    {
        using var xpsBytes = new MemoryStream();
        using (var package = Package.Open(xpsBytes, FileMode.Create, FileAccess.ReadWrite))
        using (var xps = new XpsDocument(package, CompressionOption.Normal))
            XpsDocument.CreateXpsDocumentWriter(xps).Write(paginator, ticket);
        using var completion = new ManualResetEvent(false);
        var result = StartXpsPrintJob("Microsoft Print to PDF", "A4 landscape order list check", Path.GetFullPath(output),
            IntPtr.Zero, completion.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0, out var job, out var stream, out var settings);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            using var ticketXml = ticket.GetXmlStream();
            using var bytes = new MemoryStream();
            ticketXml.CopyTo(bytes);
            WriteAndClose(settings, bytes.ToArray());
            WriteAndClose(stream, xpsBytes.ToArray());
            if (!completion.WaitOne(TimeSpan.FromSeconds(25))) { job.Cancel(); throw new TimeoutException("The PDF driver did not finish."); }
            job.GetJobStatus(out var status);
            if (status.Completion != 1 || !File.Exists(output))
                throw new InvalidOperationException($"PDF job failed ({status.Completion}, 0x{status.Result:X8}).");
            Console.WriteLine("PASS Microsoft Print to PDF produced " + output);
        }
        finally
        {
            Marshal.ReleaseComObject(settings);
            Marshal.ReleaseComObject(stream);
            Marshal.ReleaseComObject(job);
        }
    }

    private static void WriteAndClose(JobStream stream, byte[] data)
    {
        stream.Write(data, (uint)data.Length, out var written);
        if (written != data.Length) throw new IOException("The XPS print stream did not accept the whole document.");
        stream.Close();
    }

    [DllImport("XpsPrint.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StartXpsPrintJob(string printer, string jobName, string output, IntPtr progress, IntPtr completion,
        IntPtr pages, uint pageCount, out Job job, out JobStream document, out JobStream ticket);

    [ComImport, Guid("5ab89b06-8194-425f-ab3b-d7a96e350161"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Job
    {
        void Cancel();
        void GetJobStatus(out JobStatus status);
    }

    [ComImport, Guid("7a77dc5f-45d6-4dff-9307-d8cb846347ca"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface JobStream
    {
        void Read(IntPtr bytes, uint size, out uint read);
        void Write([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] bytes, uint size, out uint written);
        void Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobStatus
    {
        public uint JobId;
        public int Document;
        public int Page;
        public int PagesTotal;
        public int Completion;
        public int Result;
    }
}
