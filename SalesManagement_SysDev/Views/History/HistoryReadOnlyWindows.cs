using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal abstract class HistoryReadOnlyWindow : Window
{
    protected Grid Body { get; } = new() { Margin = new Thickness(16) };
    protected DockPanel Footer { get; } = new() { LastChildFill = false, Margin = new Thickness(16, 8, 16, 8) };
    protected CancellationToken WindowToken => lifetime.Token;
    private readonly CancellationTokenSource lifetime = new();

    protected HistoryReadOnlyWindow(Window owner, string title, string subtitle, double width)
    {
        Owner = owner; Title = title; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Width = Math.Min(width, SystemParameters.WorkArea.Width * .92); Height = Math.Min(620, SystemParameters.WorkArea.Height * .88);
        MinWidth = Math.Min(490, Width); MinHeight = Math.Min(340, Height);
        Background = AppTheme.Canvas;
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var headings = new StackPanel();
        headings.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        headings.Children.Add(new TextBlock { Text = subtitle, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,0) });
        root.Children.Add(new Border { Background = AppTheme.Primary, Padding = new Thickness(18,10,18,10), Child = headings });
        Grid.SetRow(Body, 1); root.Children.Add(Body);
        var close = new Button { Content = "閉じる", IsCancel = true, MinWidth = 84 };
        DockPanel.SetDock(close, Dock.Right); Footer.Children.Add(close);
        var bottom = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0,1,0,0), Child = Footer };
        Grid.SetRow(bottom, 2); root.Children.Add(bottom); Content = root;
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
    }
}

