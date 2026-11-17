# Demo verification results

Validation ran on `SalesManagement_Demo` in the project's SQL Server 2019 LocalDB on 2026-11-16. The WPF solution and demo runner each built with **0 warnings and 0 errors**. The test process used `--demo-database` and opened the real shell offscreen. The runner rejected an invocation without that flag before accessing the database.

## Database and fixture checks

- The 27 table definitions, primary key columns, index definitions and 50 foreign keys matched the original schema. SQL Server generated a different name for the system-named `T_OrderOperationHistory.Id` primary key in the demo. Both databases use compatibility level 150 and `SQL_Latin1_General_CP1_CI_AS`.
- The original schema inventory, exact row counts, and grouped status, employee and audit totals matched the capture taken before the backup.
- `DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS` found no violations, and all 50 foreign keys are enabled and trusted.
- Every sales stage has the planned successor count, matching customers and branches, and matching quantities. All 40 order detail view models returned the expected subtotal, yen-rounded tax and total. Stored sale detail amounts also match their planned selling prices.
- All 15 purchase orders and 17 receipts have matching supplier, product, quantity and date relationships. The application reports five waiting, four partially received and six fully received purchases.
- The 50 balances reconcile to 2,754 opening units plus 231 confirmed receipt units minus 362 units deducted by `注文確定`, for **2,623 closing units**. The inventory list displays 44 normal, 3 low and 2 zero active-product balances.
- The application login service accepted the credentials for all 14 active demo accounts and rejected the disabled account and a wrong administrator password. The WPF login screen recognized the demo readiness marker.
- Central audit paging, record chronology, completed sales history and office scope passed. The fixture has 72 labeled order audit entries and two labeled employee assignment entries.
- A second seed invocation recognized the completed fixture and verified the same rows through the read path. No duplicate fixture documents or stock movements were generated.

Full counts and per-product balances are provided in the [demo inventory](demo-inventory.md) and [reconciliation](demo-reconciliation.md).

## WPF management screen navigation

Each row shows how many records the real screen loaded in its initial view. Paging or visibility filters can reduce this count relative to the tables.

| Screen | Loaded rows |
| --- | ---: |
| 受注管理 | 39 |
| 注文管理 | 31 |
| 発注管理 | 15 |
| 入庫管理 | 17 |
| 出庫管理 | 20 |
| 入荷管理 | 19 |
| 出荷管理 | 14 |
| 売上管理 | 9 |
| 在庫管理 | 20 |
| 顧客管理 | 20 |
| 商品管理 | 20 |
| 社員管理 | 15 |
| 履歴管理 | 20 |

Clicking `次へ` in 在庫管理 and 履歴管理 displayed distinct second pages. The history count showed `21～40件` on page two.
