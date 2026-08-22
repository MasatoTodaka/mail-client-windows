# Windows向けMailアプリ（macOS Mail風 UI）実装計画

## Context

ユーザーはWindows専用のメールクライアントを新規開発したい。見た目・使い勝手はmacOS純正Mailアプリ（3ペイン構成: サイドバー／メッセージ一覧／閲覧ペイン）に寄せる。技術選定と機能範囲はユーザーとの対話で以下に確定済み:

- **技術スタック**: WinUI 3 (.NET 8, Windows App SDK) + C#
- **MVPの機能範囲**: 複数アカウント対応の基本メール機能（受信/一覧/既読/送信/返信/転送/削除/移動/フォルダツリー） + 全文検索 + デスクトップ通知
- **アカウント種別**: 汎用IMAP/SMTP（ホスト/ポート/ユーザー名/パスワード/SSLを手動入力、macOS Mailの「その他のメールアカウント」相当）。OAuth/Gmail API/Graph APIはMVP対象外
- **ビルド環境の制約**: 開発端末はMacのため、WinUI 3のビルド・実機確認はできない。ソースコード一式をここで作成し、ユーザーが自身のWindows機（Visual Studio）でビルド・検証する運用で進めることを確認済み

これはグリーンフィールドプロジェクト（既存コードなし）。既存パターンの調査は不要で、新規アーキテクチャ設計がそのままプランとなる。

---

## 1. プロジェクト構成

.NET 8 マルチプロジェクトのソリューション。WinUI依存を持つ層と持たない層を分離し、ロジック部分はMac上でも`dotnet test`できるようにする。

```
MailClient.sln
├── src/
│   ├── MailClient.App/            # WinUI 3 ヘッド (packaged, net8.0-windows10.0.19041.0)
│   │   ├── App.xaml(.cs)          # DIホスト起動、通知アクティベーション
│   │   ├── MainWindow.xaml(.cs)   # 3ペインシェル + Micaバックドロップ
│   │   ├── Views/Shell/           # Sidebar / MessageList / ReadingPane
│   │   ├── Views/Compose/         # ComposeWindow（独立AppWindow、Mail同様複数同時起動可）
│   │   ├── Views/AccountSetup/    # AddAccountDialog
│   │   └── Views/Controls/        # MessageRowControl, FolderTreeItem 等
│   ├── MailClient.ViewModels/     # net8.0、CommunityToolkit.Mvvm、WinUI非依存
│   ├── MailClient.Core/           # net8.0、Models + Abstractions（インターフェースのみ）
│   ├── MailClient.Data/           # net8.0、Microsoft.Data.Sqlite + FTS5（EF Coreは使わない）
│   ├── MailClient.Mail/           # net8.0、MailKit統合（IMAP/SMTP/同期エンジン）
│   ├── MailClient.Platform/       # net8.0-windows、Credential Locker / AppNotificationManager
│   └── MailClient.Infrastructure/ # DI合成ルート
└── tests/
    ├── MailClient.Core.Tests/
    ├── MailClient.Data.Tests/     # SQLite/FTS5、Mac上でも実行可
    ├── MailClient.Mail.Tests/     # GreenMail等に対する結合テスト
    └── MailClient.ViewModels.Tests/
```

**理由**: `Core`/`Data`/`Mail`/`ViewModels`は`net8.0`ターゲットのため、Macでも`dotnet build`/`dotnet test`が通り、ロジックの大半をWindows機に持ち込む前に検証できる。Windows専用API（Credential Locker、AppNotificationManager）は`Platform`に隔離。

パッケージ形態は **MSIX (Packaged)** を採用（`Windows.Security.Credentials.PasswordVault`がパッケージ済みアプリで安定動作するため）。

## 2. コアライブラリ選定

| 用途 | 選定 | 理由 |
|---|---|---|
| IMAP/SMTP | **MailKit** (MIT) | 業界標準、`ImapClient.IdleAsync`でIDLE対応、商用利用も問題なし |
| ローカルDB/全文検索 | **Microsoft.Data.Sqlite + FTS5**（生SQL、EF Coreは不使用） | FTS5の`MATCH`/`bm25()`/`snippet()`はEF Coreで一級サポートされずどのみち生SQLになるため、素直にADO.NETで書く。FTS5モジュール同梱の確認をM1で最優先スパイクする |
| 認証情報保存 | `ICredentialStore`抽象 + 既定実装は **Windows Credential Locker (PasswordVault)** | パスワードは絶対にSQLite/JSONに平文保存しない |
| 通知 | **AppNotificationManager** (Windows App SDK) | 新着メールのトースト通知、クリックで該当メッセージへ遷移 |

