# モジュール担当表

2026年11月6日の作業ツリーを基にした担当案です。アプリは現在の1つのWPFプロジェクトで共同開発します。下記のファイル担当は、変更の相談先とレビュー責任を示します。既存の画面、名前空間、サービス呼び出し、DBの権限・拠点境界を維持します。

| 担当 | 機能 | 作業ブランチ | 将来の候補 |
| --- | --- | --- | --- |
| チョー | ログイン管理・社員管理 | `feature/auth-employee` | `Modules/AuthEmployee/` |
| トゥン | 顧客管理・売上管理 | `feature/customer-sales` | `Modules/CustomerSales/` |
| 堀川 | 商品管理・在庫管理 | `feature/product-inventory` | `Modules/ProductInventory/` |
| モォー | 受注管理 | `feature/order-management` | `Modules/OrderManagement/` |
| 秋田 | 発注管理・入庫管理 | `feature/purchase-receiving` | `Modules/PurchaseReceiving/` |
| アン | 注文管理・出庫管理 | `feature/ordering-shipping` | `Modules/OrderingShipping/` |
| 重屋 | 入荷管理・出荷管理 | `feature/arrival-delivery` | `Modules/ArrivalDelivery/` |

統合担当の第一候補は**モォー**です。受注の画面遷移と注文への連携を担当するため、シェルや取引の境界にまたがる変更を確認しやすい役割です。チームで正式に指名し、GitHubの管理権限を持つ人と共有変更、統合順、共通ビルドを管理します。モォーが作成した共有PRは、アンを統合の代理候補とし、影響先の担当者がレビューします。

## 現在のファイル

以下のアプリ内パスは `SalesManagement_SysDev/` からの相対パスです。表の「XAMLペア」は各 `.xaml` と `.xaml.cs` の両方を指します。テストとリポジトリ文書のパスはリポジトリ直下からの相対パスです。

### チョー · ログイン管理・社員管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `F_login`、`EmployeeManagementView` |
| 表示モデル | `Presentation/LoginViewModel.cs`、`Presentation/EmployeeManagementViewModel.cs` |
| 認証・社員 | `EmployeeService.cs`、`EmployeePasswordHasher.cs`、`EmployeeSchemaInitializer.cs` |
| エンティティ | `Entity/MEmployee.cs`、`Entity/MPosition.cs`、`Entity/EmployeeAssignmentHistory.cs` |
| テスト | `tests/EmployeeManagementChecks/` |

共有ファイルの `BusinessWindows.cs` 内の `F_Employee` と `F_PasswordChange`、`BusinessService.cs` 内の認証・パスワード処理、`F_Main` 内のログイン管理を変更する場合は統合担当と調整します。役割や営業所の解釈を変える場合は全機能に影響します。

### トゥン · 顧客管理・売上管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `CustomerManagementView`、`SalesManagementView`、`F_SaleDetail`、`F_SaleHide` |
| 表示モデル | `Presentation/CustomerManagementViewModel.cs`、`Presentation/SalesManagementViewModel.cs` |
| サービス | `CustomerService.cs`、`SalesService.cs` |
| エンティティ | `Entity/MClient.cs`、`Entity/TSale.cs`、`Entity/TSaleDetail.cs` |
| テスト | `tests/CustomerManagementChecks/`、`tests/SalesManagementChecks/` |

共有の入口は `F_Client` と `F_Sale` です。顧客の有効状態や拠点の変更はモォーと、売上明細や集計の前提は売上を生成する出荷側の重屋と確認します。

### 堀川 · 商品管理・在庫管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `ProductManagementView`、`InventoryManagementView` |
| 表示モデル | `Presentation/ProductManagementViewModel.cs`、`Presentation/InventoryManagementViewModel.cs` |
| サービス | `ProductService.cs`、`InventoryService.cs` |
| エンティティ | `Entity/MProduct.cs`、`Entity/MMaker.cs`、`Entity/MMajorClassification.cs`、`Entity/MSmallClassification.cs`、`Entity/TStock.cs` |
| テスト | `tests/ProductManagementChecks/` |

共有の入口は `F_Product` と `F_Stock`、共有サービスには `EnsureStock` があります。未確定受注での商品使用判定はモォーと、入庫の加算と注文確定の減算は秋田・アンと確認します。

