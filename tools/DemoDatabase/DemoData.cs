namespace SalesManagement.Demo;

internal enum SalesStage { Draft, OrderCreated, OutboundCreated, ArrivalCreated, ShipmentCreated, SaleRecorded }

internal sealed record OfficeSeed(string Name, string City);
internal sealed record ProductSeed(string Model, string Name, decimal Price, string Color, int SafetyStock, int Opening);
internal sealed record SupplierSeed(string Name, string Category, string Group, ProductSeed[] Products);
internal sealed record CustomerSeed(int Office, string Name, int Number, bool Hidden);
internal sealed record EmployeeSeed(int Id, string Name, int Office, string Role, bool Hidden);
internal sealed record SaleSeed(int Number, CustomerSeed Customer, SalesStage Stage, DateTime Date, DateTime Delivery,
    IReadOnlyList<(int Product, int Quantity)> Lines, string Note, bool Hidden);
internal sealed record ReceiptSeed(DateTime Date, bool Confirmed, IReadOnlyList<(int Product, int Quantity)> Lines);
internal sealed record PurchaseSeed(int Number, int Supplier, int Office, DateTime Date,
    IReadOnlyList<(int Product, int Quantity)> Lines, IReadOnlyList<ReceiptSeed> Receipts);

internal static class DemoData
{
    internal const string Marker = "KansaiOA/v1";
    internal const string AuditLabel = "（デモ）";
    internal const int Admin = 9101;
    internal static int Sales(int office) => 9102 + office * 3;
    internal static int Logistics(int office) => 9103 + office * 3;

    internal static readonly OfficeSeed[] Offices =
    [
        new("大阪本社", "大阪府大阪市"), new("北大阪営業所", "大阪府吹田市"), new("京都営業所", "京都府京都市"),
        new("神戸営業所", "兵庫県神戸市"), new("奈良営業所", "奈良県奈良市")
    ];

