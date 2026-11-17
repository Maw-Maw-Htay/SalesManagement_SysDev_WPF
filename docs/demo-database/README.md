# 関西OA機器販売株式会社（架空）デモデータベース

`SalesManagement_Demo` is a separate catalog on the existing `(LocalDB)\MSSQLLocalDB` instance. The WPF demo process uses the same schema, authentication, management screens and transaction services as the normal application. The original `SalesManagement` remains the default catalog.

## Run the prepared demo

From `C:\Projects\SalesManagement_SysDev_WPF` in Windows PowerShell, run the first command to copy the demo administrator password. Run the second command to build and start a separate demo window. Enter employee ID **9101** and paste the password into the password box. The login screen and main window show `デモ`.

```powershell
& .\tools\DemoDatabase\Copy-DemoPassword.ps1 -EmployeeId 9101
& .\tools\DemoDatabase\Open-Demo.ps1
```

Employee ID **9102** demonstrates 営業 access and **9103** demonstrates 物流 access. Use the password script with the corresponding ID. The credential file uses Windows data protection tied to the current Windows account and machine. The fixture stores the application's existing salted PBKDF2 hashes in SQL Server. `EmPassword` holds a random compatibility placeholder and is required by the existing schema.

## Source database and dependencies

The source inventory records the real LocalDB engine, collation, table counts, column sizes, identity keys, foreign keys, indexes, status flags and audit counts. It aggregates employee counts without exporting employee identifiers or credentials. [Original database inventory](original-inventory.md)

| Table group | Dependency |
| --- | --- |
| `M_Position`, `M_SalesOffice`, `M_Employee` | Employees require one valid role and office. The application recognizes `管理者`, `営業`, and `物流`. The existing password hash column and role/employee flags are required for login. |
| `M_MajorClassification`, `M_SmallClassification`, `M_Maker`, `M_Product` | The product catalog references a small classification and manufacturer. Small classifications reference a major classification. A manufacturer is also the purchase-order supplier in this schema. |
| `M_Client` | Each customer belongs to one sales office. A 受注 and its successors must use the same customer and office. |
| `T_Stock` | Inventory is scoped by `PrID`. An active product has one active balance in the fixture. There are no warehouse balances or stock movement ledger tables. |
| `T_Order` through `T_Sale` and their detail tables | `OrID` refers to the original 受注 across downstream documents. Where there is no direct successor ID, the services match order, customer, office and product quantities. |
| `T_Hattyu` and `T_Warehousing` with detail tables | Receipts refer to the purchase ID and contain products belonging to its supplier. The application derives purchasing progress from confirmed active receipt quantities. |
| `T_OrderOperationHistory`, `T_EmployeeAssignmentHistory` | Audit entries refer to recorded business or employee actions and carry a historical snapshot. The logical IDs and audit history need to stay with any retained transactions or employee records. |

The older initializer contains `SeedMinimumData` and `Seed150SampleData`, which are separate from the demo fixture. The current login preparation checks a database connection. The isolated demo login additionally checks the fixture version marker before allowing a session.

## Data specification

Five branches serve Osaka headquarters, North Osaka, Kyoto, Kobe and Nara. The fixture provides 15 employees, 30 customers, 10 suppliers, four major classifications, 10 small classifications and 50 products. The company, employee names, trading partners and model numbers are fictional. Contact fields contain clearly marked fictional locations and `000` telephone and postal placeholders. The actual customer and supplier tables have no email column. The product codes are 13-digit, check-digit-valid demo barcodes for use within this application. One customer, one discontinued product and one employee demonstrate the supported hidden states.

The catalog covers desktops, notebooks, displays, printing, input devices, networking, storage, accessories and OA consumables. `M_Product.Price` is the existing selling price field. Purchase lines have product and quantity fields, so the fixture leaves purchase unit cost outside the database. The production 受注 and shipment services write the amounts. Fixture catalog prices stay constant during generation. The application uses its existing net subtotal and 10% tax calculation when displaying 受注.

| Last completed step | Customer orders |
| --- | ---: |
| 受注 entered and awaiting confirmation, including one hidden entry | 9 |
| 受注 confirmed and 注文 created | 6 |
| 注文 confirmed and 出庫 awaiting confirmation | 6 |
| 出庫 confirmed and 入荷 awaiting confirmation | 5 |
| 入荷 confirmed and 出荷 awaiting confirmation | 5 |
| 出荷 confirmed and 売上 generated | 9 |

Five purchases have no confirmed receiving, four are partially received and six are fully received. Pending receipt documents are included in the first two groups. The receipt document-creation fixture supplies the pending header and lines because the installed workflow confirms existing receipt records. `WarehousingService.ConfirmShelf` performs the confirmation and stock credit. `BusinessService.ConfirmChumon` performs the stock debit for sales. Subsequent stages use the normal service methods and their status flags.

The opening balance is inserted once per product in the initial fixture transaction. It is separate from receiving and consumes no receiving history. The reconciliation reports the opening balance, only confirmed receipts, only confirmed 注文 deductions and the closing quantity for every product. The dataset includes zero and low stock, a draft requesting an out-of-stock item, and a draft whose requested quantity exceeds a positive balance.

Dates represent a simulated working calendar keyed to the provisioning date in Japan time, with weekdays between completed stages. The tool assigns the dates only to its own isolated demo records after using the normal workflow. Each generated audit actor is marked `（デモ）`. The order audit notes also state `【デモデータ】`. The original audit tables were excluded from data extraction.

## Reproduce and verify

Visual Studio 2022 with SQL Server Data Tools or a local `SqlPackage.exe`, `sqlcmd`, and the project's .NET 8 Windows runtime are required. Review the schema and supply the SqlPackage path explicitly if it is outside the standard installation location.

```powershell
& .\tools\DemoDatabase\New-OriginalBackup.ps1
& .\tools\DemoDatabase\New-DemoSchema.ps1
& .\tools\DemoDatabase\Invoke-DemoData.ps1 -Mode seed
& .\tools\DemoDatabase\Invoke-DemoData.ps1 -Mode verify
& .\tools\DemoDatabase\Invoke-DemoData.ps1 -Mode smoke
```

`New-DemoSchema.ps1` extracts schema only, creates the fixed demo catalog if absent, and verifies its name and schema markers. The generator checks the catalog and fixture markers before writing. It has a single seed lock, uses the committed document status when resuming known fixture operations, and verifies a completed fixture on repeat runs. If you change demo records interactively after setup, verifying the original fixture may report a difference. The readiness marker still permits normal demo use.

The Windows-encrypted credential store under `build_verify/demo-database/private` is Git-ignored. Keep it with the demo if you plan to verify the exact seeded credentials later. Use the real login screen to change a demo password when needed. The verification command checks that the saved credential file still matches the database.

## Results and recovery

- [Verified backup evidence and recovery plan](backup-and-recovery.md)
- [Demo schema and actual counts per table](demo-inventory.md)
- [Relationship and inventory reconciliation](demo-reconciliation.md)
- [Build and application verification results](verification.md)
