# Demo relationship and stock reconciliation

Catalog `SalesManagement_Demo`, scenario date 2026-11-16 Japan time.

- SQL Server checked all constraints, and all 50 foreign keys are enabled and trusted.
- Five offices, 30 customers, ten makers and 50 products match the plan. All 14 active logins and the disabled-account rejection were verified.
- 40 受注, 31 注文, 25 出庫, 19 入荷, 14 出荷 and 9 売上 match their unique parents, branches, quantities, statuses and business dates.
- Stored order and sale amounts match the fixture unit prices. All 40 受注 detail view models match the expected net subtotal, 10% yen-rounded tax, and total.
- 72 order events and two employee assignment events have recorded relationships and explicit demo labels.
- 15 発注 and 17 入庫 have matching maker, product and purchase IDs. Unposted receipts remain pending.
- All 50 independent product balances reconcile to the documented opening quantities, confirmed receipts, and confirmed 注文. No duplicate or negative balances.
- Management queries loaded the expected master and transaction rows. Purchase status counts are 5 waiting, 4 partial, and 6 received.
- Inventory shows 44 normal, 3 low and 2 zero balances. Inventory and audit pagination, timeline, business history, and sales office restrictions passed.

## Product opening and closing balances

Confirmed receipts add stock and 注文確定 deducts it. Opening + received − deducted = closing.

| Model | Product | Opening | Received | Deducted | Closing | Safety |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| AN-D510 | 法人向けスリムデスクトップ Core i5 16GB | 58 | 17 | 16 | 59 | 8 |
| AN-D730 | 設計部門向けタワーPC Core i7 32GB | 42 | 0 | 0 | 42 | 5 |
| AN-M320 | 省スペースミニPC 8GB 256GB | 73 | 16 | 0 | 89 | 10 |
| AN-A240 | 受付用23.8型一体型デスクトップ | 40 | 0 | 0 | 40 | 6 |
| AN-W900 | 業務用ワークステーション 64GB | 31 | 0 | 0 | 31 | 4 |
| SH-N141 | 14型法人モバイルノート 16GB 512GB | 68 | 18 | 15 | 71 | 10 |
| SH-N156 | 15.6型事務用ノートPC テンキー付き | 59 | 0 | 0 | 59 | 10 |
| SH-N133 | 13.3型軽量ノートPC USB-C給電 | 44 | 12 | 0 | 56 | 8 |
| SH-N145 | 14型タッチ対応コンバーチブルPC | 40 | 0 | 0 | 40 | 6 |
| SH-N170 | 17.3型業務用ノートPC 32GB | 32 | 0 | 0 | 32 | 5 |
| YN-L240 | 24型フルHD液晶ディスプレイ | 87 | 19 | 15 | 91 | 12 |
| YN-L270 | 27型QHD液晶モニター 高さ調節対応 | 65 | 0 | 0 | 65 | 9 |
| YN-L280 | 28型4K液晶モニター USB-C対応 | 48 | 15 | 0 | 63 | 7 |
| YN-L150 | 15.6型モバイル液晶モニター | 49 | 0 | 0 | 49 | 8 |
| YN-L340 | 34型ウルトラワイドモニター | 40 | 0 | 0 | 40 | 5 |
| AH-P410 | A4モノクロレーザープリンター 両面印刷 | 51 | 14 | 14 | 51 | 7 |
| AH-P600 | A4カラー複合機 有線LAN対応 | 42 | 0 | 0 | 42 | 5 |
| AH-S300 | 業務用両面ドキュメントスキャナー | 44 | 11 | 0 | 55 | 6 |
| AH-P730 | A3カラーレーザープリンター | 32 | 0 | 0 | 32 | 4 |
| AH-L220 | 感熱式ラベルプリンター USB接続 | 46 | 0 | 0 | 46 | 6 |
| KJ-K110 | 日本語109配列USBキーボード | 92 | 15 | 14 | 93 | 20 |
| KJ-K260 | 静音ワイヤレスキーボード | 77 | 0 | 0 | 77 | 15 |
| KJ-M105 | 有線光学式マウス 3ボタン | 106 | 14 | 18 | 102 | 22 |
| KJ-M250 | 静音ワイヤレスマウス 5ボタン | 90 | 0 | 18 | 72 | 18 |
| KJ-T200 | USBテンキーパッド 19キー | 69 | 0 | 0 | 69 | 12 |
| MK-S081 | 8ポート法人向けギガビットスイッチ | 62 | 25 | 20 | 67 | 10 |
| MK-S241 | 24ポート管理型PoE+スイッチ | 46 | 0 | 0 | 46 | 6 |
| MK-R620 | 小規模拠点向けVPNルーター | 50 | 15 | 0 | 65 | 7 |
| MK-A610 | 天井設置型Wi-Fi 6アクセスポイント | 62 | 0 | 20 | 42 | 8 |
| MK-C305 | Cat6A LANケーブル 5m | 110 | 0 | 20 | 90 | 25 |
| HY-S100 | 業務用SATA SSD 1TB | 78 | 7 | 18 | 67 | 15 |
| HY-N200 | NVMe SSD 2TB ヒートシンク付き | 68 | 0 | 0 | 68 | 10 |
| HY-H400 | 外付けバックアップHDD 4TB | 58 | 7 | 17 | 48 | 10 |
| HY-U064 | USBメモリ 64GB キャップ式 | 120 | 0 | 62 | 58 | 30 |
| HY-N402 | 2ベイ小規模オフィス向けNAS | 43 | 0 | 16 | 27 | 6 |
| HZ-D650 | USB-Cドッキングステーション 65W | 79 | 7 | 14 | 72 | 12 |
| HZ-H420 | 4ポートUSB 3.2ハブ | 84 | 0 | 0 | 84 | 14 |
| HZ-A090 | 法人ノート向けUSB-C充電器 90W | 64 | 5 | 0 | 69 | 13 |
| HZ-V101 | モニターアーム クランプ式 | 60 | 0 | 0 | 60 | 8 |
| HZ-C203 | HDMIケーブル 4K対応 3m | 97 | 0 | 17 | 80 | 22 |
| NS-C108 | 会議用フルHDウェブカメラ | 62 | 8 | 17 | 53 | 10 |
| NS-S320 | USB会議用スピーカーフォン | 50 | 0 | 16 | 34 | 8 |
| NS-H200 | 片耳USBヘッドセット マイク付き | 79 | 6 | 15 | 70 | 15 |
| NS-U750 | 事務所向け無停電電源 750VA | 47 | 0 | 0 | 47 | 7 |
| NS-B110 | 受付用一次元バーコードリーダー | 4 | 0 | 0 | 4 | 8 |
| TN-T410 | A4モノクロ機用交換トナー 3000枚 | 4 | 0 | 0 | 4 | 12 |
| TN-L056 | 感熱ラベルロール 56mm 6巻 | 0 | 0 | 0 | 0 | 8 |
| TN-A450 | 上質コピー紙 A4 500枚×5冊 | 0 | 0 | 0 | 0 | 12 |
| TN-C200 | 複合機用メンテナンスキット | 2 | 0 | 0 | 2 | 7 |
| TN-L038 | 旧型向け感熱ラベル 38mm | 0 | 0 | 0 | 0 | 4 |
| **Total** | | **2754** | **231** | **362** | **2623** | |
