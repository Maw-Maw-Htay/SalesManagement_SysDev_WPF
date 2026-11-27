using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum WarehouseMovementKind { Inbound, Outbound }
public enum WarehouseManagementTab { All, Visible, Hidden }
public enum WarehouseBusinessStatusFilter { All, Unconfirmed, Confirmed }

public sealed record WarehouseLookupOption(int Id, string Name);
public sealed record WarehouseMovementSearchCriteria(string Keyword, DateTime? StartDate, DateTime? EndDate,
    int? PrimaryId, int? EmployeeId, WarehouseBusinessStatusFilter Status)
{
    public string ReceiptNumber { get; init; } = "";
    public string PurchaseNumber { get; init; } = "";
    public string Supplier { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public DateTime? PurchaseFrom { get; init; }
    public DateTime? PurchaseTo { get; init; }
}
public sealed record ReceiptProductSearch(int Id, string Code, string Name);
public sealed record WarehouseMovementSummary(int Total, int Unconfirmed, int Confirmed);
public sealed record WarehouseMovementDirectory(IReadOnlyList<WarehouseMovementRecord> Records,
    IReadOnlyList<WarehouseLookupOption> PrimaryOptions, IReadOnlyList<WarehouseLookupOption> Employees,
    WarehouseMovementSummary Summary);
public sealed record WarehouseMovementLineInput(int ProductId, int Quantity);
public sealed record WarehouseMovementLine(int ProductId, string ProductCode, string ProductName, int Quantity,
    int? CurrentStock)
{
    public int? SafetyStock { get; init; }
    public string SafetyStockText => SafetyStock is >= 0 ? SafetyStock.Value.ToString("N0") : "未記録";
    public string StockConditionText => new InventoryRowData(null, ProductId, ProductCode, ProductName, "", "", "", "",
        default, CurrentStock, SafetyStock, 0).ConditionLabel;
    public string QuantityText => Quantity.ToString("N0");
    public string CurrentStockText => CurrentStock?.ToString("N0") ?? "要確認";
    public string InventoryImpactText => CurrentStock.HasValue
        ? $"{CurrentStock.Value:N0} → {(long)CurrentStock.Value + Quantity:N0}"
        : "在庫情報を確認";
}
public sealed record WarehouseMovementRecord(WarehouseMovementKind Kind, int Id, int ReferenceId, int PrimaryFilterId,
    string PartnerName, string OfficeName, int? OfficeId, int? EmployeeId, string EmployeeName,
    DateTime? MovementDate, int? BusinessState, int ManagementFlag, string? HiddenReason,
    int LineCount, int Quantity, string ProductSearchText)
{
    public DateTime? PurchaseDate { get; init; }
    public IReadOnlyList<ReceiptProductSearch> ReceiptProducts { get; init; } = [];
    public int? RelatedOrderId { get; init; }
    public int? CustomerId { get; init; }
    public ArrivalRecord? RelatedArrival { get; init; }
    public OutboundArrivalStatus ArrivalStatus { get; init; }
    public string RelatedOrderCode => RelatedOrderId is int id ? $"CH-{id:000000}" : "未特定";
    public string ArrivalStatusLabel => ArrivalStatus switch
    {
        OutboundArrivalStatus.Pending => "入荷未確定",
        OutboundArrivalStatus.Completed => "入荷済み",
        OutboundArrivalStatus.Review => "要確認",
        _ => "未連携"
    };
    public string Code => Kind == WarehouseMovementKind.Inbound ? $"IN-{Id:000000}" : $"OUT-{Id:000000}";
    public string ReferenceCode => Kind == WarehouseMovementKind.Inbound
        ? $"PO-{ReferenceId:000000}" : RelatedOrderId.HasValue ? RelatedOrderCode : $"ORD-{ReferenceId:000000}";
    public string PartnerColumnHeader => Kind == WarehouseMovementKind.Inbound ? "メーカー" : "出庫先営業所";
    public string PartnerDisplay => Kind == WarehouseMovementKind.Inbound ? PartnerName : OfficeName;
    public string MovementDateLabel => Kind == WarehouseMovementKind.Inbound ? "入庫日" : "出庫日";
    public string MovementDateText => MovementDate?.ToString("yyyy/MM/dd") ?? "未設定";
    public string QuantityLabel => Kind == WarehouseMovementKind.Inbound ? "入庫数量" : "出庫数量";
    public string BusinessStatusLabel => (Kind, BusinessState) switch
    {
        (WarehouseMovementKind.Inbound, WarehousingService.ConfirmedState) => "確定済み",
        (WarehouseMovementKind.Outbound, OutboundService.ConfirmedState) => "確定済み",
        (_, WarehousingService.UnconfirmedState) => "未確定",
        _ => "要確認"
    };
    public string ManagementLabel => ManagementFlag switch
    {
        WarehousingService.ActiveFlag => "表示中",
        WarehousingService.HiddenFlag => "非表示",
        _ => "要確認"
    };
    public bool IsConfirmed => BusinessState == WarehousingService.ConfirmedState;
    public bool IsUnconfirmed => BusinessState == WarehousingService.UnconfirmedState;
}
public sealed record WarehouseMovementDetail(WarehouseMovementRecord Record,
    IReadOnlyList<WarehouseMovementLine> Lines)
{
    public PurchaseOrderDetail? RelatedPurchase { get; init; }
}
public sealed record WarehouseMovementOperationResult(bool Succeeded, string Message,
    int? RecordId = null, int? RelatedRecordId = null);
