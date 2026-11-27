using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record ProductMasterOption(int Id, string Name);
public sealed record ProductRowData(int Id, string? JanCode, string Name, int MakerId, string Maker,
    int ClassificationId, string Classification,
    string ModelNumber, string Color, DateTime ReleaseDate, decimal Price, int SafetyStock, long? StockQuantity,
    int ManagementFlag, string? HiddenReason)
{
    public string Code => $"P-{Id:0000}";
    public InventoryRowData Inventory => new(null, Id, JanCode ?? "", Name, Maker, Classification, ModelNumber,
        Color, ReleaseDate, StockQuantity, SafetyStock, ManagementFlag);
}
public sealed record ProductEditSnapshot(int Id, string? JanCode, string Name, int MakerId, int ClassificationId,
    string ModelNumber, string Color, DateTime ReleaseDate, decimal Price, int SafetyStock, int ManagementFlag);
public sealed record ProductInput(string? JanCode, string Name, int MakerId, int ClassificationId,
    string ModelNumber, string Color, DateTime ReleaseDate, decimal Price, int SafetyStock);
public sealed record ProductOperationResult(bool Succeeded, string Message, int? ProductId = null);
public sealed record ProductCatalog(IReadOnlyList<ProductRowData> Products,
    IReadOnlyList<ProductMasterOption> Makers, IReadOnlyList<ProductMasterOption> Classifications);

/// <summary>商品マスタの読取・登録・更新・非表示／復元。</summary>
public static class ProductService
{
    public const int ActiveFlag = 0;
    // M_Product uses 1 for hidden records; older WPF code provisionally wrote 2.
    public const int HiddenFlag = 1;
    public static bool IsRestorable(int flag, string? reason) =>
        flag == HiddenFlag || flag == 2 && !string.IsNullOrWhiteSpace(reason);

    public static bool CanView => AppSession.Employee is { EmFlag: 0, Po.PoFlag: 0 }
        && (AppSession.IsAdmin || AppSession.IsLogistics);
    public static bool CanEdit => CanView;
    public static bool CanViewHidden => CanEdit;
    public static bool CanViewOrders => CanView && AppSession.IsAdmin;

    internal sealed record ActorScope(int Id, int OfficeId, string Role)
    {
        public bool IsAdmin => Role == "管理者";
        public bool CanMaintain => Role is "管理者" or "物流";
    }

    public static ProductCatalog Load()
    {
        using var db = new SalesManagementContext();
        var actor = GetActor(db);
        var products = Project(ReadableProducts(db, actor).OrderBy(x => x.PrId)).ToList();
        var makers = db.MMakers.AsNoTracking().Where(x => x.MaFlag == ActiveFlag)
            .OrderBy(x => x.MaName).Select(x => new ProductMasterOption(x.MaId, x.MaName)).ToList();
        var classifications = db.MSmallClassifications.AsNoTracking()
            .Where(x => x.ScFlag == ActiveFlag && x.Mc.McFlag == ActiveFlag)
            .OrderBy(x => x.Mc.McName).ThenBy(x => x.ScName)
            .Select(x => new ProductMasterOption(x.ScId, x.Mc.McName + " / " + x.ScName)).ToList();
        return new(products, makers, classifications);
    }

    public static ProductRowData? ReadDetail(int productId)
    {
        using var db = new SalesManagementContext();
        return Project(ReadableProducts(db, GetActor(db)).Where(x => x.PrId == productId)).SingleOrDefault();
    }

    private static IQueryable<MProduct> ReadableProducts(SalesManagementContext db, ActorScope actor) =>
        db.MProducts.AsNoTracking().Where(x => actor.CanMaintain || x.PrFlag == ActiveFlag);

    private static IQueryable<ProductRowData> Project(IQueryable<MProduct> products) =>
        products.Select(x => new ProductRowData(x.PrId, x.PrJcode, x.PrName, x.MaId, x.Ma.MaName, x.ScId,
            x.Sc.Mc.McName + " / " + x.Sc.ScName, x.PrModelNumber, x.PrColor, x.PrReleaseDate, x.Price, x.PrSafetyStock,
            x.TStocks.Any(s => s.StFlag == ActiveFlag) ? x.TStocks.Where(s => s.StFlag == ActiveFlag).Sum(s => (long?)s.StQuantity) : null,
            x.PrFlag, x.PrHidden));

    public static ProductOperationResult Create(int employeeId, ProductInput input)
    {
        using var db = new SalesManagementContext();
        return Create(db, employeeId, input);
    }

