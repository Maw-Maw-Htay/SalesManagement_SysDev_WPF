# SQL Server database inventory — [SalesManagement_Demo]

Captured 2026-11-16T01:17:10.8382874Z. SQL Server 15.0.4382.1, compatibility 150, collation `SQL_Latin1_General_CP1_CI_AS`.

## Tables and exact row counts
| Table | Rows |
| --- | ---: |
| `[dbo].[M_Client]` | 30 |
| `[dbo].[M_Employee]` | 15 |
| `[dbo].[M_MajorClassification]` | 4 |
| `[dbo].[M_Maker]` | 10 |
| `[dbo].[M_Position]` | 3 |
| `[dbo].[M_Product]` | 50 |
| `[dbo].[M_SalesOffice]` | 5 |
| `[dbo].[M_SmallClassification]` | 10 |
| `[dbo].[T_Arrival]` | 19 |
| `[dbo].[T_ArrivalDetail]` | 64 |
| `[dbo].[T_Chumon]` | 31 |
| `[dbo].[T_ChumonDetail]` | 104 |
| `[dbo].[T_EmployeeAssignmentHistory]` | 2 |
| `[dbo].[T_Hattyu]` | 15 |
| `[dbo].[T_HattyuDetail]` | 30 |
| `[dbo].[T_Order]` | 40 |
| `[dbo].[T_OrderDetail]` | 136 |
| `[dbo].[T_OrderOperationHistory]` | 72 |
| `[dbo].[T_Sale]` | 9 |
| `[dbo].[T_SaleDetail]` | 30 |
| `[dbo].[T_Shipment]` | 14 |
| `[dbo].[T_ShipmentDetail]` | 47 |
| `[dbo].[T_Stock]` | 50 |
| `[dbo].[T_Syukko]` | 25 |
| `[dbo].[T_SyukkoDetail]` | 84 |
| `[dbo].[T_Warehousing]` | 17 |
| `[dbo].[T_WarehousingDetail]` | 34 |

