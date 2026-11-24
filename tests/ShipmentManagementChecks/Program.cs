using SalesManagement_SysDev;

internal static class Program
{
    private static int checks;
    private static int Main()
    {
        try
        {
            CheckRecordFormattingAndFilters();
            CheckStateTransitionsAndQuantities();
            CheckSalesAndArrivalGuards();
            var product = new MProduct { PrId = 2, Price = 1200m };
            var orderLine = new TOrderDetail { Pr = product, PrId = 2, OrQuantity = 2, OrTotalPrice = 2000m };
            var detail = ShipmentOperationRules.CreateSaleDetail(new TSale(), orderLine);
            Check(detail.PrId == 2 && detail.SaQuantity == 2 && detail.SaPrTotalPrice == 2000m,
                "new sale uses the order amount after a catalogue price change");
            var legacy = new TSaleDetail { PrId = 2, SaQuantity = 2, SaPrTotalPrice = 2400m };
            Check(legacy.SaPrTotalPrice != detail.SaPrTotalPrice &&
                ShipmentOperationRules.ValidateExistingSale(true, [new(2, 2)], [new(legacy.PrId, legacy.SaQuantity)]) == null,
                "legacy sale with matching quantities can be reused despite a different recorded amount");
            CheckPermissionsAndHideReason();
            Console.WriteLine($"PASS: {checks} shipment-management checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { AppSession.Employee = null; }
    }

    private static void CheckRecordFormattingAndFilters()
    {
        var shipment = Shipment(42, ShipmentService.ActiveFlag, ShipmentService.UnconfirmedState) with
        {
            ConfirmedDate = new DateTime(2026, 10, 26, 23, 59, 0),
            ProductSearchText = "P-0012 コーヒー"
        };
        var criteria = new ShipmentSearchCriteria("P-001", new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 26), ShipmentStateFilter.Unconfirmed);
        Check(shipment.Code == "SH-000042" && shipment.OrderCode == "CH-000007", "shipment and order codes use existing identity keys");
        Check(shipment.StateLabel == "未確定" && shipment.ManagementLabel == "表示中", "shipment and management statuses remain distinct");
        Check(Shipment(1, 0, null).StateLabel == "要確認", "unknown shipment state is surfaced for review");
        Check(ShipmentFilter.Matches(shipment, criteria, ShipmentManagementTab.Visible, false), "partial product search combines with date and state filters");
        Check(ShipmentFilter.Matches(shipment, criteria with { Keyword = "8", StartDate = null, EndDate = null, State = ShipmentStateFilter.All },
            ShipmentManagementTab.Visible, false), "customer numeric code is searchable");
        Check(ShipmentFilter.Matches(shipment, criteria with { Keyword = "SH-0000" }, ShipmentManagementTab.Visible, false), "shipment code supports partial matching");
        Check(ShipmentFilter.Matches(shipment, criteria with { Keyword = "ORD-" }, ShipmentManagementTab.Visible, false) == false, "search does not match fabricated code formats");
        Check(!ShipmentFilter.Matches(shipment, criteria with { EndDate = new DateTime(2026, 10, 25) }, ShipmentManagementTab.Visible, false), "end date includes the selected calendar day only");
        Check(ShipmentFilter.Validate(criteria with { StartDate = new DateTime(2026, 10, 27) }) != null, "reversed date range is rejected");
        Check(!ShipmentFilter.Matches(shipment with { ConfirmedDate = null }, criteria with { StartDate = null }, ShipmentManagementTab.Visible, false), "unconfirmed shipment has no fabricated confirmation date");

        var hidden = shipment with { ManagementFlag = ShipmentService.HiddenFlag, State = ShipmentService.ConfirmedState };
        var noFilters = criteria with { Keyword = "", StartDate = null, EndDate = null, State = ShipmentStateFilter.All };
        Check(ShipmentFilter.Matches(hidden, noFilters, ShipmentManagementTab.Hidden, true), "administrator can filter hidden shipments separately");
        Check(!ShipmentFilter.Matches(hidden, noFilters, ShipmentManagementTab.Hidden, false), "non-administrator cannot view hidden shipments");
        Check(!ShipmentFilter.Matches(hidden, noFilters, ShipmentManagementTab.All, false), "hidden shipments are excluded for sales users");
    }

    private static void CheckStateTransitionsAndQuantities()
    {
        Check(ShipmentOperationRules.ValidateConfirmState(ShipmentService.ActiveFlag, ShipmentService.UnconfirmedState) == null, "visible unconfirmed shipment can be confirmed");
        Check(ShipmentOperationRules.ValidateConfirmState(ShipmentService.ActiveFlag, ShipmentService.ConfirmedState) != null, "confirmed shipment cannot be confirmed twice");
        Check(ShipmentOperationRules.ValidateConfirmState(ShipmentService.HiddenFlag, ShipmentService.UnconfirmedState) != null, "hidden shipment cannot be confirmed");
        Check(ShipmentOperationRules.ValidateConfirmState(0, null) != null, "unknown shipment state is rejected");
        Check(ShipmentOperationRules.ValidateHideState(ShipmentService.ActiveFlag, ShipmentService.ConfirmedState) == null, "confirmed visible shipment can be hidden");
        Check(ShipmentOperationRules.ValidateHideState(ShipmentService.ActiveFlag, ShipmentService.UnconfirmedState) != null, "unconfirmed shipment cannot be hidden");
        Check(ShipmentOperationRules.ValidateHideState(ShipmentService.HiddenFlag, ShipmentService.ConfirmedState) != null, "hidden shipment cannot be hidden twice");
        Check(ShipmentOperationRules.ValidateLines([new(2, 1), new(3, 8)]) == null, "positive shipment lines are accepted");
        Check(ShipmentOperationRules.ValidateLines([]) != null && ShipmentOperationRules.ValidateLines([new(0, 1)]) != null
            && ShipmentOperationRules.ValidateLines([new(2, 0)]) != null, "empty and invalid shipment lines are rejected");
        Check(ShipmentOperationRules.LinesMatch([new(2, 3), new(2, 2), new(3, 4)], [new(3, 4), new(2, 5)]), "quantity matching aggregates duplicate product lines");
        Check(!ShipmentOperationRules.LinesMatch([new(2, 3), new(3, 4)], [new(3, 4), new(2, 5)]), "arrival quantity differences are rejected");
    }

    private static void CheckSalesAndArrivalGuards()
    {
        Check(ShipmentOperationRules.ValidateArrivalMatchCount(1) == null, "one matching confirmed arrival is accepted");
        Check(ShipmentOperationRules.ValidateArrivalMatchCount(0) != null && ShipmentOperationRules.ValidateArrivalMatchCount(2) != null,
            "missing or ambiguous arrival match is rejected");
        Check(ShipmentOperationRules.ValidateShipmentMatchCount(1) == null && ShipmentOperationRules.ValidateShipmentMatchCount(2) != null,
            "duplicate shipment keys are rejected");
        Check(ShipmentOperationRules.ValidateExistingSaleCount(0) == null && ShipmentOperationRules.ValidateExistingSaleCount(1) == null
            && ShipmentOperationRules.ValidateExistingSaleCount(2) != null, "sale reuse is allowed only when there is at most one sale per order");
        var shipmentLines = new[] { new ShipmentQuantityInput(2, 4), new ShipmentQuantityInput(3, 1) };
        Check(ShipmentOperationRules.ValidateExistingSale(true, shipmentLines, shipmentLines) == null, "matching sale can be reused without duplication");
        Check(ShipmentOperationRules.ValidateExistingSale(false, shipmentLines, shipmentLines) != null, "sale with different customer or office is rejected");
        Check(ShipmentOperationRules.ValidateExistingSale(true, shipmentLines, [new(2, 3)]) != null, "sale quantity mismatch is rejected");
    }

    private static void CheckPermissionsAndHideReason()
    {
        AppSession.Employee = Employee("営業");
        Check(ShipmentService.CanView && ShipmentService.CanConfirm && !ShipmentService.CanHide && !ShipmentService.CanViewHidden,
            "active sales user can view and confirm but cannot hide or view hidden shipments");
        Check(!ShipmentService.Hide(10, 42, "理由").Succeeded, "unauthorized hide is denied before database access");
        AppSession.Employee = Employee("物流");
        Check(!ShipmentService.CanView && !ShipmentService.CanConfirm && !ShipmentService.CanHide,
            "logistics role is denied by the existing shipment permission matrix");
        AppSession.Employee = Employee("管理者");
        Check(ShipmentService.CanView && ShipmentService.CanConfirm && ShipmentService.CanHide && ShipmentService.CanViewHidden,
            "administrator can view, confirm, hide, and review hidden shipments");
        Check(ShipmentOperationRules.ValidateHideReason(null) != null && ShipmentOperationRules.ValidateHideReason("  ") != null,
            "empty hide reason is rejected");
        Check(ShipmentOperationRules.ValidateHideReason("誤登録のため") == null, "nonempty hide reason is accepted");
        Check(ShipmentOperationRules.ValidateHideReason(new string('理', ShipmentService.MaximumHideReasonLength)) == null
            && ShipmentOperationRules.ValidateHideReason(new string('理', ShipmentService.MaximumHideReasonLength + 1)) != null,
            "hide reason length limit is enforced");
        AppSession.Employee!.EmFlag = 2;
        Check(!ShipmentService.CanView && !ShipmentService.CanConfirm && !ShipmentService.CanHide, "inactive employee loses shipment permissions");
        AppSession.Employee = null;
        Check(!ShipmentService.CanView && !ShipmentService.CanConfirm && !ShipmentService.CanHide, "missing session denies shipment permissions");
    }

    private static ShipmentRecord Shipment(int id, int flag, int? state) =>
        new(id, 7, 8, "青空商事", 3, "東京営業所", 10, "山田", new DateTime(2026, 10, 20), state, flag, null, 2, 12, "P-0012 コーヒー");

    private static MEmployee Employee(string role) => new()
    {
        EmId = 10,
        EmName = "山田",
        SoId = 3,
        EmFlag = 0,
        Po = new MPosition { PoName = role, PoFlag = 0 }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
}
