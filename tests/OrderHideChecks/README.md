# 受注非表示の検証

Windows / .NET 8以降 / SQL Server LocalDB 15.0 が必要です。追加のテストパッケージは使用しません。

リポジトリ直下で実行します。

```powershell
dotnet run --project tests/OrderHideChecks -p:UseAppHost=false
```

UI・入力状態・レイアウトだけを検証する場合:

```powershell
dotnet run --project tests/OrderHideChecks -p:UseAppHost=false -- --skip-database
```

受注詳細だけを検証する場合は `--details-only --skip-database` を指定します。未確定／確定済み／非表示の操作権限、印刷可否、A4縦の個別伝票プレビュー、税・合計、100明細の複数ページ化、履歴の折りたたみ、明細幅、固定ヘッダー・フッターを確認し、実プリンターには出力しません。

既存の `SalesManagement` を読み取りだけで確認する場合は `--database-read-only` を指定します。このモードは保存済み受注・顧客・商品・監査履歴をSELECTで取得し、確定済み受注1件を伝票印刷用サービスで受注ID指定して再取得するだけで、テストDBの作成や取引データの更新は行いません。

既存のLocalDBインスタンスを利用できない場合は、専用の一時インスタンスで実行できます。

```powershell
$hideTestInstance = 'CodexOrderHideChecks_' + [Guid]::NewGuid().ToString('N').Substring(0,8)
try {
    SqlLocalDB create $hideTestInstance 15.0 -s
    $env:ORDER_HIDE_TEST_SERVER = '(LocalDB)\' + $hideTestInstance
    dotnet run --project tests/OrderHideChecks -p:UseAppHost=false
} finally {
    Remove-Item Env:ORDER_HIDE_TEST_SERVER -ErrorAction SilentlyContinue
    SqlLocalDB stop $hideTestInstance -k
    SqlLocalDB delete $hideTestInstance
}
```

テストDB名は `SalesManagement_OrderHideChecks_` に実行ごとのGUIDを付加します。作成した専用DBだけを最後に削除し、既存の `SalesManagement` データは変更しません。本番と同じ `HideOrderCore` に専用コンテキストを渡して検証します。

- 空欄、半角・全角空白、改行だけの理由、前後空白の除去、日本語の複数行入力、対象番号・件数。
- 非同期処理の完了待ち、処理中表示、入力・閉じる・二重送信の禁止、失敗時の理由保持と再試行。
- 一括の部分成功、失敗番号・件数、成功済み受注を再送信しない再試行。
- 実際のモーダルウィンドウの初期フォーカス、キャンセル・×・Escに割り当てたコマンド・通常のCloseによる未送信、処理中のClose拒否。
- 親中央への配置、標準幅460・自動高さ、固定ヘッダーと横並びボタン、画面内検証、スクロールによるエラーへのアクセス、XAMLバインディング。
- 1920×1080、1600×900、1366×768、1280×720の100%・125%・150%相当の作業領域に対する配置計算とWPFレイアウト。画像は `build_verify/order-hide-preview` に保存。
- DBへの単一・一括保存、ロール・営業所・実行者ID・対象存在・既存非表示の検証、監査の社員ID・日時・営業所・受注ID・理由・状態遷移、レコード・明細・在庫・確定状態の保持。
- テストDBの監査テーブルだけに一時エラートリガーを追加し、非表示と監査履歴の同時ロールバックを確認。
- 同じ受注への並行送信で成功と監査記録が1回になること、表示方針に基づく状態別件数、監査の営業所範囲。
- 受注履歴要約、記録済み明細からの数量・金額・単価・追加変更、変更のない状態フラグ、元監査本文の保持、タイムラインの展開・折りたたみ・安定した新しい順。
- 受注共通の確認・通知ダイアログで、キャンセルが確定処理を続行しないこと、主操作が結果を返して閉じること。

実機の拡大率切替・複数モニター間移動・日本語IME候補の実操作、および実運用DBを使った一覧の検索条件／タブ／選択保持の通し操作は自動試験とは別に確認してください。キャンセル等はコマンド・WPFイベントで確認し、物理キー操作は発行していません。
