# 受注更新 UI/UX 改善 — 2026-11-02

編集前にSPEC.md全体とAGENTS.md、添付要件、既存画面・サービスを確認した。従来仕様の「数量・備考のみ」は今回の明示要件に合わせて拡張し、営業所・受付担当者・単価・状態・確定・在庫・権限・拠点の制限を維持した。

## 実装内容

- 通常は既存F_Main内に表示し、登録と同じ白い共通ヘッダーとアイコンのサイドバーを使う。更新画面内でタイトルを重ねて表示しない。単独Windowで使う経路にも同じ白いヘッダーを用意した。
- 左に受注情報・商品入力・備考、右に明細・合計を配置。入力と明細の内部だけをスクロールし、フッターと合計を固定する。狭い入力領域では顧客・営業所・日付を縦に配置する。
- 顧客IDは数字・ゼロ埋め・C-付きの既存検索を再利用する。顧客変更は既存の確認ダイアログで確認し、保存するまでDBを書き換えない。営業所と元の受付担当者は変更しない。
- 商品検索、在庫照会、重複商品の数量合算、追加後のリセット、UndoはOrderRegistrationViewModelを合成して使う。QuantityUpDown、RegistrationLookup、登録用ResourceDictionary、商品詳細表示も共用する。
- 既存の受注明細単価と、未変更のレガシー保存金額を保持する。新規追加商品には現在のマスタ単価を使用し、サービス側で再検証する。数量はEnter不要で税・合計へ即時反映する。
- 保存中は再送信と離脱を禁止し、失敗時は入力を保持する。保存成功後に既存F_OrderNoticeを再利用し、×・Escでは保存済み内容を読み取り専用で残す。一覧・詳細ボタンも提供する。
- 元の受付担当者IDを保持し、更新者を既存の監査履歴へ別に記録する。更新では在庫減算・新規受注・自動確定を行わない。

## DB更新

既存のUpdateOrderCoreとSerializableトランザクションを使う。権限・所属営業所・未確定・通常表示・注文未連携を再検証し、元のヘッダー・明細のスナップショットで競合を検出する。顧客の有効性と営業所、単価、数量、金額上限を検証してからT_Order、T_OrderDetail、T_OrderOperationHistoryを同時保存する。DBエンティティ、接続設定、既存DBスキーマ、税率・端数処理は変更していない。保存済み明細の削除はサービス側でも拒否する。

## 実行した検証

- UI・入力・完了通知・登録機能の回帰: 334項目成功。バインディングエラー0件。
- 1920×1080、1600×900、1366×768、1280×720の100%・125%・150%に対応する配置計算とWPF寸法での描画を検証。実機のDPI設定変更は行っていない。
- 実際のWPF ShowDialogで、完了通知の閉じる・Esc・一覧・詳細を検証。所有Windowのネイティブ有効状態の復帰と、スクロールが有効なままであることを確認した。
- 分離したSQL Server LocalDBで38項目成功。顧客・日付・追加行の保存、元の受注番号・受付担当者・在庫の保持、異なる更新者の監査、権限・拠点・確定状態・連携済みの拒否、単価偽装拒否、競合、未変更再保存拒否を確認した。
- SQLエラーを監査テーブルの一時トリガーで発生させ、顧客・日付・追加行・監査の変更がすべてロールバックされることを確認した。
- 一時DB SalesManagement_OrderUpdateChecks_a8231a81712d421ea73de5f6cee76aa7と、一時インスタンスCodexOrderUpdateChecks_43fa5639は削除済み。既存SalesManagementに保存テストを実行していない。
- ソリューションビルド成功: 警告0、エラー0。別OutDirとUseAppHost=falseを使用した。テストプロジェクトのビルド時にはNuGet脆弱性情報の取得に関するNU1900警告が出たが、コンパイルとテストは成功した。

```powershell
dotnet build SalesManagement_SysDev.sln --no-restore -m:1 -v:minimal -p:OutDir=build_verify\update-ux\ -p:UseAppHost=false
dotnet run --project tests/OrderUpdateChecks/OrderUpdateChecks.csproj --no-restore -- --skip-database
# DB検証はREADMEに従い、固有名の一時インスタンスへORDER_UPDATE_TEST_SERVERを設定して--database-onlyで実行。
```

ログ: [UI検証](C:/Projects/SalesManagement_SysDev_WPF/build_verify/order-update-ux-ui.log)、[DB検証](C:/Projects/SalesManagement_SysDev_WPF/build_verify/order-update-ux-database.log)。

## 残る確認事項