    internal static ProductOperationResult Create(SalesManagementContext db, int employeeId, ProductInput input)
    {
        if (!CanMaintain(employeeId)) return Denied();
        var invalid = ValidateInput(input);
        if (invalid != null) return new(false, invalid);
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            GetActor(db, employeeId);
            if (!ValidMasters(db, input.MakerId, input.ClassificationId))
                return new(false, "有効なメーカーと商品分類を選択してください。");
            var jan = NormalizeJan(input.JanCode);
            if (jan != null && db.MProducts.Any(x => x.PrJcode == jan && x.PrFlag == ActiveFlag))
                return new(false, "このJANコードは表示中の商品に登録されています。");

            var product = new MProduct
            {
                PrJcode = jan, PrName = input.Name.Trim(), MaId = input.MakerId, ScId = input.ClassificationId,
                PrModelNumber = input.ModelNumber.Trim(), PrColor = input.Color.Trim(),
                PrReleaseDate = input.ReleaseDate.Date, Price = input.Price, PrSafetyStock = input.SafetyStock,
                PrFlag = ActiveFlag, PrHidden = null
            };
            db.MProducts.Add(product);
            db.SaveChanges();
            if (!db.TStocks.Any(x => x.PrId == product.PrId))
                db.TStocks.Add(new TStock { PrId = product.PrId, StQuantity = 0, StFlag = ActiveFlag });
            db.SaveChanges();
            tx.Commit();
            return new(true, "商品を登録しました。初期在庫数は0です。", product.PrId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception)
        {
            return new(false, "商品を登録できませんでした。入力内容とデータベースの状態を確認してください。");
        }
    }

    public static ProductOperationResult Update(int employeeId, ProductEditSnapshot expected, ProductInput input)
    {
        using var db = new SalesManagementContext();
        return Update(db, employeeId, expected, input);
    }

    internal static ProductOperationResult Update(SalesManagementContext db, int employeeId, ProductEditSnapshot expected, ProductInput input)
    {
        if (!CanMaintain(employeeId)) return Denied();
        var invalid = ValidateInput(input);
        if (invalid != null) return new(false, invalid);
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            GetActor(db, employeeId);
            var product = FindForUpdate(db, expected.Id);
            if (product == null || product.PrFlag != ActiveFlag)
                return new(false, "商品が存在しないか、更新できる状態ではありません。");
            if (Snapshot(product) != expected)
                return new(false, "別の利用者が商品情報を更新しました。最新情報を読み込み直してください。");
            if (!ValidMasters(db, input.MakerId, input.ClassificationId))
                return new(false, "有効なメーカーと商品分類を選択してください。");
            var jan = NormalizeJan(input.JanCode);
            if (jan != null && db.MProducts.Any(x => x.PrId != product.PrId && x.PrJcode == jan && x.PrFlag == ActiveFlag))
                return new(false, "このJANコードは表示中の商品に登録されています。");

            product.PrJcode = jan; product.PrName = input.Name.Trim(); product.MaId = input.MakerId;
            product.ScId = input.ClassificationId; product.PrModelNumber = input.ModelNumber.Trim();
            product.PrColor = input.Color.Trim(); product.PrReleaseDate = input.ReleaseDate.Date;
            product.Price = input.Price; product.PrSafetyStock = input.SafetyStock;
            db.SaveChanges();
            tx.Commit();
            return new(true, "商品情報を更新しました。", product.PrId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception)
        {
            return new(false, "商品情報を更新できませんでした。一覧を再読込してからお試しください。");
        }
    }

    public static ProductOperationResult Hide(int employeeId, int productId, string? reason)
    {
        using var db = new SalesManagementContext();
        return Hide(db, employeeId, productId, reason);
    }

