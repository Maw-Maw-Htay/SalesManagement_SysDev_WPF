using System.IO;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static void CheckReceivingWrites()
    {
        var database = "ReceivingChecks_" + Guid.NewGuid().ToString("N");
        var output = Path.GetFullPath(AppContext.BaseDirectory);
        var dataPath = Path.Combine(output, database + ".mdf").Replace("'", "''");
        var logPath = Path.Combine(output, database + "_log.ldf").Replace("'", "''");
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = "master", IntegratedSecurity = true,
            TrustServerCertificate = true, Pooling = false
        };
        using var master = new SqlConnection(settings.ConnectionString);
        master.Open();
        Sql(master, $"CREATE DATABASE [{database}] ON (NAME=N'{database}', FILENAME=N'{dataPath}') LOG ON (NAME=N'{database}_log', FILENAME=N'{logPath}')");
        try
        {
            settings.InitialCatalog = database;
            var fixtureString = settings.ConnectionString;
            using var fixture = new SqlConnection(fixtureString);
            fixture.Open();
            RequireReceivingFixture(fixture, database);
            Sql(fixture, """
                CREATE TABLE dbo.M_Position (PoID int PRIMARY KEY, PoName nvarchar(50) NOT NULL, PoFlag int NOT NULL);
                CREATE TABLE dbo.M_Employee (EmID int PRIMARY KEY, PoID int NOT NULL, SoID int NOT NULL, EmFlag int NOT NULL, EmName nvarchar(50) NOT NULL);
                CREATE TABLE dbo.M_SalesOffice (SoID int PRIMARY KEY, SoName nvarchar(50) NOT NULL, SoPhone nvarchar(13) NOT NULL);
                CREATE TABLE dbo.M_Maker (MaID int PRIMARY KEY, MaName nvarchar(50) NOT NULL, MaAddress nvarchar(50) NOT NULL,
                    MaPhone nvarchar(13) NOT NULL, MaPostal nvarchar(7) NOT NULL, MaFAX nvarchar(13) NOT NULL,
                    MaFlag int NOT NULL, MaHidden nvarchar(max) NULL);
                CREATE TABLE dbo.M_MajorClassification (McID int PRIMARY KEY, McName nvarchar(50) NOT NULL);
                CREATE TABLE dbo.M_SmallClassification (ScID int PRIMARY KEY, McID int NOT NULL, ScName nvarchar(50) NOT NULL);
                CREATE TABLE dbo.M_Product (PrID int PRIMARY KEY, MaID int NOT NULL, ScID int NOT NULL, PrName nvarchar(50) NOT NULL,
                    Price decimal(9,0) NOT NULL, PrJCode nvarchar(13) NULL, PrSafetyStock int NOT NULL,
                    PrModelNumber nvarchar(20) NOT NULL, PrColor nvarchar(20) NOT NULL, PrReleaseDate date NOT NULL,
                    PrFlag int NOT NULL, PrHidden nvarchar(max) NULL,
                    CONSTRAINT FK_TestProduct_Maker FOREIGN KEY (MaID) REFERENCES dbo.M_Maker(MaID));
                CREATE TABLE dbo.T_Stock (StID int IDENTITY PRIMARY KEY, PrID int NOT NULL REFERENCES dbo.M_Product(PrID),
                    StQuantity int NOT NULL, StFlag int NOT NULL);
                CREATE INDEX IX_T_Stock_PrID ON dbo.T_Stock (PrID);
                CREATE TABLE dbo.T_Hattyu (HaID int IDENTITY PRIMARY KEY, MaID int NOT NULL, EmID int NOT NULL, HaDate date NOT NULL,
                    WaWarehouseFlag int NULL, HaFlag int NOT NULL, HaHidden nvarchar(max) NULL,
                    CONSTRAINT FK_TestPurchase_Maker FOREIGN KEY (MaID) REFERENCES dbo.M_Maker(MaID));
                CREATE TABLE dbo.T_HattyuDetail (HaDetailID int IDENTITY PRIMARY KEY, HaID int NOT NULL REFERENCES dbo.T_Hattyu(HaID),
                    PrID int NOT NULL, HaQuantity int NOT NULL,
                    CONSTRAINT FK_TestPurchaseLine_Product FOREIGN KEY (PrID) REFERENCES dbo.M_Product(PrID));
                CREATE TABLE dbo.T_Warehousing (WaID int IDENTITY PRIMARY KEY, HaID int NOT NULL REFERENCES dbo.T_Hattyu(HaID),
                    EmID int NOT NULL, WaDate date NOT NULL, WaShelfFlag int NULL, WaFlag int NOT NULL, WaHidden nvarchar(max) NULL);
                CREATE INDEX IX_T_Warehousing_HaID ON dbo.T_Warehousing (HaID);
                CREATE TABLE dbo.T_WarehousingDetail (WaDetailID int IDENTITY PRIMARY KEY, WaID int NOT NULL REFERENCES dbo.T_Warehousing(WaID),
                    PrID int NOT NULL, WaQuantity int NOT NULL,
                    CONSTRAINT FK_TestReceiptLine_Product FOREIGN KEY (PrID) REFERENCES dbo.M_Product(PrID));
                INSERT dbo.M_Position VALUES (1,N'管理者',0);
                INSERT dbo.M_Employee VALUES (1,1,4,0,N'試験担当者');
                INSERT dbo.M_SalesOffice VALUES (4,N'試験営業所','000-0000');
                INSERT dbo.M_Maker VALUES (1,N'試験メーカー',N'試験住所','000-0000','0000000','000-0000',0,NULL);
                INSERT dbo.M_MajorClassification VALUES (1,N'試験大分類');
                INSERT dbo.M_SmallClassification VALUES (1,1,N'試験小分類');
                INSERT dbo.M_Product VALUES (1,1,1,N'試験商品',10,NULL,5,'A',N'黒','20251001',0,NULL),
                    (2,1,1,N'別の試験商品',20,NULL,5,'B',N'白','20251001',0,NULL),
                    (3,1,1,N'在庫未登録商品',20,NULL,5,'C',N'白','20251001',0,NULL),
                    (4,1,1,N'ロールバック試験商品',20,NULL,5,'D',N'白','20251001',0,NULL),
                    (5,1,1,N'在庫重複試験商品',20,NULL,5,'E',N'白','20251001',0,NULL);
                INSERT dbo.T_Stock (PrID,StQuantity,StFlag) VALUES (1,12,0),(5,1,0),(5,2,0);
                SET IDENTITY_INSERT dbo.T_Hattyu ON;
                INSERT dbo.T_Hattyu (HaID, MaID, EmID, HaDate, WaWarehouseFlag, HaFlag, HaHidden)
                    VALUES (101,1,1,'20251001',NULL,0,NULL);
                SET IDENTITY_INSERT dbo.T_Hattyu OFF;
                SET IDENTITY_INSERT dbo.T_HattyuDetail ON;
                INSERT dbo.T_HattyuDetail (HaDetailID, HaID, PrID, HaQuantity) VALUES (1,101,1,16),(2,101,2,4);
                SET IDENTITY_INSERT dbo.T_HattyuDetail OFF;
                SET IDENTITY_INSERT dbo.T_Warehousing ON;
                INSERT dbo.T_Warehousing (WaID, HaID, EmID, WaDate, WaShelfFlag, WaFlag, WaHidden)
                    VALUES (1,101,1,'20251002',0,0,NULL),(2,101,1,'20251003',0,0,NULL),
                    (3,101,1,'20251004',0,2,NULL),(4,101,1,'20251005',1,2,NULL);
                SET IDENTITY_INSERT dbo.T_Warehousing OFF;
                SET IDENTITY_INSERT dbo.T_WarehousingDetail ON;
                INSERT dbo.T_WarehousingDetail (WaDetailID, WaID, PrID, WaQuantity)
                    VALUES (1,1,1,2),(2,1,1,3),(3,1,2,4),(4,2,1,6);
                SET IDENTITY_INSERT dbo.T_WarehousingDetail OFF;
                """);

            AppSession.Employee = Employee("管理者");
            AppSession.Employee.EmId = 1;
            AppSession.Employee.SoId = 4;
            WarehouseMovementOperationResult Invoke(string operation, params object?[] args)
            {
                using var db = new SalesManagementContext();
                db.Database.SetConnectionString(fixtureString);
                db.Database.OpenConnection();
                RequireReceivingFixture((SqlConnection)db.Database.GetDbConnection(), database);
                var method = typeof(WarehousingService).GetMethod(operation, BindingFlags.NonPublic | BindingFlags.Static)!;
                return (WarehouseMovementOperationResult)method.Invoke(null, [db, .. args])!;
            }
            int Number(string query) => ReadNumber(fixture, query);
            int Stock() => Number("SELECT StQuantity FROM dbo.T_Stock WHERE PrID=1");
            WarehouseMovementOperationResult Confirm(int id, params WarehouseMovementLineInput[] lines) =>
                Invoke("ConfirmShelfInContext", 1, id, lines);
            var stale = Confirm(1, new(1, 4), new(1, 3), new(2, 2));
            Check(!stale.Succeeded && stale.Message.Contains("明細が更新") && Stock() == 12
                && Number("SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID=1") == 0,
                "changed receipt quantities are rejected before any stock changes");
            var result = Confirm(1, new(1, 2), new(1, 3), new(2, 4));
            Check(result.Succeeded && Stock() == 17 && Number("SELECT StQuantity FROM dbo.T_Stock WHERE PrID=2") == 4
                && Number("SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID=1") == 1,
                $"receipt confirmation increments each product once and creates only the missing balance: {result.Message}");
            Check(!Confirm(1, new(1, 2), new(1, 3), new(2, 4)).Succeeded && Stock() == 17,
                "repeating the same confirmation cannot increase stock again");
            var concurrent = Task.WhenAll(Task.Run(() => Confirm(2, new WarehouseMovementLineInput(1, 6))),
                Task.Run(() => Confirm(2, new WarehouseMovementLineInput(1, 6)))).GetAwaiter().GetResult();
            Check(concurrent.Count(x => x.Succeeded) == 1 && Stock() == 23, "two competing confirmations result in a single stock increase");
            Check(Number("SELECT COUNT(*) FROM dbo.T_Warehousing") == 4 && Number("SELECT COUNT(*) FROM dbo.T_Hattyu") == 1,
                "confirmation does not create another receipt or purchase");

            var hide = Invoke("HideInContext", 1, 1, "試験の非表示");
            Check(hide.Succeeded && Stock() == 23 && Number("SELECT WaFlag FROM dbo.T_Warehousing WHERE WaID=1") == 2,
                "hide changes visibility without touching stock");
            var hidden = Receipt(1, 1, 2, "試験の非表示");
            var restore = Invoke("RestoreInContext", 1, hidden);
            Check(restore.Succeeded && Stock() == 23 && Number("SELECT WaFlag FROM dbo.T_Warehousing WHERE WaID=1") == 0
                && Number("SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID=1") == 1
                && Number("SELECT HaID FROM dbo.T_Warehousing WHERE WaID=1") == 101
                && Number("SELECT COUNT(*) FROM dbo.T_Warehousing WHERE WaID=1 AND WaHidden=N'試験の非表示'") == 1,
                "restore changes only the visibility flag, preserving state, relation, reason and stock");
            Check(!Invoke("RestoreInContext", 1, hidden).Succeeded && !Invoke("RestoreInContext", 1, Receipt(4, 1, 2)).Succeeded
                && !Invoke("RestoreInContext", 1, Receipt(3, 0, 2)).Succeeded && Stock() == 23,
                "repeat, legacy reasonless and unconfirmed restore attempts are rejected");
            Sql(fixture, "UPDATE dbo.M_Employee SET EmFlag=2 WHERE EmID=1");
            Check(!Invoke("HideInContext", 1, 1, "無効なセッション").Succeeded && Stock() == 23,
                "mutation rechecks the current actor record");
            Sql(fixture, "UPDATE dbo.M_Employee SET EmFlag=0 WHERE EmID=1");
            CheckPurchaseReceivingFlow(fixture, fixtureString, database);
            CheckReceivingMasterValidation(fixture, database, id => Invoke("ConfirmShelfInContext", 1, id, null));
        }
        finally
        {
            AppSession.Employee = null;
            Sql(master, $"DROP DATABASE [{database}]");
        }
    }

    private static void CheckReceivingMasterValidation(SqlConnection fixture, string database,
        Func<int, WarehouseMovementOperationResult> confirm)
    {
        RequireReceivingFixture(fixture, database);

        int AddReceipt(int supplierId, params int[] productIds)
        {
            var values = string.Join(",", productIds.Select(id => $"({id})"));
            return ReadNumber(fixture, $"""
                SET NOCOUNT ON;
                DECLARE @products TABLE (PrID int);
                INSERT @products VALUES {values};
                INSERT dbo.T_Hattyu (MaID, EmID, HaDate, HaFlag) VALUES ({supplierId}, 1, '20251001', 0);
                DECLARE @purchaseId int = SCOPE_IDENTITY();
                INSERT dbo.T_HattyuDetail (HaID, PrID, HaQuantity) SELECT @purchaseId, PrID, 9 FROM @products;
                INSERT dbo.T_Warehousing (HaID, EmID, WaDate, WaShelfFlag, WaFlag) VALUES (@purchaseId, 1, '20251002', 0, 0);
                DECLARE @receiptId int = SCOPE_IDENTITY();
                INSERT dbo.T_WarehousingDetail (WaID, PrID, WaQuantity) SELECT @receiptId, PrID, 9 FROM @products;
                SELECT @receiptId;
                """);
        }

        void CheckRejected(int receiptId, string description, params string[] messageParts)
        {
            const string stockQuantity = "SELECT SUM(StQuantity) FROM dbo.T_Stock";
            const string stockRows = "SELECT COUNT(*) FROM dbo.T_Stock";
            var beforeQuantity = ReadNumber(fixture, stockQuantity);
            var beforeRows = ReadNumber(fixture, stockRows);
            var result = confirm(receiptId);
            Check(!result.Succeeded && messageParts.All(part => result.Message.Contains(part, StringComparison.Ordinal))
                && ReadNumber(fixture, $"SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID={receiptId}") == 0
                && ReadNumber(fixture, stockQuantity) == beforeQuantity && ReadNumber(fixture, stockRows) == beforeRows,
                $"{description} leaves receipt and stock unchanged ({result.Message})");
        }

        Sql(fixture, """
            INSERT dbo.M_Maker VALUES (2,N'別メーカー',N'試験住所','000-0000','0000000','000-0000',0,NULL),
                (3,N'無効メーカー',N'試験住所','000-0000','0000000','000-0000',2,NULL);
            UPDATE dbo.M_Product SET MaID=2 WHERE PrID=4;
            """);
        CheckRejected(AddReceipt(1, 1, 4), "manufacturer mismatch on a later product",
            "P-0004", "別メーカー（ID 2）", "試験メーカー（ID 1）", "一致しません");
        Sql(fixture, "UPDATE dbo.M_Product SET PrFlag=2 WHERE PrID=2");
        CheckRejected(AddReceipt(1, 2), "inactive product", "P-0002", "商品マスタの状態");
        Sql(fixture, "UPDATE dbo.M_Product SET MaID=3 WHERE PrID=4");
        CheckRejected(AddReceipt(3, 4), "inactive manufacturer", "P-0004", "無効メーカー", "有効ではありません");

        // Simulate corrupt legacy keys in the disposable database only.
        Sql(fixture, """
            ALTER TABLE dbo.T_HattyuDetail NOCHECK CONSTRAINT FK_TestPurchaseLine_Product;
            ALTER TABLE dbo.T_WarehousingDetail NOCHECK CONSTRAINT FK_TestReceiptLine_Product;
            """);
        CheckRejected(AddReceipt(1, 999), "broken product references", "P-0999", "商品マスタに見つかりません");
        Sql(fixture, """
            ALTER TABLE dbo.M_Product NOCHECK CONSTRAINT FK_TestProduct_Maker;
            UPDATE dbo.M_Product SET MaID=999 WHERE PrID=4;
            """);
        CheckRejected(AddReceipt(1, 4), "missing required product manufacturer", "P-0004", "メーカー（ID 999）");
        Sql(fixture, "ALTER TABLE dbo.T_Hattyu NOCHECK CONSTRAINT FK_TestPurchase_Maker");
        CheckRejected(AddReceipt(999, 1), "broken purchase supplier reference", "P-0001", "仕入先（メーカーID 999）");
    }

    private static void CheckPurchaseReceivingFlow(SqlConnection fixture, string connectionString, string database)
    {
        const string connectionSetting = "SALESMANAGEMENT_CONNECTION_STRING";
        var previousConnection = Environment.GetEnvironmentVariable(connectionSetting);
        Environment.SetEnvironmentVariable(connectionSetting, connectionString);
        try
        {
            using (var db = new SalesManagementContext())
            {
                db.Database.OpenConnection();
                RequireReceivingFixture((SqlConnection)db.Database.GetDbConnection(), database);
            }

            var orderDate = new DateTime(2025, 10, 1);
            var receivedOn = orderDate.AddDays(1);
            int Register(params PurchaseLineInput[] lines)
            {
                var result = PurchaseOrderService.Register(1, 1, orderDate, lines);
                Check(result.Succeeded && result.OrderId.HasValue, $"purchase registration succeeds: {result.Message}");
                return result.OrderId!.Value;
            }
            int Number(string query) => ReadNumber(fixture, query);
            int Stock(int product) => Number($"SELECT StQuantity FROM dbo.T_Stock WHERE PrID={product} AND StFlag=0");
            int StockRows(int product) => Number($"SELECT COUNT(*) FROM dbo.T_Stock WHERE PrID={product}");
            int ReceiptCount(int order) => Number($"SELECT COUNT(*) FROM dbo.T_Warehousing WHERE HaID={order}");
            WarehouseMovementOperationResult Receive(int order, params WarehouseMovementLineInput[] actual) =>
                WarehousingService.ConfirmPurchaseReceipt(1, order, receivedOn, actual);

            Sql(fixture, """
                INSERT dbo.M_Maker VALUES (6,N'別の試験仕入先',N'試験住所','000-0000','0000000','000-0000',0,NULL),
                    (7,N'商品なしの試験仕入先',N'試験住所','000-0000','0000000','000-0000',0,NULL);
                INSERT dbo.M_Product VALUES (6,6,1,N'在庫ゼロ商品',20,NULL,5,'F',N'白','20251001',0,NULL),
                    (7,6,1,N'非表示の商品',20,NULL,5,'G',N'白','20251001',2,NULL);
                INSERT dbo.T_Stock (PrID,StQuantity,StFlag) VALUES (6,0,0);
                """);
            var catalog = PurchaseOrderService.LoadRegistrationCatalog();
            Check(catalog.Products.Single(product => product.Id == 6) is
                { MakerId: 6, StockQuantity: 0, StockState: InventoryStockState.Available }
                && catalog.Products.Single(product => product.Id == 3) is
                { StockQuantity: null, StockState: InventoryStockState.MissingRecord },
                "registration catalog distinguishes verified zero from an unknown balance");
            Check(catalog.Makers.Any(maker => maker.Id == 7)
                && catalog.Products.All(product => product.MakerId != 7 && product.Id != 7),
                "the active supplier without products is selectable and hidden products are excluded");
            var ordersBefore = Number("SELECT COUNT(*) FROM dbo.T_Hattyu");
            var receiptsBefore = Number("SELECT COUNT(*) FROM dbo.T_Warehousing");
            var mismatch = PurchaseOrderService.Register(1, 1, orderDate, [new(6, 2)]);
            Check(!mismatch.Succeeded && mismatch.Message == "選択した仕入先と商品のメーカーが一致しません。商品または仕入先を確認してください。"
                && Number("SELECT COUNT(*) FROM dbo.T_Hattyu") == ordersBefore && Stock(6) == 0,
                "registration rejects a real manufacturer mismatch before saving an order");
            Check(!PurchaseOrderService.Register(1, 6, orderDate, []).Succeeded
                && Number("SELECT COUNT(*) FROM dbo.T_Hattyu") == ordersBefore,
                "registration rejects an empty order before creating a purchase");
            var zeroStockOrder = PurchaseOrderService.Register(1, 6, orderDate, [new(6, 2)]);
            Check(zeroStockOrder.Succeeded && zeroStockOrder.OrderId is int zeroOrderId
                && Stock(6) == 0 && ReceiptCount(zeroOrderId) == 0
                && Number("SELECT COUNT(*) FROM dbo.T_Warehousing") == receiptsBefore,
                "purchasing a matching product with zero stock creates only a purchase order");

            var orderId = Register(new(1, 20), new(3, 8));
            Check(Stock(1) == 23 && StockRows(3) == 0 && ReceiptCount(orderId) == 0
                && Number($"SELECT WaWarehouseFlag FROM dbo.T_Hattyu WHERE HaID={orderId}") == PurchaseOrderService.RegisteredState,
                "purchase registration records the draft and leaves receipts and stock untouched");
            Check(WarehousingService.Load().Records.All(row => !row.IsAwaitingReceipt)
                && !Receive(orderId, new(1, 20), new(3, 8)).Succeeded, "draft and legacy purchase orders are excluded from receiving");
            Check(!PurchaseOrderService.Confirm(2, orderId).Succeeded, "purchase confirmation requires the logged-in actor");

            var confirmations = Task.WhenAll(Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() => PurchaseOrderService.Confirm(1, orderId)))).GetAwaiter().GetResult();
            Check(confirmations.Count(result => result.Succeeded) == 1 && Stock(1) == 23 && StockRows(3) == 0
                && ReceiptCount(orderId) == 0
                && Number($"SELECT WaWarehouseFlag FROM dbo.T_Hattyu WHERE HaID={orderId}") == PurchaseOrderService.ConfirmedState,
                "competing purchase confirmations release exactly one order and leave receipts and stock untouched");
            var awaiting = WarehousingService.Load().Records.Single(row => row.IsAwaitingReceipt);
            Check(awaiting.Id == 0 && awaiting.ReferenceId == orderId && awaiting.PartnerName == "試験メーカー"
                && awaiting.ReceiptProducts.Select(product => product.Id).SequenceEqual([1, 3])
                && awaiting.OrderedQuantity == 28 && awaiting.MovementDate == null && awaiting.BusinessStatusLabel == "入庫待ち",
                "receiving lists the real supplier, products and ordered quantity before any receipt exists");
            var detailsMethod = typeof(WarehousingService).GetMethod("LoadAwaitingReceipt", BindingFlags.NonPublic | BindingFlags.Static)!;
            var awaitingDetail = (WarehouseMovementDetail)detailsMethod.Invoke(null, [orderId])!;
            Check(awaitingDetail.RelatedPurchase?.Order.Id == orderId && awaitingDetail.Lines.All(line => line.IsAwaitingQuantity),
                "receiving detail links to the purchase and leaves actual quantities blank");
            Check(!Receive(orderId, new(1, 19), new(3, 8)).Succeeded
                && Stock(1) == 23 && StockRows(3) == 0 && ReceiptCount(orderId) == 0,
                "a quantity mismatch is refused before a receipt or missing balance can be written");
            Check(!WarehousingService.ConfirmPurchaseReceipt(2, orderId, receivedOn, [new(1, 20), new(3, 8)]).Succeeded,
                "receipt confirmation requires the logged-in actor");

            var concurrentReceipts = Task.WhenAll(Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() => Receive(orderId, new(1, 20), new(3, 8))))).GetAwaiter().GetResult();
            Check(concurrentReceipts.Count(result => result.Succeeded) == 1, "competing receipt confirmations post once");
            var posted = concurrentReceipts.Single(result => result.Succeeded);
            Check(posted.RecordId is > 0 && posted.RelatedRecordId == orderId && ReceiptCount(orderId) == 1
                && Stock(1) == 43 && Stock(2) == 4 && Stock(3) == 8 && StockRows(3) == 1,
                "verified receipt increments each matching PrID and initializes the missing balance");
            var receiptId = posted.RecordId!.Value;
            var title = WarehousingService.Load().Records.Single(row => row.ReferenceId == orderId);
            var receipt = WarehousingService.LoadDetail(receiptId);
            Check(title.IsConfirmed && !title.IsAwaitingReceipt && title.OrderedQuantity == 28 && title.Quantity == 28
                && receipt?.Record.EmployeeId == 1 && receipt.Record.MovementDate == receivedOn
                && receipt.RelatedPurchase?.Order.IsFullyReceived == true
                && receipt.Lines.Select(line => (line.ProductId, line.Quantity)).ToHashSet().SetEquals([(1, 20), (3, 8)]),
                "stored receipt preserves the purchase, operator, date and actual quantities and replaces the waiting row");
            Check(!new WarehouseMovementDetailViewModel(receipt!).CanConfirm && !Receive(orderId, new(1, 20), new(3, 8)).Succeeded
                && Stock(1) == 43 && ReceiptCount(orderId) == 1, "confirmed receipts are read-only and cannot repost");
            var filters = new InventoryFilters("", "", "", "", "", null, null, null, InventorySort.Code, false);
            var inventory = InventoryService.LoadPageAsync(filters, 1, 20, true, CancellationToken.None).GetAwaiter().GetResult();
            Check(inventory.Rows.Single(row => row.ProductId == 1).Quantity == 43
                && inventory.Rows.Single(row => row.ProductId == 3).Quantity == 8,
                "reloading inventory reads the committed balances");

            var duplicateOrder = Register(new(1, 3), new(4, 2), new(5, 1));
            Check(PurchaseOrderService.Confirm(1, duplicateOrder).Succeeded, "purchase with ambiguous stock can be reviewed in receiving");
            var duplicate = Receive(duplicateOrder, new(1, 3), new(4, 2), new(5, 1));
            Check(!duplicate.Succeeded && duplicate.Message.Contains("重複") && Stock(1) == 43 && StockRows(4) == 0
                && StockRows(5) == 2 && ReceiptCount(duplicateOrder) == 0, "duplicate active balances roll back all earlier stock changes");

            var failingOrder = Register(new(1, 2), new(4, 6));
            Check(PurchaseOrderService.Confirm(1, failingOrder).Succeeded, "purchase for late SQL failure was released");
            Sql(fixture, $"ALTER TABLE dbo.T_Warehousing ADD CONSTRAINT CK_ReceivingFixture_RejectReceipt CHECK (HaID <> {failingOrder})");
            try
            {
                var failure = Receive(failingOrder, new(1, 2), new(4, 6));
                Check(!failure.Succeeded && Stock(1) == 43 && StockRows(4) == 0 && ReceiptCount(failingOrder) == 0,
                    "a failed receipt insert rolls back balance updates and initialization");
            }
            finally { Sql(fixture, "ALTER TABLE dbo.T_Warehousing DROP CONSTRAINT CK_ReceivingFixture_RejectReceipt"); }
            Check(Receive(failingOrder, new(1, 2), new(4, 6)).Succeeded && Stock(1) == 45 && Stock(4) == 6
                && ReceiptCount(failingOrder) == 1, "retry after a rolled-back transaction posts a single linked receipt");

            var legacyOrder = zeroStockOrder.OrderId!.Value;
            Sql(fixture, $"UPDATE dbo.T_Hattyu SET WaWarehouseFlag=0 WHERE HaID={legacyOrder}");
            var oldOrder = PurchaseOrderService.Load().Orders.Single(order => order.Id == legacyOrder);
            Check(oldOrder.ProgressLabel == "発注未確定" && oldOrder.IsAwaitingConfirmation
                && WarehousingService.Load().Records.All(row => row.ReferenceId != legacyOrder)
                && !Receive(legacyOrder, new WarehouseMovementLineInput(6, 2)).Succeeded && Stock(6) == 0,
                "an old registration appears as unconfirmed and cannot post receipt stock before purchase confirmation");
            Check(PurchaseOrderService.Confirm(1, legacyOrder).Succeeded && !PurchaseOrderService.Confirm(1, legacyOrder).Succeeded
                && PurchaseOrderService.LoadDetail(legacyOrder)?.Order.ProgressLabel == "入庫待ち"
                && WarehousingService.Load().Records.Count(row => row.ReferenceId == legacyOrder && row.IsAwaitingReceipt) == 1
                && Stock(6) == 0 && ReceiptCount(legacyOrder) == 0,
                "explicit confirmation releases one legacy order without adding stock or a receipt");
            Check(Receive(legacyOrder, new WarehouseMovementLineInput(6, 2)).Succeeded
                && !Receive(legacyOrder, new WarehouseMovementLineInput(6, 2)).Succeeded
                && Stock(6) == 2 && ReceiptCount(legacyOrder) == 1,
                "the released legacy order posts actual stock exactly once on receipt confirmation");
            Sql(fixture, $"UPDATE dbo.T_Warehousing SET WaFlag=2 WHERE HaID={legacyOrder}; UPDATE dbo.T_Hattyu SET WaWarehouseFlag=0 WHERE HaID={legacyOrder}");
            Check(PurchaseOrderService.LoadDetail(legacyOrder)?.Order is { HasRelatedReceipt: true, IsAwaitingConfirmation: false }
                && !PurchaseOrderService.Confirm(1, legacyOrder).Succeeded && Stock(6) == 2 && ReceiptCount(legacyOrder) == 1,
                "a hidden linked receipt also prevents re-release of an old purchase");

            var invalidLegacyOrder = Register(new PurchaseLineInput(1, 2));
            Sql(fixture, $"UPDATE dbo.T_Hattyu SET WaWarehouseFlag=0, MaID=6 WHERE HaID={invalidLegacyOrder}");
            Check(PurchaseOrderService.Confirm(1, invalidLegacyOrder) is { Succeeded: false } rejected
                && rejected.Message.Contains("メーカーが一致しません")
                && Number($"SELECT WaWarehouseFlag FROM dbo.T_Hattyu WHERE HaID={invalidLegacyOrder}") == 0
                && ReceiptCount(invalidLegacyOrder) == 0 && Stock(1) == 45,
                "a legacy order with a manufacturer mismatch stays outside the receiving workflow");
        }
        finally { Environment.SetEnvironmentVariable(connectionSetting, previousConnection); }
    }

    private static int ReadNumber(SqlConnection connection, string query)
    {
        using var command = new SqlCommand(query, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void RequireReceivingFixture(SqlConnection connection, string name)
    {
        if (!name.StartsWith("ReceivingChecks_", StringComparison.Ordinal) || connection.Database != name)
            throw new InvalidOperationException("The test connection is outside the receiving fixture.");
    }

    private static void Sql(SqlConnection connection, string statement)
    {
        using var command = new SqlCommand(statement, connection);
        command.ExecuteNonQuery();
    }
}