## Columns, identity, defaults, and computed expressions
| Table | Column | SQL type | Nullable | Identity | Default or expression |
| --- | --- | --- | --- | --- | --- |
| `[dbo].[M_Client]` | `ClID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_Client]` | `SoID` | `int` | no | — | — |
| `[dbo].[M_Client]` | `ClName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Client]` | `ClAddress` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Client]` | `ClPhone` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_Client]` | `ClPostal` | `nvarchar(7)` | no | — | — |
| `[dbo].[M_Client]` | `ClFAX` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_Client]` | `ClFlag` | `int` | no | — | — |
| `[dbo].[M_Client]` | `ClHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_Employee]` | `EmID` | `int` | no | — | — |
| `[dbo].[M_Employee]` | `EmName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Employee]` | `SoID` | `int` | no | — | — |
| `[dbo].[M_Employee]` | `PoID` | `int` | no | — | — |
| `[dbo].[M_Employee]` | `EmHiredate` | `date` | no | — | — |
| `[dbo].[M_Employee]` | `EmPassword` | `nvarchar(10)` | no | — | — |
| `[dbo].[M_Employee]` | `EmPhone` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_Employee]` | `EmFlag` | `int` | no | — | — |
| `[dbo].[M_Employee]` | `EmHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_Employee]` | `EmPasswordHash` | `nvarchar(255)` | yes | — | — |
| `[dbo].[M_MajorClassification]` | `McID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_MajorClassification]` | `McName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_MajorClassification]` | `McFlag` | `int` | no | — | — |
| `[dbo].[M_MajorClassification]` | `McHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_Maker]` | `MaID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_Maker]` | `MaName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Maker]` | `MaAddress` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Maker]` | `MaPhone` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_Maker]` | `MaPostal` | `nvarchar(7)` | no | — | — |
| `[dbo].[M_Maker]` | `MaFAX` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_Maker]` | `MaFlag` | `int` | no | — | — |
| `[dbo].[M_Maker]` | `MaHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_Position]` | `PoID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_Position]` | `PoName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Position]` | `PoFlag` | `int` | no | — | — |
| `[dbo].[M_Position]` | `PoHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_Product]` | `PrID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_Product]` | `MaID` | `int` | no | — | — |
| `[dbo].[M_Product]` | `PrName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_Product]` | `Price` | `decimal(9,0)` | no | — | — |
| `[dbo].[M_Product]` | `PrJCode` | `nvarchar(13)` | yes | — | — |
| `[dbo].[M_Product]` | `PrSafetyStock` | `int` | no | — | — |
| `[dbo].[M_Product]` | `ScID` | `int` | no | — | — |
| `[dbo].[M_Product]` | `PrModelNumber` | `nvarchar(20)` | no | — | — |
| `[dbo].[M_Product]` | `PrColor` | `nvarchar(20)` | no | — | — |
| `[dbo].[M_Product]` | `PrReleaseDate` | `date` | no | — | — |
| `[dbo].[M_Product]` | `PrFlag` | `int` | no | — | — |
| `[dbo].[M_Product]` | `PrHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_SalesOffice]` | `SoID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_SalesOffice]` | `SoName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_SalesOffice]` | `SoAddress` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_SalesOffice]` | `SoPhone` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_SalesOffice]` | `SoPostal` | `nvarchar(7)` | no | — | — |
| `[dbo].[M_SalesOffice]` | `SoFAX` | `nvarchar(13)` | no | — | — |
| `[dbo].[M_SalesOffice]` | `SoFlag` | `int` | no | — | — |
| `[dbo].[M_SalesOffice]` | `SoHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[M_SmallClassification]` | `ScID` | `int` | no | 1 / 1 | — |
| `[dbo].[M_SmallClassification]` | `McID` | `int` | no | — | — |
| `[dbo].[M_SmallClassification]` | `ScName` | `nvarchar(50)` | no | — | — |
| `[dbo].[M_SmallClassification]` | `ScFlag` | `int` | no | — | — |
| `[dbo].[M_SmallClassification]` | `ScHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_Arrival]` | `ArID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Arrival]` | `SoID` | `int` | no | — | — |
| `[dbo].[T_Arrival]` | `EmID` | `int` | yes | — | — |
| `[dbo].[T_Arrival]` | `ClID` | `int` | no | — | — |
| `[dbo].[T_Arrival]` | `OrID` | `int` | no | — | — |
| `[dbo].[T_Arrival]` | `ArDate` | `date` | yes | — | — |
| `[dbo].[T_Arrival]` | `ArStateFlag` | `int` | yes | — | — |
| `[dbo].[T_Arrival]` | `ArFlag` | `int` | no | — | — |
| `[dbo].[T_Arrival]` | `ArHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_ArrivalDetail]` | `ArDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_ArrivalDetail]` | `ArID` | `int` | yes | — | — |
| `[dbo].[T_ArrivalDetail]` | `PrID` | `int` | yes | — | — |
| `[dbo].[T_ArrivalDetail]` | `ArQuantity` | `int` | yes | — | — |
| `[dbo].[T_Chumon]` | `ChID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Chumon]` | `SoID` | `int` | no | — | — |
| `[dbo].[T_Chumon]` | `EmID` | `int` | yes | — | — |
| `[dbo].[T_Chumon]` | `ClID` | `int` | no | — | — |
| `[dbo].[T_Chumon]` | `OrID` | `int` | no | — | — |
| `[dbo].[T_Chumon]` | `ChDate` | `date` | yes | — | — |
| `[dbo].[T_Chumon]` | `ChStateFlag` | `int` | yes | — | — |
| `[dbo].[T_Chumon]` | `ChFlag` | `int` | no | — | — |
| `[dbo].[T_Chumon]` | `ChHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_ChumonDetail]` | `ChDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_ChumonDetail]` | `ChID` | `int` | no | — | — |
| `[dbo].[T_ChumonDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_ChumonDetail]` | `ChQuantity` | `int` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `Id` | `bigint` | no | 1 / 1 | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `EmployeeId` | `int` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `EmployeeName` | `nvarchar(50)` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `RecordedAt` | `datetimeoffset(7)` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `ActorEmployeeId` | `int` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `ActorName` | `nvarchar(50)` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `BeforeOfficeId` | `int` | yes | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `BeforeOfficeName` | `nvarchar(50)` | yes | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `AfterOfficeId` | `int` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `AfterOfficeName` | `nvarchar(50)` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `BeforePositionId` | `int` | yes | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `BeforeRoleName` | `nvarchar(50)` | yes | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `AfterPositionId` | `int` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `AfterRoleName` | `nvarchar(50)` | no | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `Operation` | `nvarchar(24)` | no | — | — |
| `[dbo].[T_Hattyu]` | `HaID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Hattyu]` | `MaID` | `int` | no | — | — |
| `[dbo].[T_Hattyu]` | `EmID` | `int` | no | — | — |
| `[dbo].[T_Hattyu]` | `HaDate` | `date` | no | — | — |
| `[dbo].[T_Hattyu]` | `WaWarehouseFlag` | `int` | yes | — | — |
| `[dbo].[T_Hattyu]` | `HaFlag` | `int` | no | — | — |
| `[dbo].[T_Hattyu]` | `HaHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_HattyuDetail]` | `HaDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_HattyuDetail]` | `HaID` | `int` | no | — | — |
| `[dbo].[T_HattyuDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_HattyuDetail]` | `HaQuantity` | `int` | no | — | — |
| `[dbo].[T_Order]` | `OrID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Order]` | `SoID` | `int` | no | — | — |
| `[dbo].[T_Order]` | `EmID` | `int` | no | — | — |
| `[dbo].[T_Order]` | `ClID` | `int` | no | — | — |
| `[dbo].[T_Order]` | `ClCharge` | `nvarchar(50)` | yes | — | — |
| `[dbo].[T_Order]` | `OrDate` | `date` | no | — | — |
| `[dbo].[T_Order]` | `OrStateFlag` | `int` | yes | — | — |
| `[dbo].[T_Order]` | `OrFlag` | `int` | no | — | — |
| `[dbo].[T_Order]` | `OrHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_Order]` | `OrDeliveryDate` | `date` | yes | — | — |
| `[dbo].[T_OrderDetail]` | `OrDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_OrderDetail]` | `OrID` | `int` | no | — | — |
| `[dbo].[T_OrderDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_OrderDetail]` | `OrQuantity` | `int` | no | — | — |
| `[dbo].[T_OrderDetail]` | `OrTotalPrice` | `decimal(10,0)` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `Id` | `bigint` | no | 1 / 1 | — |
| `[dbo].[T_OrderOperationHistory]` | `RecordedAt` | `datetimeoffset(7)` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `Operation` | `nvarchar(40)` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `OrderId` | `int` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `CustomerId` | `int` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `CustomerName` | `nvarchar(100)` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `OfficeId` | `int` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `BeforeState` | `int` | yes | — | — |
| `[dbo].[T_OrderOperationHistory]` | `AfterState` | `int` | yes | — | — |
| `[dbo].[T_OrderOperationHistory]` | `BeforeManagement` | `int` | yes | — | — |
| `[dbo].[T_OrderOperationHistory]` | `AfterManagement` | `int` | yes | — | — |
| `[dbo].[T_OrderOperationHistory]` | `Details` | `nvarchar(max)` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `ActorEmployeeId` | `int` | yes | — | — |
| `[dbo].[T_OrderOperationHistory]` | `ActorName` | `nvarchar(100)` | no | — | — |
| `[dbo].[T_OrderOperationHistory]` | `ActorOfficeName` | `nvarchar(100)` | no | — | — |
| `[dbo].[T_Sale]` | `SaID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Sale]` | `ClID` | `int` | no | — | — |
| `[dbo].[T_Sale]` | `SoID` | `int` | no | — | — |
| `[dbo].[T_Sale]` | `EmID` | `int` | no | — | — |
| `[dbo].[T_Sale]` | `OrID` | `int` | no | — | — |
| `[dbo].[T_Sale]` | `SaDate` | `date` | no | — | — |
| `[dbo].[T_Sale]` | `SaHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_Sale]` | `SaFlag` | `int` | no | — | — |
| `[dbo].[T_SaleDetail]` | `SaDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_SaleDetail]` | `SaID` | `int` | no | — | — |
| `[dbo].[T_SaleDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_SaleDetail]` | `SaQuantity` | `int` | no | — | — |
| `[dbo].[T_SaleDetail]` | `SaPrTotalPrice` | `decimal(10,0)` | no | — | — |
| `[dbo].[T_Shipment]` | `ShID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Shipment]` | `ClID` | `int` | no | — | — |
| `[dbo].[T_Shipment]` | `EmID` | `int` | yes | — | — |
| `[dbo].[T_Shipment]` | `SoID` | `int` | no | — | — |
| `[dbo].[T_Shipment]` | `OrID` | `int` | no | — | — |
| `[dbo].[T_Shipment]` | `ShStateFlag` | `int` | yes | — | — |
| `[dbo].[T_Shipment]` | `ShFinishDate` | `date` | yes | — | — |
| `[dbo].[T_Shipment]` | `ShFlag` | `int` | no | — | — |
| `[dbo].[T_Shipment]` | `ShHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_ShipmentDetail]` | `ShDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_ShipmentDetail]` | `ShID` | `int` | no | — | — |
| `[dbo].[T_ShipmentDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_ShipmentDetail]` | `ShQuantity` | `int` | no | — | — |
| `[dbo].[T_Stock]` | `StID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Stock]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_Stock]` | `StQuantity` | `int` | no | — | — |
| `[dbo].[T_Stock]` | `StFlag` | `int` | no | — | — |
| `[dbo].[T_Syukko]` | `SyID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Syukko]` | `EmID` | `int` | yes | — | — |
| `[dbo].[T_Syukko]` | `ClID` | `int` | no | — | — |
| `[dbo].[T_Syukko]` | `SoID` | `int` | no | — | — |
| `[dbo].[T_Syukko]` | `OrID` | `int` | no | — | — |
| `[dbo].[T_Syukko]` | `SyDate` | `date` | yes | — | — |
| `[dbo].[T_Syukko]` | `SyStateFlag` | `int` | yes | — | — |
| `[dbo].[T_Syukko]` | `SyFlag` | `int` | no | — | — |
| `[dbo].[T_Syukko]` | `SyHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_SyukkoDetail]` | `SyDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_SyukkoDetail]` | `SyID` | `int` | no | — | — |
| `[dbo].[T_SyukkoDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_SyukkoDetail]` | `SyQuantity` | `int` | no | — | — |
| `[dbo].[T_Warehousing]` | `WaID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_Warehousing]` | `HaID` | `int` | no | — | — |
| `[dbo].[T_Warehousing]` | `EmID` | `int` | no | — | — |
| `[dbo].[T_Warehousing]` | `WaDate` | `date` | no | — | — |
| `[dbo].[T_Warehousing]` | `WaShelfFlag` | `int` | yes | — | — |
| `[dbo].[T_Warehousing]` | `WaHidden` | `nvarchar(max)` | yes | — | — |
| `[dbo].[T_Warehousing]` | `WaFlag` | `int` | no | — | — |
| `[dbo].[T_WarehousingDetail]` | `WaDetailID` | `int` | no | 1 / 1 | — |
| `[dbo].[T_WarehousingDetail]` | `WaID` | `int` | no | — | — |
| `[dbo].[T_WarehousingDetail]` | `PrID` | `int` | no | — | — |
| `[dbo].[T_WarehousingDetail]` | `WaQuantity` | `int` | no | — | — |

## Primary keys, unique constraints, and indexes
| Table | Name | Type | PK | Unique | Key columns | Included columns | Filter |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `[dbo].[M_Client]` | `PK__M_Client__B1FCF8D58C35E4D2` | CLUSTERED | yes | yes | `ClID ASC` | — | — |
| `[dbo].[M_Client]` | `IX_M_Client_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[M_Employee]` | `PK__M_Employ__DCB5BEC24482B998` | CLUSTERED | yes | yes | `EmID ASC` | — | — |
| `[dbo].[M_Employee]` | `IX_M_Employee_PoID` | NONCLUSTERED | no | no | `PoID ASC` | — | — |
| `[dbo].[M_Employee]` | `IX_M_Employee_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[M_MajorClassification]` | `PK__M_MajorC__27B1E232B6CE2E6D` | CLUSTERED | yes | yes | `McID ASC` | — | — |
| `[dbo].[M_Maker]` | `PK__M_Maker__2725BF40FEBFC979` | CLUSTERED | yes | yes | `MaID ASC` | — | — |
| `[dbo].[M_Position]` | `PK__M_Positi__A4C01F9E8A03A2CA` | CLUSTERED | yes | yes | `PoID ASC` | — | — |
| `[dbo].[M_Product]` | `PK__M_Produc__A5021A4FEB48A98E` | CLUSTERED | yes | yes | `PrID ASC` | — | — |
| `[dbo].[M_Product]` | `IX_M_Product_ScID` | NONCLUSTERED | no | no | `ScID ASC` | — | — |
| `[dbo].[M_Product]` | `IX_M_Product_MaID` | NONCLUSTERED | no | no | `MaID ASC` | — | — |
| `[dbo].[M_SalesOffice]` | `PK__M_SalesO__BC3C9374243FA851` | CLUSTERED | yes | yes | `SoID ASC` | — | — |
| `[dbo].[M_SmallClassification]` | `PK__M_SmallC__ACB791BA805D9A7F` | CLUSTERED | yes | yes | `ScID ASC` | — | — |
| `[dbo].[M_SmallClassification]` | `IX_M_SmallClassification_McID` | NONCLUSTERED | no | no | `McID ASC` | — | — |
| `[dbo].[T_Arrival]` | `PK_T_Arrival` | CLUSTERED | yes | yes | `ArID ASC` | — | — |
| `[dbo].[T_Arrival]` | `IX_T_Arrival_ClID` | NONCLUSTERED | no | no | `ClID ASC` | — | — |
| `[dbo].[T_Arrival]` | `IX_T_Arrival_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_Arrival]` | `IX_T_Arrival_OrID` | NONCLUSTERED | no | no | `OrID ASC` | — | — |
| `[dbo].[T_Arrival]` | `IX_T_Arrival_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[T_ArrivalDetail]` | `PK_T_ArrivalDetail` | CLUSTERED | yes | yes | `ArDetailID ASC` | — | — |
| `[dbo].[T_ArrivalDetail]` | `IX_T_ArrivalDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_ArrivalDetail]` | `IX_T_ArrivalDetail_ArID` | NONCLUSTERED | no | no | `ArID ASC` | — | — |
| `[dbo].[T_Chumon]` | `PK__T_Chumon__AF02F0B8EDF28122` | CLUSTERED | yes | yes | `ChID ASC` | — | — |
| `[dbo].[T_Chumon]` | `IX_T_Chumon_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_Chumon]` | `IX_T_Chumon_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[T_Chumon]` | `IX_T_Chumon_ClID` | NONCLUSTERED | no | no | `ClID ASC` | — | — |
| `[dbo].[T_Chumon]` | `IX_T_Chumon_OrID` | NONCLUSTERED | no | no | `OrID ASC` | — | — |
| `[dbo].[T_ChumonDetail]` | `PK_T_ChumonDetail` | CLUSTERED | yes | yes | `ChDetailID ASC` | — | — |
| `[dbo].[T_ChumonDetail]` | `IX_T_ChumonDetail_ChID` | NONCLUSTERED | no | no | `ChID ASC` | — | — |
| `[dbo].[T_ChumonDetail]` | `IX_T_ChumonDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `PK_T_EmployeeAssignmentHistory` | CLUSTERED | yes | yes | `Id ASC` | — | — |
| `[dbo].[T_EmployeeAssignmentHistory]` | `IX_T_EmployeeAssignmentHistory_EmployeeId_RecordedAt` | NONCLUSTERED | no | no | `EmployeeId ASC`, `RecordedAt ASC` | — | — |
| `[dbo].[T_Hattyu]` | `PK_T_Hattyu` | CLUSTERED | yes | yes | `HaID ASC` | — | — |
| `[dbo].[T_Hattyu]` | `IX_T_Hattyu_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_Hattyu]` | `IX_T_Hattyu_MaID` | NONCLUSTERED | no | no | `MaID ASC` | — | — |
| `[dbo].[T_HattyuDetail]` | `PK_T_HattyuDetail` | CLUSTERED | yes | yes | `HaDetailID ASC` | — | — |
| `[dbo].[T_HattyuDetail]` | `IX_T_HattyuDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_HattyuDetail]` | `IX_T_HattyuDetail_HaID` | NONCLUSTERED | no | no | `HaID ASC` | — | — |
| `[dbo].[T_Order]` | `PK__T_Order__E1649648FAE4D6E9` | CLUSTERED | yes | yes | `OrID ASC` | — | — |
| `[dbo].[T_Order]` | `IX_T_Order_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_Order]` | `IX_T_Order_ClID` | NONCLUSTERED | no | no | `ClID ASC` | — | — |
| `[dbo].[T_Order]` | `IX_T_Order_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[T_OrderDetail]` | `PK__T_OrderD__45EDE90EEC5B2390` | CLUSTERED | yes | yes | `OrDetailID ASC` | — | — |
| `[dbo].[T_OrderDetail]` | `IX_T_OrderDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_OrderDetail]` | `IX_T_OrderDetail_OrID` | NONCLUSTERED | no | no | `OrID ASC` | — | — |
| `[dbo].[T_OrderOperationHistory]` | `PK__T_OrderO__3214EC07BCD35447` | CLUSTERED | yes | yes | `Id ASC` | — | — |
| `[dbo].[T_OrderOperationHistory]` | `IX_T_OrderOperationHistory_OrderId` | NONCLUSTERED | no | no | `OrderId ASC` | — | — |
| `[dbo].[T_OrderOperationHistory]` | `IX_T_OrderOperationHistory_OfficeId_RecordedAt` | NONCLUSTERED | no | no | `OfficeId ASC`, `RecordedAt ASC` | — | — |
| `[dbo].[T_Sale]` | `PK_T_Sale` | CLUSTERED | yes | yes | `SaID ASC` | — | — |
| `[dbo].[T_Sale]` | `IX_T_Sale_OrID` | NONCLUSTERED | no | no | `OrID ASC` | — | — |
| `[dbo].[T_Sale]` | `IX_T_Sale_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[T_Sale]` | `IX_T_Sale_ClID` | NONCLUSTERED | no | no | `ClID ASC` | — | — |
| `[dbo].[T_Sale]` | `IX_T_Sale_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_SaleDetail]` | `PK_T_SaleDetail` | CLUSTERED | yes | yes | `SaDetailID ASC` | — | — |
| `[dbo].[T_SaleDetail]` | `IX_T_SaleDetail_SaID` | NONCLUSTERED | no | no | `SaID ASC` | — | — |
| `[dbo].[T_SaleDetail]` | `IX_T_SaleDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_Shipment]` | `PK_T_Shipment` | CLUSTERED | yes | yes | `ShID ASC` | — | — |
| `[dbo].[T_Shipment]` | `IX_T_Shipment_ClID` | NONCLUSTERED | no | no | `ClID ASC` | — | — |
| `[dbo].[T_Shipment]` | `IX_T_Shipment_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[T_Shipment]` | `IX_T_Shipment_OrID` | NONCLUSTERED | no | no | `OrID ASC` | — | — |
| `[dbo].[T_Shipment]` | `IX_T_Shipment_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_ShipmentDetail]` | `PK_T_ShipmentDetail` | CLUSTERED | yes | yes | `ShDetailID ASC` | — | — |
| `[dbo].[T_ShipmentDetail]` | `IX_T_ShipmentDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_ShipmentDetail]` | `IX_T_ShipmentDetail_ShID` | NONCLUSTERED | no | no | `ShID ASC` | — | — |
| `[dbo].[T_Stock]` | `PK__T_Stock__C33CEFE204B2DFA6` | CLUSTERED | yes | yes | `StID ASC` | — | — |
| `[dbo].[T_Stock]` | `IX_T_Stock_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_Syukko]` | `PK_T_Syukko` | CLUSTERED | yes | yes | `SyID ASC` | — | — |
| `[dbo].[T_Syukko]` | `IX_T_Syukko_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_Syukko]` | `IX_T_Syukko_SoID` | NONCLUSTERED | no | no | `SoID ASC` | — | — |
| `[dbo].[T_Syukko]` | `IX_T_Syukko_ClID` | NONCLUSTERED | no | no | `ClID ASC` | — | — |
| `[dbo].[T_Syukko]` | `IX_T_Syukko_OrID` | NONCLUSTERED | no | no | `OrID ASC` | — | — |
| `[dbo].[T_SyukkoDetail]` | `PK_T_SyukkoDetail` | CLUSTERED | yes | yes | `SyDetailID ASC` | — | — |
| `[dbo].[T_SyukkoDetail]` | `IX_T_SyukkoDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_SyukkoDetail]` | `IX_T_SyukkoDetail_SyID` | NONCLUSTERED | no | no | `SyID ASC` | — | — |
| `[dbo].[T_Warehousing]` | `PK_T_Warehousing` | CLUSTERED | yes | yes | `WaID ASC` | — | — |
| `[dbo].[T_Warehousing]` | `IX_T_Warehousing_EmID` | NONCLUSTERED | no | no | `EmID ASC` | — | — |
| `[dbo].[T_Warehousing]` | `IX_T_Warehousing_HaID` | NONCLUSTERED | no | no | `HaID ASC` | — | — |
| `[dbo].[T_WarehousingDetail]` | `PK_T_WarehousingDetail` | CLUSTERED | yes | yes | `WaDetailID ASC` | — | — |
| `[dbo].[T_WarehousingDetail]` | `IX_T_WarehousingDetail_PrID` | NONCLUSTERED | no | no | `PrID ASC` | — | — |
| `[dbo].[T_WarehousingDetail]` | `IX_T_WarehousingDetail_WaID` | NONCLUSTERED | no | no | `WaID ASC` | — | — |

