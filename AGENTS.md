# AGENTS.md

## この文書の役割

このファイルは、プロジェクト全体で長期間維持する技術基準、責務境界、変更方針を定義する。機能要件は [SPEC.md](SPEC.md)、利用者向けの起動・操作説明は [README.md](README.md) と各プロジェクトの `README.md` に記載する。

## 現在の技術基準

- Visual Studio 2022 / C# / .NET 8 Windows。
- Windows Presentation Foundation（WPF）デスクトップアプリケーション。
- Entity Framework Core 7.0.20 と SQL Server provider。
- SQL Server LocalDB の `SalesManagement` データベースを既存スキーマとして利用する。
- nullable reference types と implicit usings を有効にする。
- UI は日本語を基本とする。
- 現在のソリューションは `SalesManagement_SysDev.sln` と `SalesManagement_SysDev/SalesManagement_SysDev.csproj` で構成する。

元の計画に Windows Forms と記載されている場合でも、現行実装の技術的な基準は WPF とする。大規模な UI 技術変更は、実装前に別途合意する。

## 責務境界

- `F_login` はログイン画面、`F_Main` はアプリケーションシェルとナビゲーションを担当する。
- `OrderManagementWindows.cs` は受注管理、受注登録、受注詳細表示の UI と画面フローを担当する。
- `BusinessService` はログイン、受注確定、在庫・受注に関する業務処理を担当する。
- `Entity` 配下の EF Core エンティティと `SalesManagementContext` は既存データベースとの境界とする。
- UI は表示と入力を担当し、業務上の確定・権限・在庫ルールを UI だけで判断しない。
- 他モジュールのテーブルや内部エンティティを、担当サービスを迂回して直接変更しない。
- 将来モジュールを分割する場合も、Shell、Identity、Catalog、Customers、Employees、Inventory、Orders、Shipping、Sales、Shared Contracts の責務境界を維持する。

## 長期的な設計判断

- 権限は deny-by-default とし、画面上のボタン非表示だけを認可の根拠にしない。
- 拠点スコープを持つデータは、ログイン利用者の許可拠点で必ず絞り込む。
- 受注・売上・在庫などの取引データは物理削除しない。取消、非表示、反対取引、調整、状態変更で訂正する。
- 在庫数量の変更は、在庫移動または業務サービスを通して行い、直接残高を書き換えない。
- 確定処理は二重実行を防ぎ、トランザクション内で関連データを更新する。
- Manager の承認が必要な操作には、実行者、承認者、日時、理由を残す。
- 接続文字列、パスワード、秘密情報をソースコードやリポジトリへ追加しない。
- サンプルデータ投入は再実行可能かつ重複しない処理にする。

## 変更時の標準手順

1. [SPEC.md](SPEC.md) で対象機能と受入条件を確認する。
2. 既存の DB エンティティ、`BusinessService`、`AppSession`、関連画面を確認する。
3. 画面変更と業務処理を分離し、必要な権限・拠点チェックをサービス側でも確認する。
4. `dotnet build SalesManagement_SysDev.sln --no-restore -m:1 -v:minimal` で検証する。実行中のアプリが出力をロックしている場合は、別の `OutDir` と `UseAppHost=false` でコンパイル検証する。
5. 変更内容が利用者向けであれば、該当する `README.md` を更新する。

## 文書の優先順位

技術境界・恒久的な選択は本書、機能の現在要件は `SPEC.md`、利用者向け手順は `README.md` を参照する。文書間に不整合がある場合は、実装変更を始める前に不整合を明示して解消する。