internal sealed class HistoryTimelineWindow : HistoryReadOnlyWindow
{
    internal HistoryTimelineWindow(Window owner, HistoryEventRow target, Action<HistoryEventRow, Window?> showDetail)
        : base(owner, "履歴タイムライン", $"{target.TargetNumber}　{target.TargetName}", 720)
    {
        var entries = new StackPanel();
        Body.Children.Add(new ScrollViewer { Content = entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var feedback = new TextBlock { Text = "履歴を読み込んでいます…", TextWrapping = TextWrapping.Wrap };
        entries.Children.Add(feedback);
        Loaded += async (_, _) =>
        {
            var token = WindowToken;
            try
            {
                using var db = new SalesManagementContext();
                var records = await HistoryManagementService.ReadTimelineAsync(db, target, token);
                if (token.IsCancellationRequested) return;
                feedback.Text = records.Count == 0 ? "対象の操作履歴は記録されていません。" : "記録された順に表示します。";
                foreach (var entry in records)
                {
                    var row = new DockPanel { LastChildFill = true };
                    var detail = new Button { Content = "詳細", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8,0,0,0) };
                    detail.Click += (_, _) => showDetail(entry, this);
                    DockPanel.SetDock(detail, Dock.Right); row.Children.Add(detail);
                    var text = new StackPanel();
                    text.Children.Add(new TextBlock { Text = $"{entry.Timestamp}　{entry.Operation}", Foreground = entry.OperationForeground,
                        FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    text.Children.Add(new TextBlock { Text = $"{entry.ActorName}　{entry.Summary}", TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(text);
                    entries.Children.Add(new Border { BorderBrush = AppTheme.Primary, BorderThickness = new Thickness(2,0,0,0),
                        Background = AppTheme.Surface, Margin = new Thickness(4,6,0,0), Padding = new Thickness(12,8,8,8), Child = row });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!token.IsCancellationRequested)
            { feedback.Text = ex is UnauthorizedAccessException ? "この履歴を参照する権限がありません。" : "履歴を取得できませんでした。"; }
            catch (Exception) when (token.IsCancellationRequested) { }
        };
    }
}

internal sealed class BusinessHistoryWindow : HistoryReadOnlyWindow
{
    internal static void OpenFor(FrameworkElement source, string module)
    {
        if (Window.GetWindow(source) is { } owner) new BusinessHistoryWindow(owner, module, lockModule: true).ShowDialog();
    }

    private const int PageSize = 20;
    private readonly ComboBox modules = new() { Width = 110, Margin = new Thickness(0,0,8,0) };
    private readonly TextBox search = new() { MinWidth = 150, ToolTip = "伝票番号・関連伝票番号・名称で検索" };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
    private readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button previous = new() { Content = "前へ", Margin = new Thickness(0,0,6,0) };
    private readonly Button next = new() { Content = "次へ", Margin = new Thickness(0,0,8,0) };
    private readonly Button detail = new() { Content = "詳細", Margin = new Thickness(0,0,8,0), IsEnabled = false };
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
        CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeight = 35, AlternatingRowBackground = AppTheme.Canvas, CanUserSortColumns = false };
    private readonly Action<int>? openOrder;
    private IReadOnlyList<BusinessHistoryRow> rows = [];
    private int page, request;
    private bool loading, opening, failed;

    internal BusinessHistoryWindow(Window owner, string module, bool lockModule = false, Action<int>? openOrder = null)
        : base(owner, lockModule ? module.Replace("管理", "履歴") : "業務履歴", "保存されている完了取引の参照です。操作監査の日時や実行者を表す一覧ではありません。", 960)
    {
        this.openOrder = openOrder;
        var authorized = BusinessHistoryService.GetModules(AppSession.Role);
        modules.ItemsSource = lockModule ? authorized.Where(x => x == module).ToArray() : authorized;
        modules.IsEnabled = !lockModule;
        modules.SelectedItem = authorized.Contains(module) ? module : authorized.FirstOrDefault();
        AutomationProperties.SetName(modules, "業務区分"); AutomationProperties.SetName(search, "伝票番号・名称で検索");
        var filters = new DockPanel(); filters.Children.Add(modules);
        var reload = new Button { Content = "再読込", Margin = new Thickness(8,0,0,0) };
        DockPanel.SetDock(reload, Dock.Right); filters.Children.Add(reload); filters.Children.Add(search);
        Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.RowDefinitions.Add(new());
        Body.Children.Add(filters); Grid.SetRow(message, 1); Body.Children.Add(message); Grid.SetRow(grid, 2); Body.Children.Add(grid);
        AddColumn("伝票番号", nameof(BusinessHistoryRow.Number), 1, 94); AddColumn("伝票日", nameof(BusinessHistoryRow.DateText), .95, 90);
        AddColumn("取引先・出庫先", nameof(BusinessHistoryRow.Target), 1.6, 90); AddColumn("関連伝票", nameof(BusinessHistoryRow.RelatedNumber), 1, 94);
        AddColumn("状態", nameof(BusinessHistoryRow.State), .8, 74); AddColumn("管理状態", nameof(BusinessHistoryRow.Management), .75, 68);
        foreach (var button in new[] { detail, next, previous }) { DockPanel.SetDock(button, Dock.Right); Footer.Children.Add(button); }
        if (lockModule && module == "在庫管理")
        {
            foreach (var related in authorized.Where(x => x is "入庫管理" or "出庫管理"))
            {
                var link = new Button { Content = related.Replace("管理", "履歴"), Margin = new Thickness(0,0,8,0) };
                link.Click += (_, _) => new BusinessHistoryWindow(this, related, lockModule: true).ShowDialog();
                DockPanel.SetDock(link, Dock.Left); Footer.Children.Add(link);
            }
        }
        Footer.Children.Add(count);
        search.TextChanged += (_, _) => { page = 0; UpdatePage(); };
        previous.Click += (_, _) => { page--; UpdatePage(); }; next.Click += (_, _) => { page++; UpdatePage(); };
        detail.Click += async (_, _) => await OpenDetailAsync();
        grid.SelectionChanged += (_, _) => UpdateDetailButton();
        grid.MouseDoubleClick += async (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow) await OpenDetailAsync();
        };
        Loaded += async (_, _) => await ReloadAsync();
        reload.Click += async (_, _) => await ReloadAsync();
        modules.SelectionChanged += async (_, _) => { if (IsLoaded) await ReloadAsync(); };
    }

    private void AddColumn(string title, string property, double width, double minimum)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7,0,7,0)));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width, DataGridLengthUnitType.Star), MinWidth = minimum, ElementStyle = style });
    }

    private async Task ReloadAsync()
    {
        var token = WindowToken;
        var version = ++request;
        var module = modules.SelectedItem as string;
        loading = true; failed = false; rows = []; page = 0; UpdatePage();
        message.Text = "業務履歴を読み込んでいます…";
        try
        {
            if (module == null) throw new UnauthorizedAccessException();
            var loaded = await BusinessHistoryService.ReadAsync(module, token);
            if (token.IsCancellationRequested || version != request) return;
            rows = loaded.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToArray();
            message.Text = module == "在庫管理" ? "在庫移動台帳がないため、増減前後の数量・日時・担当者は確認できません。"
                : module == "発注管理" ? "入庫が完了した発注を表示します。伝票日は発注日です。" : "完了した取引と保存されている伝票日を表示します。";
        }
        catch (Exception ex) when (!token.IsCancellationRequested && version == request)
        { failed = true; message.Text = ex is UnauthorizedAccessException ? "業務履歴を参照する権限がありません。" : "業務履歴を取得できませんでした。再読込してください。"; }
        catch (Exception) when (token.IsCancellationRequested || version != request) { }
        finally { if (!token.IsCancellationRequested && version == request) { loading = false; UpdatePage(); } }
    }

    private void UpdatePage()
    {
        var term = search.Text.Trim();
        var filtered = rows.Where(x => term.Length == 0 || new[] { x.Number, x.RelatedNumber, x.Target }.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        page = Math.Clamp(page, 0, Math.Max(0, (filtered.Length - 1) / PageSize));
        grid.ItemsSource = filtered.Skip(page * PageSize).Take(PageSize);
        previous.IsEnabled = !loading && page > 0;
        next.IsEnabled = !loading && (page + 1) * PageSize < filtered.Length;
        count.Text = loading || failed || Equals(modules.SelectedItem, "在庫管理") ? "—" : filtered.Length == 0 ? "該当する取引はありません" : $"{page * PageSize + 1}～{Math.Min(filtered.Length, (page + 1) * PageSize)}件 / 全{filtered.Length:N0}件";
        UpdateDetailButton();
    }

    private void UpdateDetailButton() => detail.IsEnabled = !opening && !loading && grid.SelectedItem is BusinessHistoryRow;

    private async Task OpenDetailAsync()
    {
        if (!detail.IsEnabled || grid.SelectedItem is not BusinessHistoryRow row) return;
        opening = true; UpdateDetailButton();
        var token = WindowToken;
        try
        {
            using var db = new SalesManagementContext();
            var actor = await BusinessHistoryService.AuthorizeAsync(db, row.Module, token);
            if (row.Module == "受注管理" && openOrder != null)
            {
                var admin = actor.Po.PoName == "管理者";
                if (!await db.TOrders.AsNoTracking().AnyAsync(x => x.OrId == row.Id && x.OrStateFlag == 1 && (admin || x.SoId == actor.SoId && x.OrFlag == 0), token))
                    throw new UnauthorizedAccessException();
                if (!token.IsCancellationRequested) { Close(); openOrder(row.Id); }
                return;
            }
            Window? dialog = row.Module switch
            {
                "注文管理" => await OpenChumonAsync(db, actor, row, token),
                "発注管理" => await Task.Run(() => PurchaseOrderService.LoadDetail(row.Id), token) is { Order.IsFullyReceived: true } po ? new F_HattyuDetail(po, readOnly: true) : null,
                "入庫管理" => await Task.Run(() => WarehousingService.LoadDetail(row.Id), token) is { Record.IsConfirmed: true } inbound ? new F_WarehouseMovementDetail(inbound, readOnly: true) : null,
                "出庫管理" => await Task.Run(() => OutboundService.LoadDetail(row.Id), token) is { Record.IsConfirmed: true } outbound ? new OutboundDetailWindow(outbound, readOnly: true) : null,
                "入荷管理" => await Task.Run(() => ArrivalService.LoadDetail(row.Id), token) is { Arrival.IsConfirmed: true } arrival ? new F_ArrivalDetail(arrival, readOnly: true) : null,
                "出荷管理" => await Task.Run(() => ShipmentService.LoadDetail(row.Id), token) is { Shipment.IsConfirmed: true } shipment ? new F_ShipmentDetail(shipment, readOnly: true) : null,
                "売上管理" => await Task.Run(() => SalesService.GetDetail(row.Id), token) is { } sale ? new F_SaleDetail(sale, canHide: false, this) : null,
                _ => null
            };
            if (token.IsCancellationRequested) { dialog?.Close(); return; }
            if (dialog == null) { message.Text = "対象の業務記録を参照できません。一覧を再読込してください。"; return; }
            dialog.Owner = this; dialog.ShowDialog();
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        { message.Text = ex is UnauthorizedAccessException ? "対象の業務記録を参照できません。" : "詳細を取得できませんでした。"; }
        catch (Exception) when (token.IsCancellationRequested) { }
        finally { opening = false; UpdateDetailButton(); }
    }

    private async Task<Window?> OpenChumonAsync(SalesManagementContext db, MEmployee actor, BusinessHistoryRow row, CancellationToken token)
    {
        var admin = actor.Po.PoName == "管理者";
        if (!await db.TChumons.AsNoTracking().AnyAsync(x => x.ChId == row.Id && x.ChStateFlag == 1 && (admin || x.SoId == actor.SoId && x.ChFlag == 0), token)) return null;
        var viewModel = new ChumonManagementViewModel(loadOnCreate: false);
        var model = await viewModel.GetDetailsAsync(row.Id);
        return model is { State: "確定済み" } && !token.IsCancellationRequested
            ? new OrderDetailDialog(model, viewModel, new OrderManagementRow { OrderId = row.Id, State = model.State }, this, readOnly: true) : null;
    }
}