    internal static readonly SupplierSeed[] Suppliers =
    [
        new("株式会社蒼波コンピューティング", "電子機器", "デスクトップPC",
        [
            new("AN-D510", "法人向けスリムデスクトップ Core i5 16GB", 89800, "黒", 8, 58),
            new("AN-D730", "設計部門向けタワーPC Core i7 32GB", 156800, "黒", 5, 42),
            new("AN-M320", "省スペースミニPC 8GB 256GB", 47800, "黒", 10, 73),
            new("AN-A240", "受付用23.8型一体型デスクトップ", 111800, "白", 6, 40),
            new("AN-W900", "業務用ワークステーション 64GB", 259000, "黒", 4, 31)
        ]),
        new("白峰デジタル機器株式会社", "電子機器", "ノートPC",
        [
            new("SH-N141", "14型法人モバイルノート 16GB 512GB", 102800, "銀", 10, 68),
            new("SH-N156", "15.6型事務用ノートPC テンキー付き", 78800, "黒", 10, 59),
            new("SH-N133", "13.3型軽量ノートPC USB-C給電", 124800, "青灰", 8, 44),
            new("SH-N145", "14型タッチ対応コンバーチブルPC", 139800, "銀", 6, 40),
            new("SH-N170", "17.3型業務用ノートPC 32GB", 172800, "黒", 5, 32)
        ]),
        new("株式会社夕凪ディスプレイ", "電子機器", "液晶モニター",
        [
            new("YN-L240", "24型フルHD液晶ディスプレイ", 19800, "黒", 12, 87),
            new("YN-L270", "27型QHD液晶モニター 高さ調節対応", 31800, "黒", 9, 65),
            new("YN-L280", "28型4K液晶モニター USB-C対応", 48800, "黒", 7, 48),
            new("YN-L150", "15.6型モバイル液晶モニター", 22480, "灰", 8, 49),
            new("YN-L340", "34型ウルトラワイドモニター", 58600, "黒", 5, 40)
        ]),
        new("青檜画像機器株式会社", "電子機器", "プリンター・スキャナー",
        [
            new("AH-P410", "A4モノクロレーザープリンター 両面印刷", 34800, "白", 7, 51),
            new("AH-P600", "A4カラー複合機 有線LAN対応", 74800, "白", 5, 42),
            new("AH-S300", "業務用両面ドキュメントスキャナー", 43400, "灰", 6, 44),
            new("AH-P730", "A3カラーレーザープリンター", 132000, "白", 4, 32),
            new("AH-L220", "感熱式ラベルプリンター USB接続", 27800, "白", 6, 46)
        ]),
        new("株式会社雲路インターフェース", "PC周辺機器", "キーボード・マウス",
        [
            new("KJ-K110", "日本語109配列USBキーボード", 2380, "黒", 20, 92),
            new("KJ-K260", "静音ワイヤレスキーボード", 3780, "黒", 15, 77),
            new("KJ-M105", "有線光学式マウス 3ボタン", 1080, "黒", 22, 106),
            new("KJ-M250", "静音ワイヤレスマウス 5ボタン", 2280, "灰", 18, 90),
            new("KJ-T200", "USBテンキーパッド 19キー", 1680, "黒", 12, 69)
        ]),
        new("水鏡ネットワーク株式会社", "通信機器", "ネットワーク機器",
        [
            new("MK-S081", "8ポート法人向けギガビットスイッチ", 8980, "黒", 10, 62),
            new("MK-S241", "24ポート管理型PoE+スイッチ", 44800, "黒", 6, 46),
            new("MK-R620", "小規模拠点向けVPNルーター", 27800, "黒", 7, 50),
            new("MK-A610", "天井設置型Wi-Fi 6アクセスポイント", 19880, "白", 8, 62),
            new("MK-C305", "Cat6A LANケーブル 5m", 780, "青", 25, 110)
        ]),
        new("株式会社日和ストレージ", "PC周辺機器", "記憶装置",
        [
            new("HY-S100", "業務用SATA SSD 1TB", 10380, "黒", 15, 78),
            new("HY-N200", "NVMe SSD 2TB ヒートシンク付き", 19800, "黒", 10, 68),
            new("HY-H400", "外付けバックアップHDD 4TB", 14980, "黒", 10, 58),
            new("HY-U064", "USBメモリ 64GB キャップ式", 760, "銀", 30, 120),
            new("HY-N402", "2ベイ小規模オフィス向けNAS", 39800, "黒", 6, 43)
        ]),
        new("葉月オフィス部材株式会社", "PC周辺機器", "OAアクセサリー",
        [
            new("HZ-D650", "USB-Cドッキングステーション 65W", 10280, "灰", 12, 79),
            new("HZ-H420", "4ポートUSB 3.2ハブ", 2480, "銀", 14, 84),
            new("HZ-A090", "法人ノート向けUSB-C充電器 90W", 4580, "黒", 13, 64),
            new("HZ-V101", "モニターアーム クランプ式", 6480, "黒", 8, 60),
            new("HZ-C203", "HDMIケーブル 4K対応 3m", 980, "黒", 22, 97)
        ]),
        new("株式会社鳴砂コミュニケーション", "電子機器", "会議・受付機器",
        [
            new("NS-C108", "会議用フルHDウェブカメラ", 6980, "黒", 10, 62),
            new("NS-S320", "USB会議用スピーカーフォン", 16800, "黒", 8, 50),
            new("NS-H200", "片耳USBヘッドセット マイク付き", 3680, "黒", 15, 79),
            new("NS-U750", "事務所向け無停電電源 750VA", 25800, "黒", 7, 47),
            new("NS-B110", "受付用一次元バーコードリーダー", 8780, "白", 8, 4)
        ]),
        new("灯凪OAサプライ株式会社", "OA用品", "消耗品",
        [
            new("TN-T410", "A4モノクロ機用交換トナー 3000枚", 7280, "黒", 12, 4),
            new("TN-L056", "感熱ラベルロール 56mm 6巻", 3480, "白", 8, 0),
            new("TN-A450", "上質コピー紙 A4 500枚×5冊", 3080, "白", 12, 0),
            new("TN-C200", "複合機用メンテナンスキット", 11800, "黒", 7, 2),
            new("TN-L038", "旧型向け感熱ラベル 38mm", 1860, "白", 4, 0)
        ])
    ];

    internal static readonly string[][] CustomerNames =
    [
        ["株式会社浪速精算サービス", "有限会社弥生町設備", "京橋ワークプレイス株式会社", "株式会社堂島企画印刷", "有限会社御堂オフィス商会", "株式会社天満橋事務企画"],
        ["千里丘業務機器株式会社", "株式会社茨木テクノサポート", "有限会社箕面実務センター", "吹田ビジネス端末株式会社", "株式会社高槻物流企画", "有限会社豊中総務ラボ"],
        ["西陣システム工房株式会社", "株式会社桂川業務支援", "有限会社山科図面サービス", "洛北オフィス機材株式会社", "株式会社梅小路会議設備", "有限会社伏見電算室"],
        ["株式会社六甲事務機材", "有限会社灘サポート工業", "元町データ業務株式会社", "株式会社須磨電子整備", "有限会社兵庫港業務企画", "株式会社御影ワークサービス"],
        ["株式会社春日業務機器", "有限会社登美ヶ丘管理室", "生駒オフィス設備株式会社", "株式会社橿原書類センター", "有限会社若草電算機器", "株式会社南都ワーク企画"]
    ];

