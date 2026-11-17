using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class ProductManagementView : UserControl
{
    private ProductManagementViewModel ViewModel => (ProductManagementViewModel)DataContext;
    private bool presentingDetail;

    public ProductManagementView() : this(new ProductManagementViewModel()) { }

    internal ProductManagementView(ProductManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        UpdateTabVisuals(viewModel.ActiveTab);
    }

    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(ProductStatusTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SelectTab(ProductStatusTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(ProductStatusTab.Hidden);

    private void SelectTab(ProductStatusTab tab)
    {
        ViewModel.SelectTab(tab);
        UpdateTabVisuals(ViewModel.ActiveTab);
    }

    private void UpdateTabVisuals(ProductStatusTab active)
    {
        foreach (var (button, tab) in new[]
        {
            (AllTab, ProductStatusTab.All), (VisibleTab, ProductStatusTab.Visible), (HiddenTab, ProductStatusTab.Hidden)
        })
        {
            var selected = tab == active;
            button.Tag = selected ? "StatusActive" : null;
        }
    }

    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.IsFiltersExpanded = !ViewModel.IsFiltersExpanded;
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => ViewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.QuickSearch = "";
        QuickSearchBox.Focus();
    }
    private void ReleaseDate_ValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        ViewModel.ReportInvalidDate();
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber--;
    private void NextPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber++;
    private void ProductView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged && AdvancedFieldsScroll != null)
            AdvancedFieldsScroll.MaxHeight = Math.Clamp(e.NewSize.Height - 365, 90, 220);
    }
    private void ProductGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || ProductNameColumn == null || CategoryColumn == null || sender is not DataGrid grid) return;
        var fixedWidth = grid.Columns.Where(x => x != ProductNameColumn && x != CategoryColumn)
            .Sum(x => x.Width.IsAbsolute ? x.Width.Value : x.ActualWidth);
        var free = Math.Max(0, Math.Floor(e.NewSize.Width - fixedWidth - ProductNameColumn.MinWidth - CategoryColumn.MinWidth
            - SystemParameters.VerticalScrollBarWidth - grid.BorderThickness.Left - grid.BorderThickness.Right - 2));
        var nameShare = Math.Floor(free * .6);
        ProductNameColumn.Width = new DataGridLength(ProductNameColumn.MinWidth + nameShare);
        CategoryColumn.Width = new DataGridLength(CategoryColumn.MinWidth + free - nameShare);
    }
    private void ProductGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in ProductGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        ViewModel.SortBy(e.Column.SortMemberPath, direction);
    }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelection();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => ViewModel.SelectAllVisible(ViewModel.VisibleSelectionState != true);

    internal void OpenRegistration()
    {
        if (!ViewModel.CanRegister) return;
        var dialog = new ProductEditorDialog(ViewModel, null, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
            F_OrderNotice.Show(Window.GetWindow(this), "商品登録完了", dialog.ResultMessage, OrderNoticeKind.Success);
    }

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProductRow row) _ = OpenDetailAsync(row.Id);
    }

    private void ProductGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        for (var parent = source; parent != null && parent != ProductGrid;
            parent = parent is Visual ? VisualTreeHelper.GetParent(parent) : LogicalTreeHelper.GetParent(parent))
        {
            if (parent is ButtonBase) return;
            if (parent is DataGridRow { Item: ProductRow row })
            {
                e.Handled = true;
                _ = OpenDetailAsync(row.Id);
                return;
            }
        }
    }

    private async Task OpenDetailAsync(int productId)
    {
        if (presentingDetail || !ViewModel.CanReload) return;
        presentingDetail = true;
        var owner = Window.GetWindow(this);
        try
        {
            ProductRowData? product;
            try { product = await Task.Run(() => ProductService.ReadDetail(productId)); }
            catch (Exception ex)
            {
                if (IsLoaded) F_OrderNotice.Show(owner, "商品詳細", ex is UnauthorizedAccessException ? ex.Message
                    : "商品情報を取得できませんでした。接続状態を確認してください。", OrderNoticeKind.Warning);
                return;
            }
            if (!IsLoaded) return;
            if (product == null)
            {
                F_OrderNotice.Show(owner, "商品詳細", "商品が見つからないか、参照範囲外です。", OrderNoticeKind.Warning);
                return;
            }
            var detail = new ProductDetailDialog(product, ViewModel.CanEditProducts, owner);
            detail.ShowDialog();
            if (detail.NextAction == "edit") OpenEditor(product);
            else if (detail.NextAction == "hide") await HideRowsAsync([new ProductRow(product)]);
            else if (detail.NextAction == "restore") await RestoreAsync(product);
        }
        finally { presentingDetail = false; }
    }

    private async void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        await HideRowsAsync(ViewModel.SelectedRows);
    }

    private async Task HideRowsAsync(IReadOnlyCollection<ProductRow> rows)
    {
        if (rows.Count == 0 || !ViewModel.CanEditProducts) return;
        var owner = Window.GetWindow(this);
        var dialog = new ProductHideDialog(rows, owner);
        if (dialog.ShowDialog() != true) return;
        var result = await ViewModel.HideAsync(rows, dialog.Reason);
        F_OrderNotice.Show(owner, result.Succeeded ? "商品を非表示にしました" : "商品非表示の結果", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }

    private void OpenEditor(ProductRowData product)
    {
        if (!ViewModel.CanEditProducts || product.ManagementFlag != ProductService.ActiveFlag) return;
        var dialog = new ProductEditorDialog(ViewModel, product, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
        {
            if (ViewModel.Rows.FirstOrDefault(x => x.Id == product.Id) is { } updated)
            {
                ProductGrid.SelectedItem = updated;
                ProductGrid.ScrollIntoView(updated);
            }
            F_OrderNotice.Show(Window.GetWindow(this), "商品更新", dialog.ResultMessage, OrderNoticeKind.Success);
        }
    }

    private async Task RestoreAsync(ProductRowData product)
    {
        if (!ViewModel.CanEditProducts || !ProductService.IsRestorable(product.ManagementFlag, product.HiddenReason)) return;
        var owner = Window.GetWindow(this);
        if (!F_OrderNotice.Confirm(owner, "商品を表示に戻しますか？",
            $"商品コード：{product.Code}\n商品名：{product.Name}\n管理状態：非表示 → 表示中\n\n商品の表示状態だけを変更します。",
            "表示に戻す")) return;
        var result = await ViewModel.RestoreAsync(product);
        F_OrderNotice.Show(owner, result.Succeeded ? "商品の表示を復元しました" : "復元できませんでした", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }
}

internal sealed class ProductEditorDialog : Window
{
    private readonly ProductManagementViewModel ownerViewModel;
    private readonly ProductRowData? original;
    private readonly TextBox jan = Input(13), name = Input(50), model = Input(20), color = Input(20), price = Input(13), safetyStock = Input(10);
    private readonly ComboBox maker = MasterPicker(), classification = MasterPicker();
    private readonly DatePicker releaseDate = new() { Height = 34, SelectedDateFormat = DatePickerFormat.Short };
    private readonly Dictionary<Control, TextBlock> fieldErrors = [];
    private readonly Grid form = new() { Margin = new Thickness(16) };
    private readonly Button saveButton = new() { IsDefault = true, Background = AppTheme.Primary, Foreground = Brushes.White, BorderBrush = AppTheme.Primary };
    private readonly Button cancelButton = new() { Content = "キャンセル", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock operationError = new() { Foreground = AppTheme.DangerText, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Draft initial;
    private bool isSubmitting, saved;

    private sealed record Draft(string Jan, string Name, int Maker, int Category, string Model, string Color, DateTime? Release, string Price, string SafetyStock);

    public string ResultMessage { get; private set; } = "";

    public ProductEditorDialog(ProductManagementViewModel ownerViewModel, ProductRowData? product, Window? owner)
    {
        this.ownerViewModel = ownerViewModel;
        original = product;
        Owner = owner;
        Title = product == null ? "商品登録" : "商品更新";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var width = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var height = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(860, width * .94);
        MaxHeight = Math.Min(height * .88, SystemParameters.WorkArea.Height * .9);
        SizeToContent = SizeToContent.Height;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("ja-JP");

        maker.ItemsSource = ownerViewModel.Makers;
        classification.ItemsSource = ownerViewModel.Classifications;
        if (product != null)
        {
            jan.Text = product.JanCode ?? ""; name.Text = product.Name; model.Text = product.ModelNumber; color.Text = product.Color;
            maker.SelectedValue = product.MakerId; classification.SelectedValue = product.ClassificationId;
        }
        releaseDate.SelectedDate = product?.ReleaseDate ?? DateTime.Today;
        price.Text = product?.Price.ToString("0", CultureInfo.CurrentCulture) ?? "0";
        safetyStock.Text = product?.SafetyStock.ToString(CultureInfo.CurrentCulture) ?? "0";
        initial = Capture();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(Header(Title, product == null ? "新しい商品を登録します。" : $"{product.Code}　{product.Name}　／　表示中"));

        var basics = new StackPanel();
        basics.Children.Add(SectionTitle("商品基本情報"));
        AddValue(basics, "商品コード", product?.Code ?? "登録時に自動採番");
        AddField(basics, "商品名 *", name);
        AddField(basics, "カテゴリ *", classification);
        AddField(basics, "メーカー *", maker);
        AddField(basics, "型番 *", model);
        AddField(basics, "色 *", color);
        AddField(basics, "発売日 *", releaseDate);
        AddField(basics, "JANコード", jan);

        var settings = new StackPanel();
        settings.Children.Add(SectionTitle("価格・管理情報"));
        AddField(settings, "販売単価（円） *", price);
        AddField(settings, "安全在庫 *", safetyStock);
        AddValue(settings, "管理状態", "表示中");
        settings.Children.Add(new TextBlock
        {
            Text = product == null ? "商品登録時の現在庫は0です。" : "販売単価の変更は今後の取引に適用されます。保存済みの明細金額や現在庫は変更されません。",
            TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.SecondaryText, Margin = new Thickness(0, 10, 0, 0)
        });
        var left = FormCard(basics);
        var right = FormCard(settings);
        if (Width >= 610)
        {
            form.ColumnDefinitions.Add(new ColumnDefinition()); form.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(right, 1); right.Margin = new Thickness(10, 0, 0, 0);
        }
        else
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(right, 1); right.Margin = new Thickness(0, 10, 0, 0);
        }
        form.Children.Add(left); form.Children.Add(right);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        saveButton.Content = product == null ? "商品を登録" : "変更を保存";
        saveButton.Click += async (_, _) => await SaveAsync();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(cancelButton); actions.Children.Add(saveButton);
        var footer = new DockPanel { Margin = new Thickness(16, 9, 16, 10) };
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(operationError);
        var footerCard = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(footerCard, 2); root.Children.Add(footerCard);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = root };

        foreach (var textBox in new[] { jan, name, model, color, price, safetyStock }) textBox.TextChanged += (_, _) => InputChanged();
        maker.SelectionChanged += (_, _) => InputChanged();
        classification.SelectionChanged += (_, _) => InputChanged();
        releaseDate.SelectedDateChanged += (_, _) => InputChanged();
        releaseDate.DateValidationError += (_, e) =>
        {
            e.ThrowException = false;
            operationError.Text = "発売日に有効な日付を入力してください。無効な入力は適用されません。";
        };
        Closing += (_, e) =>
        {
            if (saved) return;
            if (isSubmitting) { e.Cancel = true; return; }
            if (Capture() != initial && !F_OrderNotice.Confirm(this, "未保存の変更", "入力した内容を破棄して閉じますか？", "破棄する", "入力に戻る"))
                e.Cancel = true;
        };
        RefreshForm(showErrors: false);
    }

    private Draft Capture() => new(jan.Text.Trim(), name.Text.Trim(), (int?)maker.SelectedValue ?? 0,
        (int?)classification.SelectedValue ?? 0, model.Text.Trim(), color.Text.Trim(),
        releaseDate.SelectedDate, price.Text.Trim(), safetyStock.Text.Trim());

    private void InputChanged()
    {
        operationError.Text = "";
        RefreshForm(showErrors: true);
    }

    private void RefreshForm(bool showErrors) => saveButton.IsEnabled =
        !isSubmitting && BuildInput(showErrors) != null && (original == null || Capture() != initial);

    private ProductInput? BuildInput(bool showErrors)
    {
        var draft = Capture();
        var parsedPrice = decimal.TryParse(draft.Price, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount);
        var parsedStock = int.TryParse(draft.SafetyStock, NumberStyles.Integer, CultureInfo.CurrentCulture, out var stock);
        var valid = true;
        void Check(Control field, bool acceptable, string message)
        {
            if (!acceptable) valid = false;
            var text = fieldErrors[field];
            text.Text = acceptable || !showErrors ? "" : message;
            text.Visibility = text.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        Check(name, draft.Name.Length is > 0 and <= 50, "商品名を50文字以内で入力してください。");
        Check(classification, draft.Category > 0, "カテゴリを選択してください。");
        Check(maker, draft.Maker > 0, "メーカーを選択してください。");
        Check(model, draft.Model.Length is > 0 and <= 20, "型番を20文字以内で入力してください。");
        Check(color, draft.Color.Length is > 0 and <= 20, "色を20文字以内で入力してください。");
        Check(releaseDate, draft.Release is { } date && date != default, "発売日を入力してください。");
        Check(jan, draft.Jan.Length <= 13, "JANコードは13文字以内で入力してください。");
        Check(price, parsedPrice && amount is >= 0 and <= 999_999_999 && decimal.Truncate(amount) == amount, "0～999,999,999の整数を入力してください。");
        Check(safetyStock, parsedStock && stock >= 0, "0以上の整数を入力してください。");
        return valid ? new ProductInput(draft.Jan, draft.Name, draft.Maker, draft.Category, draft.Model, draft.Color, draft.Release!.Value, amount, stock) : null;
    }

    private async Task SaveAsync()
    {
        if (isSubmitting || !ownerViewModel.CanEditProducts || original != null && Capture() == initial) return;
        var input = BuildInput(showErrors: true);
        if (input == null) return;
        isSubmitting = true; saveButton.IsEnabled = cancelButton.IsEnabled = form.IsEnabled = false; operationError.Text = "";
        try
        {
            var result = original == null
                ? await ownerViewModel.CreateAsync(input)
                : await ownerViewModel.UpdateAsync(ProductService.Snapshot(original), input);
            if (!result.Succeeded) { operationError.Text = result.Message; return; }
            ResultMessage = result.Message + (result.ProductId is { } id ? $"\n商品コード：P-{id:0000}" : "");
            saved = true;
            DialogResult = true;
        }
        finally
        {
            isSubmitting = false; cancelButton.IsEnabled = form.IsEnabled = true;
            if (!saved) RefreshForm(showErrors: true);
        }
    }

    private static TextBox Input(int length) => new() { Height = 34, MaxLength = length, Padding = new Thickness(8, 4, 8, 4) };
    private static ComboBox MasterPicker() => new() { Height = 34, DisplayMemberPath = nameof(ProductMasterOption.Name), SelectedValuePath = nameof(ProductMasterOption.Id) };
    private static TextBlock Label(string text) => new() { Text = text, Foreground = AppTheme.Text, Margin = new Thickness(0, 7, 0, 3) };
    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Primary, Margin = new Thickness(0, 0, 0, 6)
    };

    private void AddField(Panel target, string label, Control input)
    {
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        target.Children.Add(Label(label));
        target.Children.Add(input);
        var error = new TextBlock { Foreground = AppTheme.DangerText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        fieldErrors.Add(input, error);
        target.Children.Add(error);
    }

    private static void AddValue(Panel target, string label, string value)
    {
        target.Children.Add(Label(label));
        target.Children.Add(new TextBlock { Text = value, Foreground = AppTheme.Text, Background = AppTheme.Canvas, Padding = new Thickness(8, 6, 8, 6) });
    }

    private static Border FormCard(UIElement content) => new()
    {
        Child = content, Background = AppTheme.Surface, BorderBrush = AppTheme.Border,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14)
    };

    internal static UIElement Header(string title, string subtitle)
    {
        var bar = new DockPanel();
        var close = new Button { Content = "×", Width = 32, Height = 32, FontSize = 20, Padding = new Thickness(0),
            Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "閉じる" };
        System.Windows.Automation.AutomationProperties.SetName(close, "閉じる");
        close.Click += (_, _) => Window.GetWindow(close)?.Close();
        DockPanel.SetDock(close, Dock.Right); bar.Children.Add(close);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = AppTheme.PaleBlue, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        bar.Children.Add(stack);
        return new Border { Background = AppTheme.Primary, Padding = new Thickness(16, 9, 12, 9), Child = bar };
    }
}

internal sealed class ProductDetailDialog : Window
{
    public string? NextAction { get; private set; }

    public ProductDetailDialog(ProductRowData product, bool canEdit, Window? owner)
    {
        Title = "商品詳細";
        Owner = owner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var width = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var height = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(900, width * .94); MinWidth = Math.Min(600, Width);
        MaxHeight = Math.Min(height * .88, SystemParameters.WorkArea.Height * .92);
        Height = Math.Min(560, MaxHeight);
        var visible = product.ManagementFlag == ProductService.ActiveFlag;
        var state = visible ? "表示中" : "非表示";

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(ProductEditorDialog.Header(Title, $"{product.Code}　{product.Name}　／　{state}"));

        var cards = new Grid { Margin = new Thickness(2, 10, 2, 2) };
        cards.ColumnDefinitions.Add(new ColumnDefinition()); cards.ColumnDefinitions.Add(new ColumnDefinition());
        var basics = Card("商品基本情報", ("商品コード", product.Code), ("商品名", product.Name), ("カテゴリ", product.Classification),
            ("メーカー", product.Maker), ("JANコード", product.JanCode ?? "未登録"), ("型番", product.ModelNumber), ("色", product.Color),
            ("発売日", product.ReleaseDate.ToString("yyyy/MM/dd")));
        var pricing = Card("価格・管理情報", ("販売単価", $"¥{product.Price:N0}"), ("管理状態", state), ("非表示理由", product.HiddenReason ?? "—"));
        Grid.SetColumn(pricing, 1); pricing.Margin = new Thickness(10, 0, 0, 0); cards.Children.Add(basics); cards.Children.Add(pricing);

        var inventory = product.Inventory;
        var stock = new StackPanel { Margin = new Thickness(2, 10, 2, 2) };
        stock.Children.Add(Card("在庫情報", ("現在庫数", inventory.QuantityText), ("安全在庫数", inventory.SafetyStockText), ("在庫状態", inventory.ConditionLabel)));
        stock.Children.Add(Notice("在庫の増減前後や処理日時を確認できる移動履歴は記録されていません。"));

        var tabs = new TabControl { Margin = new Thickness(14), Background = AppTheme.Surface, BorderThickness = new Thickness(0) };
        tabs.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/SalesManagement_SysDev;component/Resources/EmployeeDetail.xaml", UriKind.Relative)
        });
        tabs.Items.Add(new TabItem { Header = "基本情報", Content = Scroll(cards) });
        tabs.Items.Add(new TabItem { Header = "在庫情報", Content = Scroll(stock) });
        tabs.Items.Add(new TabItem { Header = "取引履歴（受注）", Content = ProductService.CanViewOrders
            ? CustomerOrderHistoryView.ForProduct(product.Id) : Notice("受注の参照には管理者または営業担当者の権限が必要です。") });
        tabs.Items.Add(new TabItem { Header = "変更履歴", Content = Notice("商品マスタの変更履歴は記録されていません。") });
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 9, 16, 10) };
        void AddAction(string label, string action, bool primary)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0),
                Background = primary ? AppTheme.Primary : AppTheme.Surface, Foreground = primary ? Brushes.White : AppTheme.DangerText,
                BorderBrush = primary ? AppTheme.Primary : AppTheme.Danger };
            button.Click += (_, _) => { NextAction = action; Close(); };
            actions.Children.Add(button);
        }
        if (canEdit && visible)
        {
            AddAction("編集", "edit", true);
            AddAction("非表示にする", "hide", false);
        }
        else if (canEdit && ProductService.IsRestorable(product.ManagementFlag, product.HiddenReason))
            AddAction("表示を復元", "restore", true);
        actions.Children.Add(new Button { Content = "閉じる", IsCancel = true });
        var footer = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = actions };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = root };
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static Border Notice(string text) => new()
    {
        Background = AppTheme.Canvas, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 10, 2, 2),
        Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = text, Foreground = AppTheme.SecondaryText, TextWrapping = TextWrapping.Wrap }
    };

    private static Border Card(string title, params (string Label, string Value)[] fields)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Primary, Margin = new Thickness(0, 0, 0, 7) });
        foreach (var field in fields)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(102) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = field.Label, Foreground = AppTheme.SecondaryText, Margin = new Thickness(0, 0, 8, 0) });
            var value = new TextBlock { Text = field.Value, Foreground = AppTheme.Text, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(value, 1); row.Children.Add(value); stack.Children.Add(row);
        }
        return new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = stack };
    }
}