- 保存済み明細の削除について、AGENTS.mdの「受注・売上・在庫などの取引データは物理削除しない。」と、添付要件の削除機能が衝突する。削除フラグのない既存スキーマは変更していない。方針の回答待ちのため、現在は新規追加分のみ削除・Undo可能とし、保存済み行の削除は無効にしている。
- 通常のMSSQLLocalDBには起動エラーが残っている。既存インスタンスやSalesManagementの作り直しは行っていない。実データを使った一覧から保存までの手動通し操作は未実施で、UIは制御可能なデータソース、SQL保存は分離DBで検証した。
- 通常の実行先へ反映する場合は、Visual Studioでデバッグを停止してから再ビルド・再実行する。

## 変更ファイル

- [SalesManagement_SysDev/F_OrderUpdate.xaml](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderUpdate.xaml): 登録と同じ2カラム、商品入力、顧客変更、明細操作、固定合計・フッター、狭い入力領域への対応。
- [SalesManagement_SysDev/F_OrderUpdate.xaml.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderUpdate.xaml.cs): 既存シェルへの埋め込み、変更破棄確認、既存の完了通知を使った4経路、日付入力とフォーカス管理。
- [SalesManagement_SysDev/Presentation/OrderUpdateViewModel.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/Presentation/OrderUpdateViewModel.cs): 登録VMの合成による検索・追加・在庫・Undoの再利用、元の保存値と編集下書きの分離、顧客確認、変更検出、保存状態。
- [SalesManagement_SysDev/Presentation/OrderUpdateDataSource.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/Presentation/OrderUpdateDataSource.cs): 元の競合検出スナップショットと編集後ヘッダーを別々に既存サービスへ渡す。
- [SalesManagement_SysDev/BusinessService.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/BusinessService.cs): 同一営業所の有効顧客・日付・追加明細の更新、単価と既存明細の保持、未変更再保存の拒否。
- [SalesManagement_SysDev/F_Main.xaml.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_Main.xaml.cs): 既存ヘッダーとサイドバーで更新をホストし、離脱ガードと破棄時のDisposeを統合。
- [SalesManagement_SysDev/F_OrderDetail.xaml.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderDetail.xaml.cs): 更新コマンドをシェル内の編集フローへ接続。単独利用の既存Window経路も維持。
- [SalesManagement_SysDev/Presentation/OrderRegistrationViewModel.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/Presentation/OrderRegistrationViewModel.cs): 共通Undoで保存済み明細の識別情報を保持する表示用メタデータを追加。登録時の保存処理は変更なし。
- [SalesManagement_SysDev/OrderProductDetailsOverlay.xaml](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/OrderProductDetailsOverlay.xaml): 登録・更新で共用する既存商品詳細表示の抽出。非表示時はCollapsedかつヒットテスト無効。
- [SalesManagement_SysDev/OrderProductDetailsOverlay.xaml.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/OrderProductDetailsOverlay.xaml.cs): 共通の商品詳細の閉じるボタンへフォーカスする処理。
- [SalesManagement_SysDev/F_OrderRegistration.xaml](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderRegistration.xaml): 既存の商品詳細UIを共用コントロールへ置き換え。
- [SalesManagement_SysDev/F_OrderRegistration.xaml.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderRegistration.xaml.cs): 共用商品詳細への既存フォーカス処理の接続。
- [SalesManagement_SysDev/F_OrderNotice.xaml](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderNotice.xaml): 既存通知に×を追加し、Escは閉じるCommandへ接続。
- [SalesManagement_SysDev/F_OrderNotice.xaml.cs](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/F_OrderNotice.xaml.cs): 通知の×・Escと、一覧ボタンを区別する。既存の確定・取消の戻り値を維持。
- [SPEC.md](C:/Projects/SalesManagement_SysDev_WPF/SPEC.md): 従来の数量・備考のみの要件を今回の顧客・日付・追加商品の編集へ更新。削除禁止方針を維持。
- [SalesManagement_SysDev/README.md](C:/Projects/SalesManagement_SysDev_WPF/SalesManagement_SysDev/README.md): 利用者向けの編集・保存・完了・離脱・削除制限の説明。
- [tests/OrderUpdateChecks/Program.cs](C:/Projects/SalesManagement_SysDev_WPF/tests/OrderUpdateChecks/Program.cs): UI・VM・既存登録機能の回帰、SQL更新・監査・競合・ロールバックの検証。
- [tests/OrderUpdateChecks/README.md](C:/Projects/SalesManagement_SysDev_WPF/tests/OrderUpdateChecks/README.md): 現行の自動入力・画面・DB検証の手順。
