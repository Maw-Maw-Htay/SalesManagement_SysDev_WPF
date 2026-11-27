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

下書きの保存・再編集・登録・削除と受注一覧に絞って検証する場合は `--draft-only` を指定します。

ウィンドウやデータベースを開かず、下書きの選択状態を確認する場合は `--draft-selection-only` を指定します。

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

下書きは別の `SalesManagement_OrderDraftChecks_` テストDBで検証します。既存スキーマへのテーブル追加、未完成の入力の再表示、社員・営業所の参照制限、同時編集、登録時のロールバックと重複防止を確認します。受注管理から下書きを開く操作と、保存失敗・成功時のサイドバー遷移も WPF を起動して確認します。

受注一覧も一時DBを使用して検証します。「すべて」と「下書き」の分離、タブ件数、下書きの保存・再編集・登録・削除による件数更新、検索・並べ替え・ページ移動、商品数・税込額・担当者・営業所、権限、確定と非表示、CSV取込、全ページの印刷プレビューを確認します。1280×720、1366×768、1920×1080の受注一覧画像は検証出力先に保存します。

受注一覧の保存では、状態タブ・営業所・下書き所有者を照合し、未選択時の全ページ出力とページをまたぐチェック行の出力を検証します。`--export-only` は DB に接続せずに PDF・Excel・CSV、キャンセル、上書きとロックを検証します。`--export-only --benchmark` では各形式で合成データ 30,000 行の書き出し時間、ファイルサイズ、サンプルしたマネージドヒープ使用量と UI スレッドのタイマー発火数を記録します。

行操作メニューは、1ページの全行で選択した行にひも付くこと、スクロールや並べ替えで閉じること、表示位置、状態ごとの項目、編集と確定の無効表示、一括操作のボタン状態を確認します。実際のマウスやキーボードで確認する場合は `--draft-only --interactive-order-menu` を指定します。テストデータの受注管理画面を閉じると、一時DBを削除します。

受注登録では、メーカーと文字検索の組み合わせ、キーボードでの商品選択、メーカー切替時の選択と明細の扱い、商品候補・詳細のメーカー表示を確認します。在庫未登録・在庫0・在庫不足・在庫ありの行表示、長い商品名と数量・金額欄の配置も検証します。

1920×1080、1600×900、1366×768、1280×720の各100%・125%・150%について、タスクバー相当領域を差し引いた配置計算と、そのWPF単位でのレイアウトを検証します。実機のディスプレイ設定は変更しません。描画画像は `build_verify/order-update-preview` に出力します。

受注登録・受注更新の共通NumericUpDownについて、直接入力・＋／−操作・Enter不要の即時計算・不正値保持と保存禁止・単価の参照専用・商品追加後のリセットを検証します。更新では、顧客IDの3形式検索と変更確認、保存済み受付担当者と日付の保持、商品追加・重複合算・新規行削除・共通Undo、在庫警告、未変更／無効／保存中／失敗／成功、二重送信、未保存離脱確認、完了通知の×／Esc／一覧／詳細経路を検証します。

DB検証では既存のUpdateOrderCoreを使い、顧客・日付・数量・備考・追加商品の保存、元の受付担当者と更新者の区別、保存済み単価の維持と偽装拒否、追加商品の現在単価検証、監査、競合、権限・拠点・状態・注文連携の拒否、未変更の再保存拒否、SQLエラー時のヘッダー・追加行・監査の同時ロールバックを検証します。受注番号・在庫・未確定状態を維持することも確認します。保存済み明細の削除は現在の物理削除禁止方針に従い拒否します。

特定の検証だけを行う場合は、`--layout-only --skip-database`、`--preview-only --skip-database`、または`--database-only`を指定できます。後者は固有名の一時テストDBだけを作成・削除します。既存のSalesManagementに対して保存テストを実行しません。
