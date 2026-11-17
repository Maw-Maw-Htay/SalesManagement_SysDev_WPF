# 発注管理のチェック

`dotnet run --project tests/PurchaseOrderChecks/PurchaseOrderChecks.csproj` checks receipt progress, search, paging, registration input, permissions, read-only receipt details, CSV escaping, and A4 pagination without connecting to the database. The optional arguments `-- --read-db <administrator-employee-id>` check purchase, receipt, and catalog queries against LocalDB using SELECT operations only.
