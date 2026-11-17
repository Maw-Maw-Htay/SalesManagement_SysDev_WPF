# WPF の統合ガイド

この文書は2026年11月6日の作業ツリーを対象にしています。[モジュール担当表](MODULE_OWNERSHIP.md) と [チームのGitHub手順](TEAM_WORKFLOW.md) と併用してください。

## 現在の構成

- `SalesManagement_SysDev.sln` は Visual Studio 2022 用のソリューションで、アプリは `SalesManagement_SysDev/SalesManagement_SysDev.csproj` の1プロジェクトです。`net8.0-windows7.0`、`UseWPF=true`、nullable、implicit usings を使用しています。EF Core と SQL Server provider はいずれも `7.0.20` です。`tests/` の10個のチェックはアプリを参照する別のコンソールプロジェクトです。
- 確認したアプリのC#ソース124ファイルは同じ `SalesManagement_SysDev` 名前空間です。XAML 49ファイルのうち、39件に `x:Class` と対応するコードビハインドがあり、10件はリソース辞書です。フォルダ名からC#の名前空間を決める構成ではありません。
- 各業務ビューにはXAMLとコードビハインドがあり、多くは `Presentation/` のViewModelや機能別サービスを使います。`F_Main.xaml.cs` は画面生成と遷移、`BusinessWindows.cs` はページの入口とパスワード変更画面、`OrderManagementWindows.cs` は受注の表示・検索・画面フローを担当しています。`ChumonManagementViewModel.cs` はEFの読み取りを直接行い、確定は `BusinessService` に依頼します。受注詳細のコードビハインドにも履歴照会があるため、現状はMVVMと既存のコードビハインドが混在しています。
- `App.xaml` の `StartupUri` は `F_login.xaml` です。起動時のログイン画面は既存DBへの接続を確認します。認証成功時に `LoginViewModel` が `AppSession.Employee` を設定し、`F_login` が `F_Main` を表示します。ホームのショートカットはシェルのナビゲーションへ渡されます。サイドバーはロールに応じて組み立てられ、各ビューを `pageHost` に表示します。

### 画面の入口

| シェルのメニュー | 現在の画面への経路 | メニューが出るロール |
| --- | --- | --- |
| ログイン管理 | `F_Main` の設定パネル → `BusinessService.ChangePassword`。ログイン前は `F_login` の `F_PasswordChange` ダイアログ | ログイン後の設定 |
| 社員管理 | `F_Employee` → `EmployeeManagementView` | 管理者 |
| 顧客管理 | `F_Client` → `CustomerManagementView` | 管理者、営業、物流 |
| 売上管理 | `F_Sale` → `SalesManagementView` | 管理者、営業 |
| 商品管理 | `F_Product` → `ProductManagementView` | 管理者、営業、物流 |
| 在庫管理 | `F_Stock` → `InventoryManagementView` | 管理者、物流 |
| 受注管理 | `F_OrderManagement.DetachView()` → 一覧・履歴、受注登録・詳細・更新 | 管理者、営業 |
| 発注管理 | `F_Hattyu` → `PurchaseOrderManagementView` | 管理者、物流 |
| 入庫管理 | `F_Warehousing` → `WarehouseMovementManagementView(Inbound)` | 管理者、物流 |
| 注文管理 | `F_Chumon` → `OrderManagementView` | 管理者、営業 |
| 出庫管理 | `F_Syukko` → `WarehouseMovementManagementView(Outbound)` | 管理者、物流 |
| 入荷管理 | `F_Arrival` → `ArrivalManagementView` | 管理者、営業 |
| 出荷管理 | `F_Shipment` → `ShipmentManagementView` | 管理者、営業 |

メニュー表示と、検索・保存・確定の権限は別に確認します。サービス側の認可、拠点の絞り込み、編集中の離脱確認をレビュー対象に含めます。

### 取引とDBの境界

