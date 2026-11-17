# 履歴管理の安全なUIチェック

Windows / .NET 8 以降で、リポジトリ直下から実行します。

```powershell
dotnet run --project tests/HistoryManagementChecks -p:UseAppHost=false
```

標準実行はDBを開かず、権限別のフィルター候補、既存の履歴アイコン・通知バッジ、折りたたみ状態、読み取り専用グリッド、ページホスト表示、受注の記録開始時点と商品別変更を確認します。

初期化済みの `SalesManagement` LocalDB で読み取り専用の確認もできます。

```powershell
dotnet run --project tests/HistoryManagementChecks -p:UseAppHost=false -- --read-only-localdb
```

このオプションは既存の有効な管理者・営業アカウントと保存済みの監査データを使います。検索・期間・ページ順序・対象別タイムライン・完了取引と権限制限を確認します。初期化、シード、移行、データ変更は行いません。