**IMAP IDLE設計**: アカウントごとに専用の「IDLE用コネクション」を1本保持し、9分ごとに再発行。対話的なfetch/move/deleteは別の「作業用コネクション」で行い、IDLE中のコネクションと干渉させない。IDLE非対応サーバーは`ImapCapabilities.Idle`を見て5分間隔ポーリングにフォールバック。

## 3. データモデル（`MailClient.Core/Models`）

`Account`（パスワードは持たない）、`MailFolder`（`UidValidity`/`UidNext`/`UnreadCount`）、`MailMessage`（`Uid`/`MessageId`/フラグ/`IsBodyDownloaded`/本文は別ファイル保存）、`MailAttachment`、`FolderSyncState`、`OutboxAction`（オフライン操作キュー）。

IMAP整合性の要点: **UIDVALIDITY**が変わったらフォルダ全体を再同期、**UIDNEXT**で差分取得、対応サーバーでは**CONDSTORE/HIGHESTMODSEQ**でフラグ変更を安価に検出。

## 4. 同期エンジン

- **初回同期**: 全履歴を一括DLしない。まずINBOXの直近N件（既定50件）のヘッダーのみ取得しUIを即座に使えるようにし、他フォルダ・過去分はバックグラウンド/スクロール時に段階的に取得
- **本文**: オンデマンド取得（閲覧ペインで開いた時のみDL、`IsBodyDownloaded`で管理）
- **差分同期**: 再起動・再接続時はUIDVALIDITY確認 → `UID FETCH lastUid+1:*`で新着 → 直近分のフラグ再確認
- **ライブ更新**: `ImapIdleWatcher`がアカウントごとにIDLEループ or ポーリングを管理し、`MessageArrived`イベントをViewModel/通知層に伝播
- **オフラインキュー**: 既読/フラグ/移動/削除/送信はすべてローカルDB即時反映 + `OutboxAction`をエンキューし、`OutboxProcessor`が再接続時にリプレイ（失敗時はリトライ+バックオフ、サイドバーに「N件同期待ち」表示）

## 5. UI設計（macOS Mail → WinUI 3対応）

| Mac Mail要素 | WinUI 3実装 |
|---|---|
| 3ペイン全体 | `NavigationView`は使わずカスタム`Grid`+`GridSplitter`。左=カスタムサイドバー、中=メッセージ一覧、右=閲覧ペイン |
| サイドバー（アカウント→フォルダツリー、未読バッジ） | `TreeView` + `InfoBadge`風の未読数ピル |
| メッセージ一覧 | `ListView` + カスタム`MessageRowControl`（未読ドット、送信者、件名、スニペット、日時、フラグ星）。スクロール末尾でページ追加読み込み |
| 閲覧ペイン | **WebView2**でHTML本文をサニタイズ後`NavigateToString`。リモート画像は既定でブロックし「画像を読み込む」導線を用意（Mac Mailと同じプライバシー挙動）。プレーンテキストは`TextBlock` |
| ツールバー | アイコンのみの`CommandBar`（作成/返信/全員に返信/転送/削除/アーカイブ/検索） |
| 検索 | `AutoSuggestBox`でデバウンス検索 → FTS5検索、結果はフォルダ横断のフラットリスト |
| 作成ウィンドウ | 独立`AppWindow`（モーダルにしない。Mac Mailのように複数同時作成可能） |
| ライト/ダーク・Mica | `MicaBackdrop`、テーマはWindows設定に追従（明示的に固定しない） |

## 6. マイルストーン（各回Windows機で1回のビルド・検証で完結する粒度）

| # | 内容 | 検証方法 |
|---|---|---|
| M0 | 空のWinUI3シェル（3ペインのプレースホルダ、Mica、DIホスト） | `dotnet build`成功、明暗テーマ追従を確認 |
| M1 | SQLite+FTS5スキーマとスパイク | FTS5の`no such module`エラーが出ないこと、ダミーデータでMATCH検索が動くこと（Mac上のdotnet testでも先行確認可） |
| M2 | Credential Locker + アカウント追加フォーム（通信なし） | 再起動後もアカウント情報とパスワードが復元できること、資格情報マネージャーにエントリが見えること |
| M3 | IMAP接続 + フォルダ一覧（GreenMail/hMailServerテストサーバー相手） | サイドバーに実フォルダツリーが表示、誤資格情報時のエラー表示 |
| M4 | 初回ヘッダー同期 + メッセージ一覧 | 60件シードして最初は約50件のみロードされること、送信者/件名/日時/未読状態が一致 |
| M5 | 閲覧ペイン + オンデマンド本文取得 | HTML本文のリモート画像が既定でブロックされ、クリックで表示できること。開封で未読数が減ること |
| M6 | 返信/転送/削除/移動/フラグ + Outboxキュー | サーバー停止中に操作→UI即時反映→再接続でキュー消化・サーバー状態と一致 |
| M7 | SMTP送信 + Outbox経由送信 | テストサーバー内の別アカウントへ送信し受信確認、送信済みフォルダにも反映、オフライン作成→再接続送信 |
| M8 | IMAP IDLE + ポーリングフォールバック | 別経路でメール投入→数秒でアプリに反映。IDLE非対応時はポーリング間隔内に反映 |
| M9 | 全文検索UI | フォルダ横断でキーワード検索が正しい結果を返す |
| M10 | デスクトップ通知 | バックグラウンド時に新着トースト表示、クリックで該当メッセージへ遷移 |
| M11 | 仕上げ（複数アカウントの未読集計、設定画面、エラーUX、初回起動体験） | 手動UXウォークスルー |

