# 7人の GitHub 開発手順

1つのリポジトリに保護された `main` と7本の担当ブランチを置きます。全員が同じ `SalesManagement_SysDev.sln` を開き、機能担当とファイルの範囲は [モジュール担当表](MODULE_OWNERSHIP.md) で確認します。

## 統合担当の初回セットアップ

1. リポジトリ管理者、統合担当、代理担当をチームで決めます。モォーが統合担当の候補です。既存の親リポジトリを利用できるか、利用する場合の公開範囲と権限を管理者が確認します。
2. [統合ガイドのベースライン準備](INTEGRATION_GUIDE.md#単一リポジトリ用のベースラインを準備する) に従い、親とサブモジュール双方の現在の変更を確認します。作業用クローンに通常のファイルとしてWPFプロジェクトを取り込み、Windowsでビルドします。
3. 承認された既存リポジトリに準備PRを出すか、承認された空のリポジトリへ初回の `main` を公開します。新規リポジトリの公開範囲は事前に決めます。限定共有には、非公開かつブランチ保護を利用できるプランを選びます。
4. `main` を既定ブランチにし、下記の保護を設定します。公開済みの `main` から新規クローンし、サブモジュール取得なしでプロジェクトのソースが揃い、ビルドできることを確認します。
5. 各メンバーのGitHubユーザー名を本人から取得します。個人リポジトリなら **Settings → Collaborators → Add people**、Organizationなら **Settings → Collaborators & teams → Add people** で招待します。Organizationの開発メンバーには `Write` を付与し、各自が招待を承諾します。 [個人リポジトリへの招待](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/inviting-collaborators-to-a-personal-repository) · [Organizationのアクセス管理](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/managing-teams-and-people-with-access-to-your-repository)
6. 承認済みのURL、`main` のベースラインのコミットID、[開発用LocalDBの準備手順](INTEGRATION_GUIDE.md#開発用localdbと起動確認) を7人に共有します。各自が下記の担当ブランチを1本だけ公開した後、GitHub上で7本の名前を照合します。

### main を保護する

GitHubで **Settings → Branches** を開き、ブランチ保護ルールを追加します。パターンは `main` です。GitHub Freeでは公開リポジトリが対象です。非公開の場合は個人のGitHub Proや組織のGitHub Teamなど、ブランチ保護を利用できるプランを招待前に確認します。 [ブランチ保護の提供範囲と設定](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule)

- PRを必須にして、作成者以外の書き込み権限を持つレビュー担当による承認を最低1件要求します。差分が更新された場合に古い承認を無効にし、会話の解決を必須にします。
- 管理者もルールに従う設定を有効にします。強制pushとブランチ削除の許可はオフにします。共有ファイルのPRは機能担当と統合担当の双方が確認します。
- **Settings → General → Pull Requests** でマージコミットを許可し、headブランチの自動削除をオフにします。担当ブランチを繰り返し使うため、統合には **Create a merge commit** を使います。linear historyの必須化はオフにします。 [ブランチ保護の項目](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches) · [自動削除設定](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-the-automatic-deletion-of-branches)
- WindowsのCIを追加する場合は、ビルドジョブを動かしてGitHubにチェック名が出てから、必須ステータスチェックに指定します。それまでPRにWindowsでのビルド結果を記載します。
- PRのマージ操作は統合担当か代理が行います。個人リポジトリでは、この担当者の限定はチームの運用ルールです。組織で権限による限定が必要な場合は、管理者が利用可能なブランチ制限を確認します。 [保護ブランチの権限制限](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches#restrict-who-can-push-to-matching-branches)

## 開発者の初回セットアップ

1. GitHubの招待を承諾し、GitHub Desktopに同じアカウントでサインインします。
2. GitHub Desktopの **File → Clone Repository → URL** に承認済みのURLを入力します。新しいローカルフォルダを選んで **Clone** します。現在の作業コピーから移行する人も、新しいフォルダで始めます。 [GitHub Desktopでクローン](https://docs.github.com/en/desktop/adding-and-cloning-repositories/cloning-and-forking-repositories-from-github-desktop)
3. **Current Branch** で `main` を選び、**Fetch origin**、更新があれば **Pull origin** を実行します。`SalesManagement_SysDev.sln` をVisual Studio 2022で開き、[初回ビルド](INTEGRATION_GUIDE.md#開発用localdbと起動確認) を確認します。
4. **Current Branch → New Branch** で下の担当ブランチ名を入力し、最新の `main` をベースに **Create Branch**、続けて **Publish branch** を選びます。担当ブランチが既にGitHubにある場合は、**Fetch origin** 後にそのブランチを選択します。 [GitHub Desktopでブランチを作成・公開](https://docs.github.com/en/desktop/making-changes-in-a-branch/managing-branches-in-github-desktop)
5. 担当ファイルと [SPEC.md](../SPEC.md) の対応節を開きます。下表のファイルを入口に、関連するViewModel・サービス・チェックへ進みます。

| 開発者 | ブランチ | 最初に開くファイル（`SalesManagement_SysDev/` 内） | 連携時のレビュー相手 |
| --- | --- | --- | --- |
| チョー | `feature/auth-employee` | `F_login.xaml`、`Presentation/LoginViewModel.cs`、`EmployeeManagementView.xaml`、`EmployeeService.cs` | 統合担当 |
| トゥン | `feature/customer-sales` | `CustomerManagementView.xaml`、`CustomerService.cs`、`SalesManagementView.xaml`、`SalesService.cs` | 顧客と受注はモォー、売上連携は重屋 |
| 堀川 | `feature/product-inventory` | `ProductManagementView.xaml`、`ProductService.cs`、`InventoryManagementView.xaml`、`InventoryService.cs` | 受注での商品使用はモォー、在庫増減は秋田・アン |
| モォー | `feature/order-management` | `F_OrderManagement.xaml`、`OrderManagementWindows.cs`、`Presentation/OrderUpdateViewModel.cs` | 注文連携はアン |
| 秋田 | `feature/purchase-receiving` | `PurchaseOrderManagementView.xaml`、`PurchaseOrderService.cs`、`WarehouseMovementService.cs` の入庫側 | 共通画面はアン、在庫は堀川 |
| アン | `feature/ordering-shipping` | `OrderManagementView.xaml`、`ChumonManagementViewModel.cs`、`WarehouseMovementService.cs` の出庫側 | 受注はモォー、共通画面は秋田、入荷生成は重屋 |
| 重屋 | `feature/arrival-delivery` | `ArrivalManagementView.xaml`、`ArrivalService.cs`、`ShipmentManagementView.xaml`、`ShipmentService.cs` | 出庫はアン、売上はトゥン |

## 変更、コミット、PR

1. 作業前に、担当ブランチへ最新の `main` を取り込みます。切り替え前に **Changes** が空であることを確認し、途中の変更は担当ブランチでローカルコミットに保存します。GitHub Desktopで `main` を **Fetch origin → Pull origin** で更新し、担当ブランチへ戻ります。担当ブランチ側にもリモートの更新があれば先にpullします。**Current Branch → Choose a branch to merge into …** で `main` を選び、取り込み先が自分の担当ブランチであることを確認してマージします。 [GitHub Desktopで同期する](https://docs.github.com/en/desktop/working-with-your-remote-repository-on-github-or-github-enterprise/syncing-your-branch-in-github-desktop)
2. Issueかチームの作業表に、変更する機能とファイルを記録します。担当外の契約、共有ファイル、DBの列・フラグを変える場合は、先に [共有ファイルのルール](INTEGRATION_GUIDE.md#共有ファイルを変更するとき) に従います。
3. Visual Studioで担当ファイルを編集し、ローカルでビルドします。対象画面、許可ロール、所属外や権限のないロールでの動作を開発用データで確認します。保存を伴う変更では関連する後続処理と監査履歴も確認します。
4. GitHub Desktopの **Changes** で差分と対象ファイルを確認し、目的が同じファイルを選んで **Commit to …** を押します。コミットの例は「受注更新で保存済み明細の取消を反映」です。**Push origin** で担当ブランチに送ります。
5. GitHubで **Pull requests → New pull request** を開きます。`base: main`、`compare: 担当ブランチ` を選びます。PRに目的、対象画面、変更した共有ファイルやテーブル、実行した確認と結果を書き、担当表からレビュー担当を指定します。共有変更には統合担当も指定します。レビュー中は同じPRに必要な修正を追加し、別の機能はそのPRの統合後に着手します。
6. レビュー担当は **Files changed** で実際の差分を確認し、**Review changes** から **Approve** または **Request changes** を選び、**Submit review** を押します。XAMLペアとリソース、権限と拠点条件、DB更新のトランザクション、不要な生成物・実データの混入を確認します。著者が指摘に対応して再pushしたら、差分を再レビューします。 [GitHubのPRレビュー](https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/reviewing-proposed-changes-in-a-pull-request)
7. 統合担当は承認、会話の解決、ビルド・必要なチェックの結果、`main` との競合がないことを確認し、GitHubで **Create a merge commit** を選びます。**Merge pull request → Confirm merge** で統合し、7本の担当ブランチは保持します。 [GitHubでPRをマージする手順](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/merging-a-pull-request)
8. 統合後、全員が `main` をpullします。次の変更を始める前に、手順1で担当ブランチへ取り込みます。毎日の着手時、共有契約が合流した直後、PR提出前にも同期します。

### 競合が発生した場合

1. 担当ブランチで `main` を取り込んだ場合、Current側は担当ブランチ、Incoming側は `main` です。Desktopの競合ファイル一覧を開き、Visual Studioのマージエディターで両方の変更を比較します。
2. 共有ファイルなら担当者同士で最終形を合意します。XAMLでは `x:Name`・イベントハンドラー・リソース参照、C#ではサービスの呼び出し側・ロール条件・拠点条件・状態更新を対で確認します。双方の必要な変更を残し、競合マーカーを取り除きます。
3. 保存し、Desktopで解決済みファイルを確認してマージを完了します。コマンドで作業する場合は `git status` と `git diff --name-only --diff-filter=U` で残りを確認し、解決したパスだけを `git add -- <path>` して `git merge --continue` を実行します。クリーンな状態から始めたマージを中断して相談するときは `git merge --abort` が使えます。
4. ビルドと対象画面を確認して担当ブランチをpushし、PRの新しい差分を再レビューします。公開済み担当ブランチの更新はマージで行うため、履歴を書き換える操作は不要です。

## CODEOWNERS を有効にする条件

現在の親ではアプリ全体が1つのサブモジュール参照です。内部ファイルを通常ファイルとして取り込んだ後、7人のGitHubユーザー名と `Write` 権限を確認できた時点で `.github/CODEOWNERS` を `main` に追加します。それまでは担当表に従ってPRのReviewersを手動で指定します。

導入時は現在の正確なパスに合わせ、既定の統合担当を先に、個別の機能・共有ファイルの規則を後に置きます。例として `/SalesManagement_SysDev/EmployeeService.cs` はチョー、`/SalesManagement_SysDev/WarehouseMovementService.cs` は秋田・アン、`/SalesManagement_SysDev/Entity/SalesManagementContext.cs` は統合担当とDB担当を指定します。複数のコード所有者を指定しても、そのうち1人の承認でGitHubの所有者レビュー要件を満たし得ます。共同担当の双方確認はチームのレビュー手順で管理します。 [CODEOWNERSの権限とパターン](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)