### モォー · 受注管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `F_OrderManagement`、`F_OrderRegistration`、`F_OrderUpdate`、`F_OrderDetail`、`F_OrderHide`、`F_OrderHistoryDetail`、`OrderProductDetailsOverlay`、`RegistrationLookup`、`F_Order` |
| 表示モデル・入力補助 | `OrderDetailViewModel.cs`、`OrderWorkspaceViewModel.cs`、`Presentation/OrderHideViewModel.cs`、`Presentation/OrderHideWindowPlacement.cs`、`Presentation/OrderHistoryDetailViewModel.cs`、`Presentation/OrderRegistrationViewModel.cs`、`Presentation/OrderUpdateViewModel.cs`、`Presentation/QuantityComboBox.cs`、`Presentation/QuantityUpDown.cs`、`Presentation/RegistrationBelowBreakpointConverter.cs`、`Presentation/RegistrationWindowPlacement.cs` |
| 画面フロー・データ取得 | `OrderManagementWindows.cs`、`OrderHistoryView.cs`、`OrderListActions.cs`、`OrderSearchCriteria.cs`、`OrderSearchSuggestions.cs`、`Presentation/OrderRegistrationDataSource.cs`、`Presentation/OrderUpdateDataSource.cs` |
| 業務・DB | `OrderOperationService.cs`（`BusinessService` の partial）、`OrderUpdateRules.cs`、`OrderHistoryService.cs`、`OrderSchemaInitializer.cs` |
| エンティティ | `Entity/TOrder.cs`、`Entity/TOrderDetail.cs`、`Entity/OrderOperationHistory.cs` |
| 画面リソース | `Resources/OrderDetail.xaml`、`Resources/OrderManagement.xaml`、`Resources/Registration.xaml`、`Resources/QuantityInput.xaml` |
| テスト・資料 | `tests/OrderHideChecks/`、`tests/OrderUpdateChecks/`、`ORDER_HIDE_REVIEW.md`、`ORDER_UPDATE_REVIEW.md`、`ORDER_UPDATE_UX_REVIEW.md`、`受注CSV_サンプル_20261030.csv`（共有前にデータ内容を確認） |

受注管理の一覧は `F_OrderManagement` です。`OrderManagementView.xaml` は注文管理なのでアンが担当します。`F_Order` は通常の `F_Main` 経路からの生成を確認できなかった旧画面で、保全はモォーが担当します。`OrderManagementWindows.cs` にある `OrderDetailSnapshot` と `OrderDetailLine` は、更新サービス・詳細画面・チェックの共通契約として扱います。受注確定や取消明細の扱いはアンと確認します。`F_OrderNotice` は複数機能が使うため下記の共通担当です。

### 秋田 · 発注管理・入庫管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `PurchaseOrderManagementView`、`F_HattyuRegister`、`F_HattyuDetail`、`F_HattyuHide` |
| 表示モデル・サービス | `Presentation/PurchaseOrderManagementViewModel.cs`、`PurchaseOrderService.cs` |
| エンティティ | `Entity/THattyu.cs`、`Entity/THattyuDetail.cs`、`Entity/TWarehousing.cs`、`Entity/TWarehousingDetail.cs` |
| テスト | `tests/PurchaseOrderChecks/` |

共有の入口は `F_Hattyu` と `F_Warehousing` です。入庫の画面、表示モデル、サービス、チェックはアンとの共同担当です。入庫確定や在庫加算の変更は堀川にもレビューを依頼します。

### アン · 注文管理・出庫管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `OrderManagementView`（注文管理） |
| 表示モデル | `ChumonManagementViewModel.cs` |
| エンティティ | `Entity/TChumon.cs`、`Entity/TChumonDetail.cs`、`Entity/TSyukko.cs`、`Entity/TSyukkoDetail.cs` |

共有の入口は `F_Chumon` と `F_Syukko` です。注文確定・非表示は `BusinessService.cs` 内の担当範囲です。出庫の画面、表示モデル、サービス、チェックは秋田との共同担当です。受注連携はモォー、在庫減算は堀川、入荷生成は重屋と確認します。

### 重屋 · 入荷管理・出荷管理

| 種別 | 現在のファイル |
| --- | --- |
| XAMLペア | `ArrivalManagementView`、`F_ArrivalConfirm`、`F_ArrivalDetail`、`F_ArrivalHide`、`ShipmentManagementView`、`F_ShipmentConfirm`、`F_ShipmentDetail`、`F_ShipmentHide` |
| 表示モデル | `Presentation/ArrivalManagementViewModel.cs`、`Presentation/ShipmentManagementViewModel.cs` |
| サービス | `ArrivalService.cs`、`ShipmentService.cs` |
| エンティティ | `Entity/TArrival.cs`、`Entity/TArrivalDetail.cs`、`Entity/TShipment.cs`、`Entity/TShipmentDetail.cs` |
| テスト | `tests/ArrivalManagementChecks/`、`tests/ShipmentManagementChecks/` |