## Foreign-key relationships
| Constraint | Parent (dependent) | References | Column pairs | On delete | On update | Enabled / trusted |
| --- | --- | --- | --- | --- | --- | --- |
| `FK_M_Client_ToM_SalesOffice` | `[dbo].[M_Client]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_M_Employee_ToM_Position` | `[dbo].[M_Employee]` | `[dbo].[M_Position]` | `PoID` → `PoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_M_Employee_ToM_SalesOffice` | `[dbo].[M_Employee]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_M_Product_ToM_Maker` | `[dbo].[M_Product]` | `[dbo].[M_Maker]` | `MaID` → `MaID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_M_Product_ToM_SmallClassification` | `[dbo].[M_Product]` | `[dbo].[M_SmallClassification]` | `ScID` → `ScID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_M_SmallClassification_ToM_MajorClassification` | `[dbo].[M_SmallClassification]` | `[dbo].[M_MajorClassification]` | `McID` → `McID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Arrival_ToM_Client` | `[dbo].[T_Arrival]` | `[dbo].[M_Client]` | `ClID` → `ClID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Arrival_ToM_Employee` | `[dbo].[T_Arrival]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Arrival_ToM_SalesOffice` | `[dbo].[T_Arrival]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Arrival_ToT_Order` | `[dbo].[T_Arrival]` | `[dbo].[T_Order]` | `OrID` → `OrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_ArrivalDetail_ToM_Product` | `[dbo].[T_ArrivalDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_ArrivalDetail_ToT_Arrival` | `[dbo].[T_ArrivalDetail]` | `[dbo].[T_Arrival]` | `ArID` → `ArID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Chumon_ToM_Client` | `[dbo].[T_Chumon]` | `[dbo].[M_Client]` | `ClID` → `ClID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Chumon_ToM_Employee` | `[dbo].[T_Chumon]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Chumon_ToM_SalesOffice` | `[dbo].[T_Chumon]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Chumon_ToT_Order` | `[dbo].[T_Chumon]` | `[dbo].[T_Order]` | `OrID` → `OrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_ChumonDetail_ToM_Product` | `[dbo].[T_ChumonDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_ChumonDetail_ToT_Chumon` | `[dbo].[T_ChumonDetail]` | `[dbo].[T_Chumon]` | `ChID` → `ChID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Hattyu_ToM_Employee` | `[dbo].[T_Hattyu]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Hattyu_ToM_Maker` | `[dbo].[T_Hattyu]` | `[dbo].[M_Maker]` | `MaID` → `MaID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_HattyuDetail_ToM_Product` | `[dbo].[T_HattyuDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_HattyuDetail_ToT_Hattyu` | `[dbo].[T_HattyuDetail]` | `[dbo].[T_Hattyu]` | `HaID` → `HaID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Order_ToM_Client` | `[dbo].[T_Order]` | `[dbo].[M_Client]` | `ClID` → `ClID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Order_ToM_Employee` | `[dbo].[T_Order]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Order_ToM_SalesOffice` | `[dbo].[T_Order]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_OrderDetail_ToM_Product` | `[dbo].[T_OrderDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_OrderDetail_ToT_Order` | `[dbo].[T_OrderDetail]` | `[dbo].[T_Order]` | `OrID` → `OrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Sale_ToM_Client` | `[dbo].[T_Sale]` | `[dbo].[M_Client]` | `ClID` → `ClID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Sale_ToM_Employee` | `[dbo].[T_Sale]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Sale_ToM_SalesOffice` | `[dbo].[T_Sale]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Sale_ToT_Order` | `[dbo].[T_Sale]` | `[dbo].[T_Order]` | `OrID` → `OrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_SaleDetail_ToM_Product` | `[dbo].[T_SaleDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_SaleDetail_ToT_Sale` | `[dbo].[T_SaleDetail]` | `[dbo].[T_Sale]` | `SaID` → `SaID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Shipment_ToM_Client` | `[dbo].[T_Shipment]` | `[dbo].[M_Client]` | `ClID` → `ClID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Shipment_ToM_Employee` | `[dbo].[T_Shipment]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Shipment_ToM_SalesOffice` | `[dbo].[T_Shipment]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Shipment_ToT_Order` | `[dbo].[T_Shipment]` | `[dbo].[T_Order]` | `OrID` → `OrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_ShipmentDetail_ToM_Product` | `[dbo].[T_ShipmentDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_ShipmentDetail_ToT_Shipment` | `[dbo].[T_ShipmentDetail]` | `[dbo].[T_Shipment]` | `ShID` → `ShID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Stock_ToM_Product` | `[dbo].[T_Stock]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Syukko_ToM_Client` | `[dbo].[T_Syukko]` | `[dbo].[M_Client]` | `ClID` → `ClID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Syukko_ToM_Employee` | `[dbo].[T_Syukko]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Syukko_ToM_SalesOffice` | `[dbo].[T_Syukko]` | `[dbo].[M_SalesOffice]` | `SoID` → `SoID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Syukko_ToT_Order` | `[dbo].[T_Syukko]` | `[dbo].[T_Order]` | `OrID` → `OrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_SyukkoDetail_ToM_Product` | `[dbo].[T_SyukkoDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_SyukkoDetail_ToT_Syukko` | `[dbo].[T_SyukkoDetail]` | `[dbo].[T_Syukko]` | `SyID` → `SyID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_Warehousing_ToM_Employee` | `[dbo].[T_Warehousing]` | `[dbo].[M_Employee]` | `EmID` → `EmID` | CASCADE | NO_ACTION | yes / yes |
| `FK_T_Warehousing_ToT_Hattyu` | `[dbo].[T_Warehousing]` | `[dbo].[T_Hattyu]` | `HaID` → `HaID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_WarehousingDetail_ToM_Product` | `[dbo].[T_WarehousingDetail]` | `[dbo].[M_Product]` | `PrID` → `PrID` | NO_ACTION | NO_ACTION | yes / yes |
| `FK_T_WarehousingDetail_ToT_Warehousing` | `[dbo].[T_WarehousingDetail]` | `[dbo].[T_Warehousing]` | `WaID` → `WaID` | NO_ACTION | NO_ACTION | yes / yes |