- `Entity/SalesManagementContext.cs` が既存SQL Serverスキーマのテーブル、外部キー、列型を定義します。`OnConfiguring` は `(localdb)\MSSQLLocalDB` の `SalesManagement` にWindows認証で接続する構成です。現行の通常起動に環境変数で接続先を切り替える経路は見当たりません。チームでは各自の開発用DBを用意します。
- 受注確定で注文を作成し、注文確定で在庫を減らして出庫を作成します。出庫確定で入荷、入荷確定で出荷を作成します。出荷確定で売上を作成し、条件が一致する既存売上があれば再利用します。発注と入庫は発注明細と入庫の実績を共有し、棚入れ確定で在庫が増えます。各段階の状態、取引データの論理取消、重複防止、拠点、履歴を変えるPRは隣接機能と一緒に確認します。
- `DatabaseInitializer` には `EnsureCreated`、スキーマ補助、シード、受注履歴初期化の経路があります。現行の `F_login → LoginViewModel.PrepareDatabase` は `CanConnect` による接続確認で、アプリの通常起動経路に `DatabaseInitializer.EnsureReady` の呼び出しは見つかりませんでした。READMEの「起動時にマスタ・アカウントや履歴スキーマを準備する」という説明は、現行経路と照合して別途整合させます。初回起動の前に、開発用スキーマとテストアカウントを用意してください。

## 共有ファイルを変更するとき