共有の入口は `F_Arrival` と `F_Shipment` です。入荷の起点である出庫はアン、出荷確定で作る売上はトゥンと確認します。

### 共同担当と共通ファイル

| ファイル | 変更窓口・競合しやすい理由 |
| --- | --- |
| `WarehouseMovementManagementView`、`F_WarehouseMovementConfirm`、`F_WarehouseMovementDetail`、`F_WarehouseMovementHide` のXAMLペア、`Presentation/WarehouseMovementViewModel.cs`、`WarehouseMovementService.cs`、`tests/WarehouseMovementChecks/` | 秋田は `Inbound` / `WarehousingService`、アンは `Outbound` / `OutboundService`。共通表示・データ型・ファイル全体にまたがる修正は、両者と統合担当がレビューします。 |
| `App.xaml`、`App.xaml.cs`、`F_Main.xaml`、`F_Main.xaml.cs`、`SidebarViewModel.cs`、`HomeView` のXAMLペア、`Presentation/HomeViewModel.cs` | 統合担当。起動、ホーム、メニュー、権限表示、編集中の離脱確認を一か所で扱います。ログイン管理はチョー、受注の登録・更新の遷移はモォーと確認します。 |
| `AppSession.cs` | 統合担当とチョー。ロール、社員、営業所が全機能の認可の入力です。 |
| `BusinessWindows.cs` | 統合担当。`ManagementListPage` 基底クラス、各画面の入口、パスワード変更ダイアログを含みます。入口の変更は上の機能担当と確認します。 |
| `BusinessService.cs` | 統合担当。ログイン・パスワードはチョー、在庫初期化は堀川、受注登録・更新・確定関連はモォー、注文・出庫関連はアン、入荷・出荷の呼び出しは重屋と確認します。 |
| `Entity/SalesManagementContext.cs`、`Entity/MSalesOffice.cs`、`DatabaseInitializer.cs` | 統合担当とDB担当。プロバイダー、モデル、リレーション、営業所、初期化は複数機能に影響します。各機能に割り当てたエンティティもDB契約としてレビューします。 |
| `AppTheme.cs`、`AppTypography.cs`、`IconResourceConverter.cs`、`LucideIcon.cs`、`UiIcon.cs`、`Resources/Colors.xaml`、`Resources/Controls.xaml`、`Resources/IconResources.xaml`、`Resources/Layouts.xaml`、`Resources/Theme.xaml`、`Resources/Typography.xaml`、`Resources/Branding/`、`Resources/Lucide/` | 統合担当。色、フォント、スタイル、アイコン、ロゴ、同梱ライセンス。リソースキーやURIの変更は利用側を確認します。 |
| `F_OrderNotice`、`ManagementListView` のXAMLペア、`Presentation/ManagementListViewModel.cs`、`Presentation/ViewModelBase.cs` | 統合担当。汎用ダイアログ、共通画面、`RelayCommand` など。`F_OrderNotice` は受注以外の画面でも使われます。 |
| `SalesManagement_SysDev.csproj`、アプリ内 `.gitignore` と `README.md` | 統合担当。SDK、WPFの項目、パッケージとリソース、アプリの共通説明。READMEは対象機能の段落を担当者が更新します。 |

リポジトリ直下の `SalesManagement_SysDev.sln`、`.gitmodules`、`.gitignore`、`AGENTS.md`、`CLAUDE.md`、`README.md`、`SPEC.md`、`docs/` は統合担当が管理します。`SPEC.md` の機能節は機能担当が編集し、共通ルールは統合担当と確認します。`SPEC.html` と `SPEC.css` は仕様の閲覧用ファイルとして、仕様本文と一緒に採用・更新するか判断します。現在の `SalesManagement_SysDev` のgitlinkは、合流準備の担当範囲です。

### 担当や収録の確認が必要なもの

- `_dfd_render/` の `image1.png`、`sample.asta`、`source.docx` はアプリのビルド入力から外れています。作成者と配布可否が確定した時点で統合担当が資料としての収録を判断します。
- `F_Order` や `QuantityComboBox.cs` の旧画面・入力補助、`Resources/Theme.xaml` の集約辞書は上記の担当が保全します。利用箇所と実行時の参照を確認してから整理します。
- `.vs/`、`bin/`、`obj/`、`build_verify/`、`.tmp/` は作業環境または生成物です。Git管理用の `.git/` は各作業コピーの管理情報です。

共有ファイルを変更する前に、PRまたはIssueへ対象のメソッド・画面・リソースキーと影響範囲を書き、統合担当と影響先の担当者を指定します。変更の順番と確認項目は [統合ガイド](INTEGRATION_GUIDE.md) を参照してください。
