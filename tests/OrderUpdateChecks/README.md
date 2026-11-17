# 受注更新の検証

Windows / .NET 8以降 / SQL Server LocalDB 15.0 が必要です。追加のテストフレームワークは使用しません。

リポジトリ直下で実行します。

```powershell
dotnet run --project tests/OrderUpdateChecks -p:UseAppHost=false
```

UI・入力状態だけを検証する場合:

```powershell
dotnet run --project tests/OrderUpdateChecks -p:UseAppHost=false -- --skip-database
```

既存インスタンスを利用できない場合、専用の一時インスタンスを使用できます。既存の `MSSQLLocalDB` を初期化・変更する必要はありません。

```powershell
$orderUpdateInstance = 'CodexOrderUpdateChecks_' + [Guid]::NewGuid().ToString('N').Substring(0,8)
try {
    SqlLocalDB create $orderUpdateInstance 15.0 -s
    $env:ORDER_UPDATE_TEST_SERVER = '(LocalDB)\' + $orderUpdateInstance
    dotnet run --project tests/OrderUpdateChecks -p:UseAppHost=false
} finally {
    Remove-Item Env:ORDER_UPDATE_TEST_SERVER -ErrorAction SilentlyContinue
    SqlLocalDB stop $orderUpdateInstance -k
    SqlLocalDB delete $orderUpdateInstance
}
```

テスト用DBは `SalesManagement_OrderUpdateChecks_` に毎回固有のIDを付けて作成し、最後に削除します。既存の `SalesManagement` データは変更しません。DB検証は本番と同じ `UpdateOrderCore` を専用コンテキストで実行します。SQLエラー時のロールバックは、テスト用DB内の監査テーブルに一時的なエラー発生トリガーを作って検証します。

1920×1080、1600×900、1366×768、1280×720の各100%・125%・150%について、タスクバー相当領域を差し引いた配置計算と、そのWPF単位でのレイアウトを検証します。実機のディスプレイ設定は変更しません。描画画像は `build_verify/order-update-preview` に出力します。

受注登録・受注更新の共通NumericUpDownについて、直接入力・＋／−操作・Enter不要の即時計算・不正値保持と保存禁止・単価の参照専用・商品追加後のリセットを検証します。更新では、顧客IDの3形式検索と変更確認、保存済み受付担当者と日付の保持、商品追加・重複合算・新規行削除・共通Undo、在庫警告、未変更／無効／保存中／失敗／成功、二重送信、未保存離脱確認、完了通知の×／Esc／一覧／詳細経路を検証します。

DB検証では既存のUpdateOrderCoreを使い、顧客・日付・数量・備考・追加商品の保存、元の受付担当者と更新者の区別、保存済み単価の維持と偽装拒否、追加商品の現在単価検証、監査、競合、権限・拠点・状態・注文連携の拒否、未変更の再保存拒否、SQLエラー時のヘッダー・追加行・監査の同時ロールバックを検証します。受注番号・在庫・未確定状態を維持することも確認します。保存済み明細の削除は現在の物理削除禁止方針に従い拒否します。

特定の検証だけを行う場合は、`--layout-only --skip-database`、`--preview-only --skip-database`、または`--database-only`を指定できます。後者は固有名の一時テストDBだけを作成・削除します。既存のSalesManagementに対して保存テストを実行しません。
