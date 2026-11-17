# 売上管理のチェック

リポジトリ直下から次のコマンドを実行します。

```powershell
dotnet run --project tests/SalesManagementChecks -p:UseAppHost=false
```

検索・日付範囲・金額・表示状態・通貨表示・平均計算と、ロール許可および非表示理由の検証を行います。チェックは既存 `SalesManagement` LocalDB へ書き込みません。売上一覧・詳細・非表示の実DB統合確認は、実際のLocalDBで利用者権限と既存データを確認してから実施してください。