    internal static ProductOperationResult Hide(SalesManagementContext db, int employeeId, int productId, string? reason)
    {
        if (!CanMaintain(employeeId)) return Denied();
        if (productId <= 0) return new(false, "対象の商品を確認してください。");
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            GetActor(db, employeeId);
            var product = FindForUpdate(db, productId);
            if (product == null) return new(false, "商品が存在しません。");
            if (product.PrFlag != ActiveFlag) return new(false, "商品は表示中ではありません。");
            if (db.TOrderDetails.Any(x => x.PrId == productId && (x.OrQuantity != 0 || x.OrTotalPrice != 0m)
                && x.Or.OrFlag == 0 && (x.Or.OrStateFlag ?? 0) == 0))
                return new(false, "未確定の受注で使用中の商品は非表示にできません。先に受注を確定または取り消してください。");
            product.PrFlag = HiddenFlag;
            product.PrHidden = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            db.SaveChanges();
            tx.Commit();
            return new(true, "商品を非表示にしました。商品・在庫・取引データは削除していません。", product.PrId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception)
        {
            return new(false, "商品を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    public static ProductOperationResult Restore(int employeeId, ProductRowData expected)
    {
        using var db = new SalesManagementContext();
        return Restore(db, employeeId, expected);
    }

    internal static ProductOperationResult Restore(SalesManagementContext db, int employeeId, ProductRowData expected)
    {
        if (!CanMaintain(employeeId)) return Denied();
        if (expected.Id <= 0 || !IsRestorable(expected.ManagementFlag, expected.HiddenReason))
            return new(false, "この商品の管理状態は復元対象ではありません。");
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            GetActor(db, employeeId);
            var product = FindForUpdate(db, expected.Id);
            if (product == null || product.PrFlag != expected.ManagementFlag || product.PrHidden != expected.HiddenReason
                || product.PrJcode != expected.JanCode)
                return new(false, "商品情報が変更されました。一覧を再読込してください。");
            var jan = NormalizeJan(product.PrJcode);
            if (jan != null && db.MProducts.Any(x => x.PrId != product.PrId && x.PrFlag == ActiveFlag && x.PrJcode == jan))
                return new(false, "同じJANコードの商品が表示中のため、復元できません。");
            product.PrFlag = ActiveFlag;
            product.PrHidden = null;
            db.SaveChanges();
            tx.Commit();
            return new(true, "商品を表示中に戻しました。", product.PrId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception) { return new(false, "商品の表示を復元できませんでした。一覧を再読込してからお試しください。"); }
    }

    private static MProduct? FindForUpdate(SalesManagementContext db, int productId) =>
        db.MProducts.FromSqlInterpolated($"SELECT * FROM dbo.M_Product WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {productId}").SingleOrDefault();

    public static ProductEditSnapshot Snapshot(ProductRowData row) =>
        new(row.Id, row.JanCode, row.Name, row.MakerId, row.ClassificationId, row.ModelNumber,
            row.Color, row.ReleaseDate, row.Price, row.SafetyStock, row.ManagementFlag);

    public static ProductEditSnapshot Snapshot(MProduct product) =>
        new(product.PrId, product.PrJcode, product.PrName, product.MaId, product.ScId, product.PrModelNumber,
            product.PrColor, product.PrReleaseDate, product.Price, product.PrSafetyStock, product.PrFlag);

    private static bool CanMaintain(int employeeId) => CanEdit && AppSession.Employee is { } actor
        && actor.EmId == employeeId;

    private static bool ValidMasters(SalesManagementContext db, int makerId, int classificationId) =>
        db.MMakers.Any(x => x.MaId == makerId && x.MaFlag == ActiveFlag)
        && db.MSmallClassifications.Any(x => x.ScId == classificationId && x.ScFlag == ActiveFlag && x.Mc.McFlag == ActiveFlag);

    public static string? ValidateInput(ProductInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "商品名を入力してください。";
        if (input.Name.Trim().Length > 50) return "商品名は50文字以内で入力してください。";
        if (string.IsNullOrWhiteSpace(input.ModelNumber) || input.ModelNumber.Trim().Length > 20)
            return "型番は必須です。20文字以内で入力してください。";
        if (string.IsNullOrWhiteSpace(input.Color) || input.Color.Trim().Length > 20)
            return "色は必須です。20文字以内で入力してください。";
        if (input.MakerId <= 0 || input.ClassificationId <= 0) return "メーカーと商品分類を選択してください。";
        if (input.Price < 0 || input.Price > 999_999_999 || decimal.Truncate(input.Price) != input.Price)
            return "販売価格は0～999,999,999の整数で入力してください。";
        if (input.SafetyStock < 0) return "安全在庫は0以上の整数で入力してください。";
        if (input.ReleaseDate == default) return "発売日を入力してください。";
        var jan = NormalizeJan(input.JanCode);
        if (jan is { Length: > 13 }) return "JANコードは13文字以内で入力してください。";
        return null;
    }

    private static string? NormalizeJan(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static ActorScope GetActor(SalesManagementContext db, int? maintainingEmployeeId = null)
    {
        const string denied = "商品情報を表示または変更する権限がありません。再ログインしてください。";
        if (!CanView || AppSession.Employee is not { } session || maintainingEmployeeId.HasValue && session.EmId != maintainingEmployeeId)
            throw new UnauthorizedAccessException(denied);
        var actor = db.MEmployees.AsNoTracking().Where(x => x.EmId == session.EmId && x.EmFlag == 0 && x.Po.PoFlag == 0)
            .Select(x => new ActorScope(x.EmId, x.SoId, x.Po.PoName)).SingleOrDefault();
        if (actor == null || actor.Role != session.Po.PoName || actor.OfficeId != session.SoId
            || maintainingEmployeeId.HasValue && !actor.CanMaintain) throw new UnauthorizedAccessException(denied);
        return actor;
    }
    private static ProductOperationResult Denied() => new(false, "商品情報を変更する権限がありません。画面を開き直してください。");
}
