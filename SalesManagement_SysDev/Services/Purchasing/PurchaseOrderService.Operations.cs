using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static partial class PurchaseOrderService
{
    public static bool CanAssignOffice => CanView && AppSession.IsAdmin;

    public sealed record OfficeOption(int Id, string Name);

    public static IReadOnlyList<OfficeOption> LoadAssignableOffices(int actorId)
    {
        if (!CanAssignOffice || AppSession.Employee?.EmId != actorId)
            throw new UnauthorizedAccessException("営業所の割り当ては管理者のみ実行できます。");
        using var db = new SalesManagementContext();
        if (ResolveActor(db, actorId) is not { IsAdministrator: true })
            throw new UnauthorizedAccessException("営業所の割り当ては管理者のみ実行できます。");
        return db.MSalesOffices.AsNoTracking().Where(x => x.SoFlag == 0)
            .OrderBy(x => x.SoId).Select(x => new OfficeOption(x.SoId, x.SoName)).ToList();
    }

    public static PurchaseOperationResult AssignOffice(int actorId, int orderId, int officeId)
    {
        if (!CanAssignOffice || AppSession.Employee?.EmId != actorId)
            return Denied("営業所の割り当ては管理者のみ実行できます。");
        try
        {
            using var db = new SalesManagementContext();
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            if (ResolveActor(db, actorId) is not { IsAdministrator: true })
                return Denied("営業所の割り当ては管理者のみ実行できます。");
            var order = db.THattyus.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Hattyu WITH (UPDLOCK, HOLDLOCK) WHERE HaID = {orderId}").SingleOrDefault();
            if (order == null || order.RegisteredOfficeId.HasValue)
                return Denied("営業所が未設定の発注を選択してください。");
            if (!db.MSalesOffices.Any(x => x.SoId == officeId && x.SoFlag == 0))
                return Denied("有効な営業所を選択してください。");
            order.RegisteredOfficeId = officeId;
            db.SaveChanges();
            transaction.Commit();
            return new(true, orderId, "発注と関連入庫の営業所を設定しました。");
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("発注の営業所設定に失敗しました: {0}", error);
            return Denied("営業所を設定できませんでした。一覧を再読込してください。");
        }
    }

    public static PurchaseOperationResult Confirm(int orderId, int actorId)
    {
        if (!CanView || AppSession.Employee?.EmId != actorId)
            return Denied("発注を確定する権限がありません。");
        try
        {
            using var db = new SalesManagementContext();
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = ResolveActor(db, actorId);
            EnsureCanView(actor);
            var order = db.THattyus.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Hattyu WITH (UPDLOCK, HOLDLOCK) WHERE HaID = {orderId}")
                .Include(x => x.THattyuDetails).SingleOrDefault();
            if (order == null || order.HaFlag != ActiveFlag
                || (!actor!.IsAdministrator && order.RegisteredOfficeId != actor.OfficeId))
                return Denied("操作できる表示中の発注が見つかりません。");
            if (order.WaWarehouseFlag != 0 || db.TWarehousings.Any(x => x.HaId == orderId))
                return Denied("この発注は確定済みか、関連する入庫がすでにあります。");
            if (order.THattyuDetails.Count == 0 || order.THattyuDetails.Any(x => x.HaQuantity <= 0))
                return Denied("発注明細の商品と数量を確認してください。");

            db.Database.ExecuteSqlRaw("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.T_Warehousing')
                    AND name = N'WaDate' AND is_nullable = 0)
                    ALTER TABLE dbo.T_Warehousing ALTER COLUMN WaDate date NULL;
                """);
            var receipt = new TWarehousing { HaId = order.HaId, WaFlag = ActiveFlag, WaShelfFlag = 0 };
            foreach (var line in order.THattyuDetails)
                receipt.TWarehousingDetails.Add(new TWarehousingDetail { PrId = line.PrId, WaQuantity = line.HaQuantity });
            db.TWarehousings.Add(receipt);
            order.WaWarehouseFlag = 1;
            db.SaveChanges();
            transaction.Commit();
            return new(true, orderId, "発注を確定し、入庫情報を作成しました。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return Denied("発注を確定できませんでした。発注明細と入庫テーブルの状態を確認してください。");
        }
    }

    public static string? ValidateHideReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "非表示理由を入力してください。";
        return reason.Trim().Length > MaximumHideReasonLength
            ? $"非表示理由は{MaximumHideReasonLength}文字以内で入力してください。" : null;
    }

    public static PurchaseOperationResult Hide(int actorId, int orderId, string? reason)
    {
        if (!CanHide || AppSession.Employee?.EmId != actorId) return Denied("発注を非表示にする権限がありません。管理者または物流担当としてログインしてください。");
        if (ValidateHideReason(reason) is { } validation) return Denied(validation);

        using var db = new SalesManagementContext();
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        var actor = ResolveActor(db, actorId);
        if (actor is not { IsAllowedPurchaser: true }) return Denied("発注を非表示にする権限がありません。");
        var order = db.THattyus.FirstOrDefault(x => x.HaId == orderId && x.HaFlag == ActiveFlag
            && (actor.IsAdministrator || x.RegisteredOfficeId == actor.OfficeId));
        if (order == null) return Denied("対象の発注は見つからないか、すでに非表示です。");

        order.HaFlag = HiddenFlag;
        order.HaHidden = reason!.Trim();
        db.SaveChanges();
        transaction.Commit();
        return new(true, order.HaId, "発注を非表示にしました。入庫記録と在庫情報は変更していません。");
    }

    public static string? ValidateRegistration(int makerId, DateTime? orderDate, IReadOnlyList<PurchaseLineInput> lines)
    {
        if (makerId <= 0) return "メーカーを選択してください。";
        if (orderDate is null || orderDate.Value == default) return "発注日を入力してください。";
        if (lines.Count == 0) return "発注明細を1件以上追加してください。";
        if (lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0)) return "商品と1以上の発注数量を確認してください。";
        if (lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            return "同じ商品を重複して明細に追加できません。数量を1行にまとめてください。";
        return null;
    }

    public static PurchaseOperationResult Register(int actorId, int makerId, DateTime? orderDate,
        IReadOnlyList<PurchaseLineInput> lines)
    {
        if (!CanRegister) return Denied("発注を登録する権限がありません。");
        if (ValidateRegistration(makerId, orderDate, lines) is { } validation) return Denied(validation);
        if (Interlocked.CompareExchange(ref registrationInProgress, 1, 0) != 0)
            return Denied("別の発注を登録しています。完了してから再度お試しください。");

        try
        {
            using var db = new SalesManagementContext();
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = ResolveActor(db, actorId);
            if (actor is not { IsAllowedPurchaser: true })
                return Denied("発注を登録する権限がありません。管理者または物流担当としてログインしてください。");

            var maker = db.MMakers.FirstOrDefault(x => x.MaId == makerId && x.MaFlag == ActiveFlag);
            if (maker == null) return Denied("選択したメーカーは存在しないか、現在利用できません。");
            var productIds = lines.Select(x => x.ProductId).ToArray();
            var eligibleProductIds = db.MProducts.AsNoTracking()
                .Where(x => productIds.Contains(x.PrId) && x.PrFlag == ActiveFlag && x.MaId == makerId && x.Ma.MaFlag == ActiveFlag)
                .Select(x => x.PrId).ToHashSet();
            if (eligibleProductIds.Count != productIds.Length)
                return Denied("選択した商品とメーカーの組み合わせが無効です。商品を再選択してください。");

            var order = new THattyu
            {
                MaId = makerId,
                EmId = actor.EmployeeId,
                RegisteredOfficeId = actor.OfficeId,
                HaDate = orderDate!.Value.Date,
                WaWarehouseFlag = 0,
                HaFlag = ActiveFlag,
                HaHidden = null,
                THattyuDetails = lines.Select(x => new THattyuDetail
                {
                    PrId = x.ProductId,
                    HaQuantity = x.Quantity
                }).ToList()
            };
            db.THattyus.Add(order);
            db.SaveChanges();
            transaction.Commit();
            return new(true, order.HaId, $"発注 {FormatCode(order.HaId)} を登録しました。入庫・在庫数は変更していません。");
        }
        finally { Volatile.Write(ref registrationInProgress, 0); }
    }
}
