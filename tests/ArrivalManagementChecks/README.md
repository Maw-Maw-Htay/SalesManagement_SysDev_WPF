# 入荷管理チェック

リポジトリ直下で `dotnet run --project tests/ArrivalManagementChecks -p:UseAppHost=false` を実行すると、検索・日付条件、状態遷移、入荷明細検証、ロール、非表示理由を LocalDB に接続せず確認します。

このチェックは SQL のトランザクションを実行しません。実 `SalesManagement` LocalDB 上での入荷確定、出荷情報作成、同時実行時のロック挙動、非表示保存は、DB 接続可能な環境で別途統合確認が必要です。
