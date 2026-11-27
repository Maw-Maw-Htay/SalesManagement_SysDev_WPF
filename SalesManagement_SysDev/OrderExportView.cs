using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace SalesManagement_SysDev;

public sealed partial class F_OrderManagement
{
    private const string ExportSettingsKey = @"HKEY_CURRENT_USER\Software\SalesManagement_SysDev";
    private const string ExportDirectoryValue = "OrderExportDirectory";
    private CancellationTokenSource? orderExportCancellation;
    private string? lastExportDirectory;

    private async void SaveOrders()
    {
        if (!viewModel.SaveCommand.CanExecute(null) || appliedOrderRequest is not { } currentList) return;
        viewModel.IsExporting = true;
        viewModel.SetExportNotice();
        using var cancellation = new CancellationTokenSource();
        orderExportCancellation = cancellation;
        try
        {
            var actor = OrderExportActor.Capture();
            var request = new OrderExportRequest(currentList, viewModel.SelectedOrderIds, viewModel.SelectedDraftIds, actor);
            var exportDate = DateTime.Now;
            var picker = new SaveFileDialog
            {
                Title = "受注一覧を保存",
                FileName = $"受注一覧_{exportDate:yyyyMMdd}",
                Filter = OrderExportService.FileFilter,
                FilterIndex = 1,
                AddExtension = true,
                CheckPathExists = true,
                OverwritePrompt = true,
                ValidateNames = true
            };
            var directory = lastExportDirectory ?? ReadExportDirectory();
            if (directory != null && Path.IsPathFullyQualified(directory)) picker.InitialDirectory = directory;
            var allowOverwrite = false;
            picker.FileOk += (_, _) => allowOverwrite = File.Exists(picker.FileName);
            if (picker.ShowDialog(NavigationOwner ?? this) != true || cancellation.IsCancellationRequested) return;
            if (isHistoryView || appliedOrderRequest != currentList)
                throw new OrderExportException("受注一覧が更新されました。表示中の一覧から保存し直してください。");

            var format = (OrderExportFormat)picker.FilterIndex;
            var destination = OrderExportService.ValidatePath(picker.FileName, format);
            var job = Task.Run(async () =>
            {
                await OrderExportService.SaveAsync(request, destination, format, allowOverwrite, exportDate, cancellation.Token).ConfigureAwait(false);
                RememberExportDirectory(Path.GetDirectoryName(destination)!);
            }, cancellation.Token);
            if (await Task.WhenAny(job, Task.Delay(300, cancellation.Token)) != job && !cancellation.IsCancellationRequested)
                viewModel.ShowExportProgress = true;
            await job;

            lastExportDirectory = Path.GetDirectoryName(destination);
            if (orderRoot.IsLoaded) viewModel.SetExportNotice(destination);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError("受注一覧の出力に失敗しました: {0}", exception);
            if (orderRoot.IsLoaded) viewModel.SetExportNotice(error: ExportErrorMessage(exception));
        }
        finally
        {
            viewModel.ShowExportProgress = false;
            viewModel.IsExporting = false;
            orderExportCancellation = null;
        }
    }

    private static string ExportErrorMessage(Exception error) => error switch
    {
        OrderExportException => error.Message,
        DirectoryNotFoundException => "保存先のフォルダーが見つかりません。別の保存先を指定してください。",
        PathTooLongException => "ファイル名または保存先のパスが長すぎます。短い名前を指定してください。",
        UnauthorizedAccessException or SecurityException => "保存先に書き込めません。既存のファイルを開いている場合は閉じ、保存先の権限を確認してください。",
        IOException io when (io.HResult & 0xffff) is 32 or 33 => "ファイルを別のアプリで使用しています。閉じてから保存し直してください。",
        IOException io when (io.HResult & 0xffff) is 39 or 112 => "保存先の空き容量が不足しています。空き容量を確認してください。",
        IOException io when (io.HResult & 0xffff) is 80 or 183 => "同じ名前のファイルが作成されました。名前を変更するか、保存し直してください。",
        IOException => "ファイルを書き込めませんでした。保存先とファイルの状態を確認してください。",
        SqlException => "受注一覧を取得できませんでした。データベースの接続を確認してください。",
        _ => "保存処理でエラーが発生しました。表示中の一覧から保存し直してください。"
    };

    private static string? ReadExportDirectory()
    {
        try { return Registry.GetValue(ExportSettingsKey, ExportDirectoryValue, null) as string; }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    private static void RememberExportDirectory(string path)
    {
        try { Registry.SetValue(ExportSettingsKey, ExportDirectoryValue, path); }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        { Trace.TraceWarning("前回の保存先を記録できませんでした: {0}", ex); }
    }

    private void CancelOrderExport(object sender, RoutedEventArgs e) => orderExportCancellation?.Cancel();
    private void DismissOrderExportNotice(object sender, RoutedEventArgs e) => viewModel.SetExportNotice();
    private void OpenExportFolder(object sender, RoutedEventArgs e) => OpenExportPath(Path.GetDirectoryName(viewModel.ExportedPath));
    private void OpenExportFile(object sender, RoutedEventArgs e) => OpenExportPath(viewModel.ExportedPath);

    private void OpenExportPath(string? path)
    {
        if (path == null) return;
        try { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("保存先を開けませんでした: {0}", ex);
            viewModel.SetExportNotice(error: "保存済みのファイルまたは保存場所を開けませんでした。移動や削除がないか確認してください。");
        }
    }
}