## Check constraints and other application objects
- FOREIGN_KEY_CONSTRAINT 50
- PRIMARY_KEY_CONSTRAINT 27
- USER_TABLE 27

## Application account dependencies
| Role | Master flag | Accounts | Active accounts | Accounts with a hash |
| --- | ---: | ---: | ---: | ---: |
| 営業 | 0 | 7 | 6 | 7 |
| 物流 | 0 | 6 | 6 | 6 |
| 管理者 | 0 | 2 | 2 | 2 |

## Recorded status and audit counts
| Entity | Business flag | Management flag | Rows |
| --- | --- | --- | ---: |
| 入庫 | 0 | 0 | 4 |
| 入庫 | 1 | 0 | 13 |
| 入荷 | 0 | 0 | 5 |
| 入荷 | 1 | 0 | 14 |
| 出庫 | 0 | 0 | 6 |
| 出庫 | 1 | 0 | 19 |
| 出荷 | 0 | 0 | 5 |
| 出荷 | 1 | 0 | 9 |
| 受注 | 0 | 0 | 8 |
| 受注 | 0 | 2 | 1 |
| 受注 | 1 | 0 | 31 |
| 在庫 | NULL / absent | 0 | 50 |
| 売上 | NULL / absent | 0 | 9 |
| 注文 | 0 | 0 | 6 |
| 注文 | 1 | 0 | 25 |
| 発注 | NULL / absent | 0 | 15 |

- 受注操作履歴 `受注確定` — 31
- 受注操作履歴 `受注非表示` — 1
- 受注操作履歴 `新規受注登録` — 40
- 社員変更履歴 `所属・権限変更` — 2