internal sealed class ProductHideDialog : Window
{
    private readonly TextBox reason = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 76,
        MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10),
        Background = AppTheme.Surface, BorderBrush = AppTheme.InputBorder };
    public string Reason => reason.Text.Trim();

    public ProductHideDialog(IReadOnlyCollection<ProductRow> products, Window? owner)
    {
        Owner = owner; Title = "商品を非表示にしますか？"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Width = 520; MaxWidth = Math.Max(360, (owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width) * .9);
        MaxHeight = Math.Max(340, (owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height) * .85);
        SizeToContent = SizeToContent.Height;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new Border { Background = AppTheme.Primary, Padding = new Thickness(20, 0, 20, 0), Child = new TextBlock { Text = Title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } });
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 14) };
        body.Children.Add(new TextBlock { Text = products.Count == 1 ? $"商品 P-{products.First().Id:0000} を非表示にしますか？" : $"選択した {products.Count:N0} 件の商品を非表示にしますか？", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var entries = string.Join(Environment.NewLine, products.Take(8).Select(x => $"商品コード：{x.Code}　商品名：{x.Name}\n管理状態：{x.ManagementState}"));
        if (products.Count > 8) entries += $"\nほか {products.Count - 8:N0} 件";
        body.Children.Add(new TextBlock { Text = entries, Background = AppTheme.Canvas, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = "非表示理由（任意）", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 5) });
        body.Children.Add(reason);
        body.Children.Add(new TextBlock
        {
            Text = "非表示にすると、通常の商品一覧と新規受注の商品候補から外れます。既存の受注・売上・在庫データは保持されます。",
            Foreground = AppTheme.WarningText, Background = AppTheme.Canvas, Padding = new Thickness(10),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 10, 20, 14) };
        actions.Children.Add(new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), BorderBrush = AppTheme.Border });
        var hide = new Button { Content = "非表示にする", Padding = new Thickness(16, 8, 16, 8),
            Background = AppTheme.Danger, Foreground = Brushes.White, BorderBrush = AppTheme.Danger };
        hide.Click += (_, _) => DialogResult = true; actions.Children.Add(hide);
        var footer = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = actions };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
    }
}