スレッド表示（会話ビュー）はコストが低ければM9.5として任意追加、時間がかかるようならMVPから外す。

## 7. マイルストーンごとの主要ファイル

- M0: `MailClient.App/App.xaml.cs`, `MainWindow.xaml`, `MailClient.Infrastructure/ServiceCollectionExtensions.cs`
- M1: `MailClient.Data/MailDbContext.cs`, `Migrations/0001_init.sql`, `Migrations/0002_fts.sql`, `Search/FtsSearchIndex.cs`
- M2: `MailClient.Platform/CredentialLockerStore.cs`, `Views/AccountSetup/AddAccountDialog.xaml(.cs)`, `AddAccountViewModel.cs`
- M3: `MailClient.Mail/Imap/ImapAccountClient.cs`, `Repositories/FolderRepository.cs`, `Views/Shell/SidebarView.xaml(.cs)`
- M4: `MailClient.Mail/Sync/MailSyncService.cs`, `Repositories/MessageRepository.cs`, `Views/Controls/MessageRowControl.xaml`
- M5: `Views/Shell/ReadingPaneView.xaml(.cs)`, `ImapAccountClient.FetchBodyAsync`
- M6: `Core/Models/OutboxAction.cs`, `Repositories/OutboxRepository.cs`, `Mail/Sync/OutboxProcessor.cs`, `Views/Compose/ComposeWindow.xaml(.cs)`
- M7: `Mail/Smtp/SmtpSender.cs`, `ViewModels/Compose/ComposeViewModel.cs`
- M8: `Mail/Imap/ImapIdleWatcher.cs`, `Core/Events/MessageArrivedEventArgs.cs`
- M9: `ViewModels/Search/SearchViewModel.cs`, ツールバーの`AutoSuggestBox`
- M10: `MailClient.Platform/AppNotificationService.cs`, `App.xaml.cs`の`NotificationInvoked`ハンドラ

最重要ファイル（バグると波及範囲が大きい）:
- `MailSyncService.cs`（同期の正しさが全機能の前提）
- `ImapIdleWatcher.cs`（並行処理が最も難しい箇所）
- `Migrations/0002_fts.sql`（FTS5可用性はビルド環境依存の最大リスク、M1で最優先確認）
- `OutboxRepository.cs` / `OutboxProcessor.cs`（オフライン要件の要）
- `CredentialLockerStore.cs`（パッケージングとの組み合わせでM2早期に検証必須）

## 8. 検証方針

このMac環境ではビルド・実行不可のため、各マイルストーンごとに: ここでコード作成 → ユーザーがWindows機にコピー/pull → 下記手順で確認、というサイクルで進める。

1. **ツールチェーン準備**（初回のみ）: Visual Studio 2022 (.NET Desktop Development + Windows App SDK ワークロード) または `dotnet` CLI 8.0.x。`dotnet build MailClient.sln`で確認
2. **テスト用メールサーバー**: GreenMail（Docker、リセットが容易）またはhMailServer（Windowsネイティブ）のどちらかを用意し、テストメールボックスに既読/未読/フラグ混在の50〜60件、HTML+リモート画像のメール1通、プレーンテキスト1通をシード
3. 各マイルストーンの表内「検証方法」を実施（5〜15分程度の手動確認）
4. `MailClient.Core.Tests` / `MailClient.Data.Tests` / `MailClient.ViewModels.Tests` はWindows不要のため**このMac上でも**`dotnet test`で先行実行可能。`MailClient.Mail.Tests`（IMAP/SMTP結合）と`MailClient.App`/`MailClient.Platform`はWindows機が必要
5. MVP完了後、任意で実アカウント（iCloud等のアプリ用パスワード対応プロバイダ。MVPはOAuth非対応のためGmailは不向き）に接続する最終確認を実施。まず閲覧のみで様子を見てから削除/移動を試す