1. 機能担当がIssueまたはPRで、影響する呼び出し側・画面・リソースキー・テーブルと、必要な確認を書きます。[共有ファイルの一覧](MODULE_OWNERSHIP.md#共同担当と共通ファイル) に従って統合担当と機能担当を指名します。
2. `F_Main`、`AppSession`、`BusinessService`、`BusinessWindows`、`SalesManagementContext`、`DatabaseInitializer`、共通XAMLは、同じ箇所の作業が重ならないよう統合担当が順番を調整します。変更は既存の担当ブランチで小さなPRにし、公開メソッドやリソースキーが変わる場合は互換性のある契約変更を先に合流させます。その後、利用側が `main` を取り込みます。
3. エンティティやDDLは、DB担当と影響機能のレビューを受けます。共通データの更新は現行のサービス境界に沿い、ロール・拠点チェック、取引のトランザクション、再実行、監査履歴を確認します。入庫・出庫の共通コードは秋田とアンが両方向を確認します。
4. UI共通変更では全体の配色やスタイルキー、ナビゲーション、XAML読み込みを確認します。広範な整形やフォルダ移動は機能変更から切り離し、ビルドと画面確認が済んだ差分をレビューします。

### フォルダ整理の判断

初回統合では現在のフォルダ、ファイル名、名前空間、アセンブリ名、プロジェクト参照を維持します。リポジトリを1つにまとめても、アプリのパスは `SalesManagement_SysDev/` のままです。将来、担当表の `Modules/…` と `Shared/` を使用する場合は、次の順で別の整理PRを作ります。

1. 利用先を把握できる機能固有のC#ファイルを1機能ずつ移動候補にします。名前空間と公開契約を維持し、SDKの `Compile` 対象とソリューションの参照を確認します。`Entity/` とDBマッピングは共通の境界として当面そのまま扱います。
2. XAMLを移す場合は `.xaml` と `.xaml.cs` を同時に移し、`x:Class`、`partial` クラス、`clr-namespace`、Build Action、イベントと `x:Name` を確認します。`App.xaml` の `StartupUri` とXAMLに書かれた相対URIは、移動先によって解決先が変わります。
3. 特に `F_OrderRegistration.xaml` と `F_OrderUpdate.xaml` は `Resources/Registration.xaml` と `Resources/QuantityInput.xaml` を相対参照します。`F_OrderDetail.xaml` と `F_OrderManagement.xaml` も個別辞書を参照します。`App.xaml` の辞書読み込み順と `Resources/Theme.xaml` 内の相対参照、ロゴの `/SalesManagement_SysDev;component/Resources/Branding/SalesManagementLogo.png`、プロジェクトの `Resource` / `Content` 指定も維持します。共有辞書と起動画面の移動は、実際に利点がある場合に絞ります。 [WPFのパックURI](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/pack-uris-in-wpf)
4. 1機能ずつクリーンなクローンでビルドし、ログイン画面、シェル、対象画面の遷移とリソースを確認してから次へ進みます。テストの `ProjectReference` は現行のアプリパスを指しているため、アプリプロジェクトやテストプロジェクトの位置を変える場合は相対パスも確認します。

## 単一リポジトリ用のベースラインを準備する

### 調査時のGitの状態

| 作業コピー | ローカルの `main` | 設定済みの `origin` |
| --- | --- | --- |
| 親 `SalesManagement_SysDev_WPF` | `6aaaf79` | `Maw-Maw-Htay/SalesManagement_SysDev_WPF` |
| 子 `SalesManagement_SysDev/` | `2df9ad0` | `Maw-Maw-Htay/SalesManagement_SysDev` |

親の `.gitmodules` は子を指し、親のインデックスのモードは `160000`、参照は `2df9ad0` です。親の初回コミット `d0fccad` には以前のアプリが通常のフォルダとして入っています。調査時は、両方の `main` がそれぞれのローカルに記録済みの `origin/main` と一致し、親のローカル参照に担当ブランチはありませんでした。GitHub上のリポジトリの存在、最新コミット、公開範囲、保護設定、管理権限は公開作業時に確認します。

ガイド作成前の子には追跡済みファイル42件のうち変更済み17件と、無視対象を除く未追跡ファイル173件がありました。親にも `README.md`、`SPEC.md` の更新と未追跡のチェックや資料があります。親だけをコミットしたり、現在の `HEAD` だけをアーカイブしたりすると、作業中のアプリは収録されません。

### 作業用クローンを組み立てる

統合担当がチームで収録内容を確認した後に実施する手順です。元の親と子は、作業中のファイルを含めてローカルで保全します。以下はWindows PowerShellで、既存の作業コピーとは別の空いているフォルダ名を使います。

1. 収録候補を親と子で別々に取得します。

   ```powershell
   $source = 'C:\Projects\SalesManagement_SysDev_WPF'
   $stage = 'C:\Projects\SalesManagement_SysDev_WPF_team_staging'
   $review = 'C:\Projects\SalesManagement_SysDev_WPF_team_review'

   New-Item -ItemType Directory -Path $review -ErrorAction Stop | Out-Null
   git -C $source -c core.quotePath=false status --short --branch
   git -C "$source\SalesManagement_SysDev" -c core.quotePath=false status --short --branch
   git -C $source -c core.quotePath=false ls-files --cached --others --exclude-standard |
       Set-Content -LiteralPath "$review\root.candidates.txt" -Encoding utf8
   git -C "$source\SalesManagement_SysDev" -c core.quotePath=false ls-files --cached --others --exclude-standard |
       Set-Content -LiteralPath "$review\app.candidates.txt" -Encoding utf8
   ```

2. 候補からレビュー済みの相対パスを1行ずつ `root.approved.txt` と `app.approved.txt` に保存します。親側の一覧から `.gitmodules` と `SalesManagement_SysDev` のgitlinkを外します。ソリューション、プロジェクト、既存XAML、リソース、実際に使うテスト、ガイドを含めます。`_dfd_render/` やCSV等の資料は配布可否を確認してから追加します。既存のシード処理・READMEのサンプル認証情報、接続設定、画像・CSV、共有する履歴を確認します。サブモジュール内では子の `.gitignore` が適用されるため、親の除外規則は通常フォルダに取り込んだ後に効きます。追跡済みのファイルや過去コミットもレビュー対象です。
3. 作業用クローンで、アプリへの1件のgitlinkを通常ファイルに置き換える準備をします。`git ls-files --stage` で対象を確認し、他のサブモジュールがある場合は定義ファイルの削除手順を見直します。

   ```powershell
   if (Test-Path -LiteralPath $stage) { throw 'Choose an unused staging directory.' }
   git clone --no-local --no-recurse-submodules -- $source $stage
   if ($LASTEXITCODE -ne 0) { throw 'Clone failed.' }

   git -C $stage switch -c setup/team-baseline
   if ($LASTEXITCODE -ne 0) { throw 'Could not create the staging branch.' }
   $gitlinks = @(git -C $stage ls-files --stage | Where-Object { $_ -match '^160000 ' })
   if ($gitlinks.Count -ne 1 -or $gitlinks[0] -notmatch '\sSalesManagement_SysDev$') {
       throw 'Check the submodule layout before proceeding.'
   }
   git -C $stage rm --cached -- SalesManagement_SysDev
   if ($LASTEXITCODE -ne 0) { throw 'Could not stage gitlink removal.' }
   git -C $stage rm -- .gitmodules
   if ($LASTEXITCODE -ne 0) { throw 'Could not stage .gitmodules removal.' }
   ```

4. 承認したファイルだけをコピーしてステージします。ソース側で削除があった場合は、一覧から黙って落とさず削除の差分としてレビューします。

   ```powershell
   function Copy-ApprovedPaths($sourceDir, $targetDir, $manifest, $prefix) {
       foreach ($line in Get-Content -LiteralPath $manifest -Encoding utf8 -ErrorAction Stop) {
           $relative = $line.Trim().Replace('\', '/')
           if (-not $relative) { continue }
           if ([IO.Path]::IsPathRooted($relative) -or
               $relative -match '(^|/)(\.\.?|\.git)(/|$)' -or
               $relative -eq '.gitmodules' -or
               ($prefix -eq '' -and $relative -match '^SalesManagement_SysDev(/|$)')) {
               throw "Unexpected manifest path: $relative"
           }
           $from = Join-Path $sourceDir $relative
           $to = Join-Path $targetDir $relative
           if (-not (Test-Path -LiteralPath $from -PathType Leaf)) {
               throw "Missing approved file: $from"
           }
           [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to)) | Out-Null
           Copy-Item -LiteralPath $from -Destination $to -ErrorAction Stop
           "$prefix$relative"
       }
   }

   $approvedFiles = @(
       Copy-ApprovedPaths $source $stage "$review\root.approved.txt" ''
       Copy-ApprovedPaths "$source\SalesManagement_SysDev" "$stage\SalesManagement_SysDev" "$review\app.approved.txt" 'SalesManagement_SysDev/'
   )
   git -C $stage --literal-pathspecs add -- $approvedFiles
   if ($LASTEXITCODE -ne 0) { throw 'Check the approved files and ignore rules.' }
   git -C $stage diff --cached --name-status
   git -C $stage diff --cached --check
   git -C $stage ls-files --stage -- SalesManagement_SysDev/SalesManagement_SysDev.csproj
   git -C $stage ls-files --stage | Select-String '^160000 '
   ```

5. プロジェクトファイルが通常ファイルのモード `100644` で、最後の確認にgitlinkが出ないことを確認します。ステージされた差分とアプリの収録一覧を元の作業コピーと照合します。作業用クローンのルートで下記のビルドを行い、起動・ナビゲーションも確認してから、承認済みのベースラインをコミットします。

   ```powershell
   Push-Location -LiteralPath $stage
   try {
       dotnet restore SalesManagement_SysDev.sln
       if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
       dotnet build SalesManagement_SysDev.sln --no-restore -m:1 -v:minimal
       if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
   } finally {
       Pop-Location
   }
   git -C $stage status --short
   # 差分とビルドをレビューした後に実行
   git -C $stage commit -m 'Prepare WPF project as a single-repository baseline'
   ```

この方法は親の既存履歴にアプリの現在のファイルを取り込み、元の子リポジトリの履歴は保全した作業コピーで参照できます。作業用クローンの `origin` はローカルの親を向くため、公開先は別名 `github` として確認します。

### 承認後の公開手順

- **既存の親を採用する場合**。管理者が既存リポジトリを開いてアクセス・公開範囲を確認し、`main` を保護します。承認されたURLを `$approvedUrl` に設定して下のコマンドで照合します。ローカルの準備元 `main` と `github/main` が異なる場合は、リモートの最新 `main` から別の作業用クローンを作ります。両方で変わったパスを担当者と比較し、最新 `main` の変更も反映したベースラインを確認します。準備ブランチをpushし、GitHubで `setup/team-baseline → main` のPRを作成します。

  ```powershell
  $approvedUrl = 'https://github.com/OWNER/REPOSITORY.git' # 承認済みのURLに置き換える
  git -C $stage remote add github $approvedUrl
  git -C $stage remote -v
  git -C $stage fetch github
  if ($LASTEXITCODE -ne 0) { throw 'Fetch failed.' }
  $publishedBase = git -C $stage rev-parse --verify github/main
  if ($LASTEXITCODE -ne 0 -or $publishedBase -ne (git -C $stage rev-parse main)) {
      throw 'The remote main does not match the reviewed base.'
  }
  git -C $stage push -u github setup/team-baseline
  ```

- **新しいリポジトリを採用する場合**。管理者がGitHubの **New repository** で承認された所有者・名前・公開範囲を選び、README・ライセンス・gitignoreの自動初期化をせずに作成します。`git -C $stage remote add github $approvedUrl`、`git -C $stage remote -v`、`git -C $stage ls-remote github` でURLと空のリポジトリを確認し、承認済みコミットを `git -C $stage push github HEAD:refs/heads/main` で最初の `main` として公開します。その後すぐに保護を設定します。履歴の共有承認が得られない場合は、承認済みコミットからファイルだけをエクスポートし、新しいローカルフォルダでレビュー済みの初回コミットを作る方法を選びます。 [既存コードをGitHubに追加する手順](https://docs.github.com/en/migrations/importing-source-code/using-the-command-line-to-import-source-code/adding-locally-hosted-code-to-github)
- **公開後**。別のフォルダへ普通にクローンし、`main` のソリューションとWPFプロジェクトの実ファイル、ビルド、開発用DBでの起動を確認します。チームへ渡すURLはこの1つです。招待・ブランチ作成は [チームのGitHub手順](TEAM_WORKFLOW.md) に従います。統合後に問題が発覚した場合は、原因を分離し、修正またはrevertのPRで解決します。

## 開発用LocalDBと起動確認

1. WindowsにVisual Studio 2022の.NETデスクトップ開発ワークロード、.NET 8のSDK/デスクトップ実行環境、SQL Server Express LocalDBを用意します。開発メンバーごとの `MSSQLLocalDB` に、DB担当が承認したスキーマと合成データを持つ `SalesManagement` を用意します。DBファイルとテスト認証情報は承認済みの別経路で扱います。
2. クローン直後の初回はルートで `dotnet restore SalesManagement_SysDev.sln`、続けて `dotnet build SalesManagement_SysDev.sln --no-restore -m:1 -v:minimal` を実行します。実行中のアプリが出力をロックしている場合は、`-p:OutDir=build_verify/team-collaboration/ -p:UseAppHost=false` を付けてコンパイル確認します。
3. 開発PCで接続とスキーマの一部を読み取りで確認します。

   ```powershell
   SqlLocalDB info MSSQLLocalDB
   sqlcmd -S '(localdb)\MSSQLLocalDB' -E -d 'SalesManagement' -l 5 -b -Q "SET NOCOUNT ON; SELECT DB_NAME() AS db, COL_LENGTH(N'dbo.M_Employee', N'EmPasswordHash') AS employee_hash_column, COL_LENGTH(N'dbo.T_Order', N'OrDeliveryDate') AS order_delivery_column, OBJECT_ID(N'dbo.T_EmployeeAssignmentHistory', N'U') AS employee_history_table, OBJECT_ID(N'dbo.T_OrderOperationHistory', N'U') AS order_history_table;"
   ```

   接続できない場合はインスタンス名とLocalDBの状態を確認し、開発用インスタンスを起動して再試行します。列・テーブルの確認結果が `NULL` の場合は、DB担当が開発用スキーマを確認します。既存スキーマの準備を行う `DatabaseInitializer` の処理にはDDLやデータ投入もあるため、適用範囲を確認した開発用DBで扱います。
4. `SalesManagement_SysDev` をスタートアッププロジェクトにして、開発用DBでF5起動します。ログイン成功時に旧形式の認証情報が更新される場合があるため、開発用アカウントで確認します。接続結果、入力エラー、正常ログイン、ログアウト、メニューとホームの遷移を確認します。上表のロールを使い、表示対象、所属営業所の範囲、権限のない操作の拒否を確認します。受注では一覧→詳細→更新→一覧、編集中の離脱、明細取消と履歴を確認します。連携を変えた場合は、関係する確定処理と二重実行時の挙動も開発用データで確認します。

### この作業での確認結果

| 確認 | 結果 |
| --- | --- |
| ソリューションのビルド | 別出力先で成功。警告0件、エラー0件。 |
| XAMLとC#の対応 | XAML 49件、`x:Class` とコードビハインドの一致39件、辞書10件。検出した18件の起動・辞書・画像のパス参照は存在を確認。 |
| 業務チェック | 社員、顧客、売上、商品、発注、倉庫移動、入荷、出荷の既存8プログラムが成功。計258項目。 |
| 受注WPFチェック | `OrderUpdateChecks` は334項目、`OrderHideChecks --details-only` は155項目に成功。バインディングエラー0件。DB統合はスキップ。 |
| 起動・ナビゲーション | 起動URI、ログイン成功時のシェル表示、ロール別メニューと画面の生成先をソースで確認。実DBによるログインと全画面の操作は未確認。 |
| LocalDB | ツールは存在。インスタンス情報の取得でレジストリ構成のエラーが出て、読み取りの `sqlcmd` 接続はタイムアウト。接続先の実スキーマは未確認。 |

チェックはコンソールプロジェクトなので、リポジトリ直下から `dotnet run --project tests/EmployeeManagementChecks -p:UseAppHost=false` のように担当表のプロジェクトを指定します。`OrderHideChecks` と `OrderUpdateChecks` のDB統合モードは専用DBを作成・削除します。今回の受注画面チェックは次の引数で実行しました。

```powershell
dotnet run --project tests/OrderUpdateChecks -p:UseAppHost=false -- --skip-database build_verify/team-collaboration/order-update-preview
dotnet run --project tests/OrderHideChecks -p:UseAppHost=false -- --skip-database --details-only
```
