using SalesManagement_SysDev;

internal static class Program
{
    private static int checks;

    private static int Main()
    {
        try
        {
            CheckRecordsAndSearch();
            CheckTransitionRules();
            CheckPermissionsAndReasons();
            Console.WriteLine($"PASS: {checks} arrival-management checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { AppSession.Employee = null; }
    }

    private static void CheckRecordsAndSearch()
    {
        var arrival = Arrival(42, ArrivalService.ActiveFlag, ArrivalService.UnconfirmedState) with
        {
            ArrivalDate = new DateTime(2026, 10, 26, 23, 59, 0),
            ProductSearchText = "P-0012 コーヒー"
        };
        var criteria = new ArrivalSearchCriteria("P-001", new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 26), 3, 10, ArrivalStateFilter.Unconfirmed);

        Check(arrival.Code == "AR-000042" && arrival.OrderCode == "CH-000007",
            "arrival and order codes are formatted from existing identity keys");
        Check(arrival.ArrivalStateLabel == "未確定" && arrival.ManagementLabel == "表示中",
            "known business and management states have distinct labels");
        Check(Arrival(1, 0, null).ArrivalStateLabel == "要確認",
            "unknown or missing business state is surfaced for review");
        Check(ArrivalFilter.Matches(arrival, criteria, ArrivalManagementTab.Visible, false),
            "partial product search combines with date, office, employee, and state filters");
        Check(ArrivalFilter.Matches(arrival, criteria with { Keyword = "AR-0000" }, ArrivalManagementTab.Visible, false),
            "arrival number supports partial matching");
        Check(ArrivalFilter.Matches(arrival, criteria with { Keyword = "注文" }, ArrivalManagementTab.Visible, false) == false,
            "search does not match text that is not part of the record");
        Check(!ArrivalFilter.Matches(arrival, criteria with { EndDate = new DateTime(2026, 10, 25) },
            ArrivalManagementTab.Visible, false), "end-date filtering excludes later calendar dates");
        Check(ArrivalFilter.Validate(criteria with { StartDate = new DateTime(2026, 10, 27) }) != null,
            "reversed date ranges are rejected");
        Check(!ArrivalFilter.Matches(arrival with { ArrivalDate = null }, criteria with { StartDate = null },
            ArrivalManagementTab.Visible, false), "records without a date do not satisfy a date filter");

        var hidden = arrival with { ManagementFlag = ArrivalService.HiddenFlag, ArrivalState = ArrivalService.ConfirmedState };
        Check(ArrivalFilter.Matches(hidden, criteria with { Keyword = "", State = ArrivalStateFilter.All },
            ArrivalManagementTab.Hidden, true), "administrator can filter hidden arrivals separately");
        Check(!ArrivalFilter.Matches(hidden, criteria with { Keyword = "", State = ArrivalStateFilter.All },
            ArrivalManagementTab.Hidden, false), "non-administrator cannot query the hidden tab");
        Check(!ArrivalFilter.Matches(hidden, criteria with { Keyword = "", State = ArrivalStateFilter.All },
            ArrivalManagementTab.All, false), "hidden arrivals are excluded from non-administrator results");
        Check(!ArrivalFilter.Matches(arrival with { ManagementFlag = 9 }, criteria with { Keyword = "" },
            ArrivalManagementTab.Visible, false), "unknown management state is excluded from visible results");
    }

    private static void CheckTransitionRules()
    {
        Check(ArrivalOperationRules.ValidateConfirmState(ArrivalService.ActiveFlag,
            ArrivalService.UnconfirmedState, false) == null, "visible unconfirmed arrival can be confirmed");
        Check(ArrivalOperationRules.ValidateConfirmState(ArrivalService.ActiveFlag,
            ArrivalService.ConfirmedState, false) != null, "confirmed arrival cannot create a duplicate shipment");
        Check(ArrivalOperationRules.ValidateConfirmState(ArrivalService.ActiveFlag, null, true) != null,
            "unknown arrival state cannot be confirmed");
        Check(ArrivalOperationRules.ValidateConfirmState(9, ArrivalService.UnconfirmedState, true) != null,
            "unknown management state cannot be confirmed");
        Check(ArrivalOperationRules.ValidateConfirmState(ArrivalService.HiddenFlag,
            ArrivalService.UnconfirmedState, false) != null, "sales cannot confirm a hidden arrival");

        Check(ArrivalOperationRules.ValidateHideState(ArrivalService.ActiveFlag,
            ArrivalService.ConfirmedState) == null, "confirmed visible arrival can be hidden");
        Check(ArrivalOperationRules.ValidateHideState(ArrivalService.ActiveFlag,
            ArrivalService.UnconfirmedState) != null, "unconfirmed arrival cannot be hidden");
        Check(ArrivalOperationRules.ValidateHideState(ArrivalService.HiddenFlag,
            ArrivalService.ConfirmedState) != null, "already hidden arrival cannot be hidden again");
        Check(ArrivalOperationRules.ValidateHideState(9, ArrivalService.ConfirmedState) != null,
            "unknown management state cannot be hidden");

        Check(ArrivalOperationRules.ValidateLines([new(2, 1), new(3, 8)]) == null,
            "positive arrival quantities and product identifiers are accepted");
        Check(ArrivalOperationRules.ValidateLines([]) != null, "confirmation requires at least one detail line");
        Check(ArrivalOperationRules.ValidateLines([new(null, 1)]) != null, "missing product identifier is rejected");
        Check(ArrivalOperationRules.ValidateLines([new(2, null)]) != null, "missing quantity is rejected");
        Check(ArrivalOperationRules.ValidateLines([new(2, 0)]) != null, "zero quantity is rejected");
        Check(ArrivalOperationRules.ValidateLines([new(2, -1)]) != null, "negative quantity is rejected");
        Check(ArrivalOperationRules.LinesMatch([new(2, 3), new(2, 2), new(3, 4)], [new(3, 4), new(2, 5)]),
            "outbound and arrival details match after aggregating product quantities");
        Check(!ArrivalOperationRules.LinesMatch([new(2, 3), new(3, 4)], [new(3, 4), new(2, 5)]),
            "outbound quantity mismatch is rejected");
        Check(!ArrivalOperationRules.LinesMatch([new(2, 3)], [new(2, 3), new(3, 1)]),
            "outbound product mismatch is rejected");
        Check(ArrivalOperationRules.ValidateOutboundMatchCount(1) == null,
            "a unique confirmed outbound with the same source keys and lines is accepted");
        Check(ArrivalOperationRules.ValidateOutboundMatchCount(0) != null,
            "arrival without a matching confirmed outbound is rejected");
        Check(ArrivalOperationRules.ValidateOutboundMatchCount(2) != null,
            "ambiguous outbound candidates are rejected");
    }

    private static void CheckPermissionsAndReasons()
    {
        AppSession.Employee = Employee("営業");
        Check(ArrivalService.CanView && ArrivalService.CanConfirm, "active sales user can view and confirm arrivals");
        Check(!ArrivalService.CanHide && !ArrivalService.CanViewHidden, "sales cannot hide or view hidden arrivals");
        Check(!ArrivalService.Hide(10, 42, "理由").Succeeded, "unauthorized hide is denied before database access");

        AppSession.Employee = Employee("物流");
        Check(!ArrivalService.CanView && !ArrivalService.CanConfirm && !ArrivalService.CanHide,
            "logistics role is denied arrival management");
        Check(!ArrivalService.Confirm(10, 42).Succeeded,
            "unauthorized logistics confirmation is denied before database access");

        AppSession.Employee = Employee("管理者");
        Check(ArrivalService.CanView && ArrivalService.CanConfirm && ArrivalService.CanHide
            && ArrivalService.CanViewHidden, "administrator can view, confirm, hide, and review hidden arrivals");
        Check(ArrivalService.ValidateHideReason(null) != null
            && ArrivalService.ValidateHideReason("   ") != null, "empty and whitespace-only hide reasons are rejected");
        Check(ArrivalService.ValidateHideReason("棚卸し後に重複と判明") == null, "nonempty hide reason is accepted");
        Check(ArrivalService.ValidateHideReason(new string('理', ArrivalService.MaximumHideReasonLength)) == null,
            "hide reason at the storage limit is accepted");
        Check(ArrivalService.ValidateHideReason(new string('理', ArrivalService.MaximumHideReasonLength + 1)) != null,
            "hide reason over the storage limit is rejected");
        Check(!ArrivalService.Hide(10, 42, " ").Succeeded,
            "administrator hide with missing reason is denied before database access");

        AppSession.Employee!.EmFlag = 2;
        Check(!ArrivalService.CanView && !ArrivalService.CanConfirm && !ArrivalService.CanHide,
            "inactive employee loses arrival permissions");
        AppSession.Employee = null;
        Check(!ArrivalService.CanView && !ArrivalService.CanConfirm && !ArrivalService.CanHide,
            "missing session denies arrival permissions");
    }

    private static ArrivalRecord Arrival(int id, int flag, int? state) =>
        new(id, 7, 8, "青空商事", 3, "東京営業所", 10, "山田", new DateTime(2026, 10, 20),
            state, flag, null, 2, 12, "P-0012 コーヒー");

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