    private static readonly string[] EmployeeNames =
    [
        "朝比奈 直人", "木津川 沙季", "桐原 朔也", "三津谷 弘樹", "椎名 里穂", "柏木 環",
        "宮澄 亜矢", "御影 亮介", "久瀬 智紗", "南木 拓真", "水越 葵", "一ノ瀬 遥",
        "井手原 和奏", "藤代 結衣", "萱野 宗一"
    ];

    internal static ProductSeed[] Products => Suppliers.SelectMany(x => x.Products).ToArray();
    internal static CustomerSeed[] Customers => CustomerNames.SelectMany((names, office) => names.Select((name, index) =>
        new CustomerSeed(office, name, office * 6 + index + 1, office == 4 && index == 5))).ToArray();

    internal static EmployeeSeed[] Employees => EmployeeNames.Select((name, index) =>
    {
        var office = index / 3;
        var role = index % 3 == 2 ? "物流" : index % 3 == 0 && office is 0 or 2 ? "管理者" : "営業";
        return new EmployeeSeed(9101 + index, name, office, role, index == 12);
    }).ToArray();

    internal static string Barcode(int index)
    {
        var digits = $"02080000{index + 1:0000}";
        var sum = digits.Select((digit, position) => (digit - '0') * (position % 2 == 0 ? 1 : 3)).Sum();
        return digits + ((10 - sum % 10) % 10);
    }

    internal static DateTime BusinessDays(DateTime day, int offset)
    {
        var direction = Math.Sign(offset);
        while (offset != 0)
        {
            day = day.AddDays(direction);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) offset -= direction;
        }
        return day;
    }

    internal static SaleSeed[] SalesOrders(DateTime anchor)
    {
        int[][] products = [[0, 10, 20], [5, 35, 23], [15, 22, 39], [25, 28, 29], [30, 32, 34], [40, 41, 42]];
        var reasons = new[] { "新設部署の端末一式", "営業端末の入替", "受付と事務所の機器更新", "拠点ネットワーク整備", "バックアップ機器の更新", "会議室の追加設置" };
        return Enumerable.Range(0, 40).Select(i =>
        {
            var stage = i switch
            {
                < 9 => SalesStage.Draft, < 15 => SalesStage.OrderCreated, < 21 => SalesStage.OutboundCreated,
                < 26 => SalesStage.ArrivalCreated, < 31 => SalesStage.ShipmentCreated, _ => SalesStage.SaleRecorded
            };
            var office = i % 5;
            var customer = Customers.Where(x => x.Office == office && !x.Hidden).ElementAt(i / 5 % (office == 4 ? 5 : 6));
            var pattern = i % products.Length;
            var lines = products[pattern].Select((product, line) => (product, 2 + (i + line) % 5)).ToList();
            if (i % 3 == 0) lines.Add((33, 4 + i % 7));
            if (i == 0) lines.Add((46, 3));
            if (i == 1) lines.Add((48, 6));
            var day = BusinessDays(anchor, -(2 + (int)stage * 4 + i % 7));
            var delivery = stage == SalesStage.SaleRecorded ? BusinessDays(day, 5) : BusinessDays(anchor, 2 + i % 7);
            return new SaleSeed(i + 1, customer, stage, day, delivery, lines, $"【デモ{i + 1:00}】{reasons[pattern]}", i == 8);
        }).ToArray();
    }

    internal static PurchaseSeed[] Purchases(DateTime anchor) => Enumerable.Range(0, 15).Select(i =>
    {
        var supplier = i % 9;
        (int Product, int Quantity)[] lines = [(supplier * 5, 14 + i % 6), (supplier * 5 + 2, 10 + (i * 3) % 7)];
        var ordered = BusinessDays(anchor, -(3 + 2 * i));
        var first = lines.Select(x => (x.Product, x.Quantity / 2)).ToArray();
        var remainder = lines.Select(x => (x.Product, x.Quantity - x.Quantity / 2)).ToArray();
        var day = BusinessDays(ordered, 2);
        IReadOnlyList<ReceiptSeed> receipts = i switch
        {
            < 5 when i % 2 == 0 => [],
            < 5 => [new(day, false, lines)],
            < 9 when i % 2 == 0 => [new(day, true, first), new(BusinessDays(day, 1), false, remainder)],
            < 9 => [new(day, true, first)],
            _ when i % 2 == 0 => [new(day, true, first), new(BusinessDays(day, 1), true, remainder)],
            _ => [new(day, true, lines)]
        };
        return new PurchaseSeed(i + 1, supplier, i % 5, ordered, lines, receipts);
    }).ToArray();
}
