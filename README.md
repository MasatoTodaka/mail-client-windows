# Mail Client (Windows / WinUI 3)

macOS純正Mailアプリ風の3ペインUI（サイドバー／メッセージ一覧／閲覧ペイン）を持つ、汎用IMAP/SMTP対応のWindowsデスクトップメールクライアント。

実装計画の全文は `../.claude/plans/distributed-juggling-teapot.md`（このリポジトリ作成時点のプランファイル）を参照。以下はその要約と、Windows機での実行手順。

## 現在の状態

- **M0（このコミット時点で完了）**: 空のWinUI 3シェル。3ペインのプレースホルダ、DIホスト（`Microsoft.Extensions.Hosting`）、Micaバックドロップ、ソリューション一式（Core / ViewModels / Data / Mail / Platform / Infrastructure / App）
- M1以降（SQLite+FTS5、アカウント設定、IMAP同期、送受信、IDLE、検索、通知）は未実装。マイルストーン一覧はプランファイルの §6 を参照

## ビルド方法（Windows機で実施）

このリポジトリはMac上で作成されているため、ビルド・実行確認は未実施です。Windows機で以下を行ってください。

1. **前提ツール**: Visual Studio 2022 (17.10+) — ワークロード「.NET デスクトップ開発」+「Windows App SDK C# テンプレート」。または `dotnet` CLI 8.0.x + Windows App SDK
2. リポジトリを Windows機にコピー/クローン
3. `MailClient.sln` を Visual Studio で開くか、`dotnet build MailClient.sln` を実行
4. `MailClient.App` を「スタートアッププロジェクト」に設定し、実行構成を `Debug | x64`（または環境に応じて `x86`/`ARM64`）にして起動
5. 3ペインのプレースホルダウィンドウが表示され、Windowsのライト/ダーク設定に応じて外観が切り替わることを確認

`MailClient.Core` / `MailClient.ViewModels` / `MailClient.Data` / `MailClient.Mail` は `net8.0`（Windows非依存）なので、`dotnet build` / `dotnet test` はこのMac上でも実行可能。`MailClient.App` / `MailClient.Platform` は `net8.0-windows10.0.19041.0` のためWindows機が必要。

## 既知の注意点

- `Microsoft.WindowsAppSDK` / `Microsoft.Windows.SDK.BuildTools` のバージョンはこのリポジトリ作成時点の値を仮置きしています。`dotnet restore` / VS の NuGet復元で該当バージョンが見つからない（NU1102等）場合は、NuGetパッケージマネージャーで最新の安定版に上げてください
- `Package.appxmanifest` の `Publisher="CN=MasatoToda"` は仮の値です。初回デバッグ実行時にVisual Studioが自己署名のテスト証明書を自動生成して整合させるので、通常はそのままでF5実行できます。ストア配布時は正式な発行者証明書に差し替えが必要
- `Assets/*.png` は単色のプレースホルダ画像です（見た目のみ、機能に影響なし）。後で本物のアイコンに差し替えてください

## テスト用メールサーバー（M3以降で使用）

実アカウントに接続する前に、GreenMail（Docker）またはhMailServer（Windowsネイティブ）でIMAP/SMTPをローカル検証する想定。詳細はプランファイル §8 を参照。

## ソリューション構成

```
MailClient.sln
├── src/MailClient.App/            # WinUI 3 ヘッド（packaged, net8.0-windows10.0.19041.0）
├── src/MailClient.ViewModels/     # MVVM ViewModel（net8.0, WinUI非依存）
├── src/MailClient.Core/           # ドメインモデル + サービスインターフェース（net8.0）
├── src/MailClient.Data/           # SQLite + FTS5 永続化（net8.0）
├── src/MailClient.Mail/           # MailKit統合（IMAP/SMTP/同期エンジン）（net8.0）
├── src/MailClient.Platform/       # Credential Locker / AppNotificationManager（net8.0-windows）
└── src/MailClient.Infrastructure/ # DI合成ルート（net8.0）
```
