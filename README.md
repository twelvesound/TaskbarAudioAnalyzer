# Taskbar Audio Analyzer

[日本語](README.md) | [English](README.en.md)

透明化したタスクバーの裏に置く、軽量な常駐オーディオアナライザーです。

## ダウンロード

[GitHub Releases](https://github.com/twelvesound/TaskbarAudioAnalyzer/releases/latest)からビルド済みの配布物をダウンロードできます。

本体のインストーラーは[本体Release](https://github.com/twelvesound/TaskbarAudioAnalyzer/releases/tag/v1.1.1)から取得できます。従来のポータブルZIPとVST3は[v1.1.0](https://github.com/twelvesound/TaskbarAudioAnalyzer/releases/tag/v1.1.0)から取得できます。

- `TaskbarAudioAnalyzer-*-win-x64.zip`: Windowsアプリ。展開して`TaskbarAudioAnalyzer.exe`を実行してください。[.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/10.0)が必要です。
- `TaskbarAudioTap-*-win-x64.zip`: VST3プラグイン。ZIP内の`TaskbarAudioTap.vst3`を`C:\Program Files\Common Files\VST3\12sound`へコピーし、DAWで再スキャンしてください。

配布物は現在コード署名されていないため、初回実行時にWindowsの警告が表示される場合があります。ZIPと一緒に公開される`SHA256SUMS.txt`でファイルを検証できます。

表示:

- `LUFS-S`: ITU-R BS.1770のK-weightingとEBU Tech 3341の3秒窓によるShort-Term LUFS。表示は毎秒5回、0.2 LUのデッドバンド付き
- `TP`: 4倍補間による簡易トゥルーピーク推定値（dBFS）
- `PHASE`: 左右チャンネルの位相相関（−1〜+1）。約400msで平滑化し、毎秒10回更新
- Spectrum: 約60Hz〜16kHzを28本に分けた対数スペクトラム
- 入力状態: `WIN`、`VST`、またはAuto Mix中の`WIN+VST`

## 本体のインストールと配置

本体用の `TaskbarAudioAnalyzer-v1.1.1-win-x64-Setup.exe` を実行すると、Taskbar Underlay Monitorと同じくアプリとユーザー設定を分離して配置します。

- 本体: `C:\Program Files\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.exe`
- 設定: `%LOCALAPPDATA%\12sound\TaskbarAudioAnalyzer\settings.json`（従来の場所を継続使用）
- スタートメニュー: `Taskbar Audio Analyzer`

セットアップは管理者権限と [.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/10.0) が必要です。ランタイム未導入の場合は案内して中止します。実行中の本体はメニューの `Exit` で終了してからインストールしてください。VST3はこのセットアップに含まれません。

既存の設定と自動起動のON/OFFを保持し、有効な自動起動はProgram Filesの本体へ更新します。新規に自動起動を有効にする場合は本体メニューの `Enable startup` を使ってください。Windowsの「インストールされているアプリ」からアンインストールできます。ユーザー設定は再導入に備えて残します。別のWindowsユーザーの自動起動登録は、そのユーザーで `Disable startup` を選択して解除してください。

ソースから本体インストーラーを作る場合（.NET 10 SDKと [Inno Setup](https://jrsoftware.org/isdl.php) 6.7.3以降が必要）:

```powershell
.\scripts\Build-Installer.ps1
# コンパイラーの場所を指定する場合:
.\scripts\Build-Installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
# ビルドしてセットアップ画面を開く場合:
.\scripts\Install-ProgramFiles.ps1
```

生成先は `artifacts\installer\v1.1.1\` です。Setup.exeと`SHA256SUMS.txt`を生成します。VST3のSDKは不要です。

## 起動

```powershell
git clone https://github.com/twelvesound/TaskbarAudioAnalyzer.git
cd TaskbarAudioAnalyzer
.\scripts\Start-Analyzer.ps1
```

必要環境はWindows 10以降と.NET 10 SDKです。

`Start-Analyzer.ps1`はインストール済みのProgram Files版を優先します。開発用ビルドを実行する場合は `-Development` を付けてください（実行中の本体は先に終了してください）。開発版を実行しても、Program Files版が存在する限り自動起動先はインストール版のままです。

操作:

- 左ドラッグ: 位置を移動
- 通知領域のアイコンを右クリック → `Audio source`: 解析する音声を選択
  - `Default playback (loopback)`: Windowsの既定出力音
  - `Playback devices (loopback)`: 指定した再生デバイスの出力音
  - `Recording inputs`: マイク入力やオーディオインターフェイスのループバック入力
- 通知領域のアイコンを右クリック → `Input mode`
  - `Auto Mix (Windows + VST)`: Windows音声とVST3タップの音声を自動ミックス（初期設定）
  - `Windows only`: Windows側の選択音源だけを解析
  - `VST only`: VST3タップだけを解析
  - `Windows trim` / `VST trim`: 各入力を−12〜+6dBで調整
- 右クリック `Enable startup`: Windows起動時に自動起動
- 右クリック `Disable startup`: 自動起動を解除
- 右クリック `Exit`: 終了

メモ:

- 初期状態では既定の再生デバイスをWASAPIループバックで取得し、通知領域から別の出力または録音入力へ切り替えられます。
- 音声デバイスが一時的に利用できない場合は、2秒間隔で再接続します。
- 選択した音声デバイスはウィンドウ位置と一緒に保存されます。
- 入力モードと各入力のトリム値も保存されます。
- 設定は`%LOCALAPPDATA%\12sound\TaskbarAudioAnalyzer\settings.json`に保存されます。整理前のビルドフォルダにある旧設定は、初回起動時に自動移行されます。
- 自動起動が有効な場合、通常起動時に登録済みの実行ファイルパスを現在の場所へ更新します。
- `LUFS-S`は48kHzへ統一したステレオ信号をBS.1770のK-weightingで測定します。入力トリムやAuto Mixの合算は測定前に適用されます。
- `TP`は4倍補間による軽量な推定値で、認証済み測定器の代替ではありません。
- 初回起動時は画面下部中央に配置され、その後はドラッグした位置を保存します。

## ASIO / VST3タップ

`Taskbar Audio Tap`は音を加工せずに通過させ、解析用のステレオPCMだけを共有メモリ経由で本体へ送るVST3です。

VST3のビルドにはCMake、Visual Studio 2026のC++ビルド環境、Steinberg VST3 SDKが必要です。SDKは次の場所へ取得してください。

```powershell
git clone --recursive https://github.com/steinbergmedia/vst3sdk.git .\external\vst3-sdk
```

ビルドとシステムVST3フォルダへのインストール（管理者権限のPowerShellで実行）:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Vst3.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Install-Vst3.ps1
```

インストール先は`C:\Program Files\Common Files\VST3\12sound\TaskbarAudioTap.vst3`です。VST3のベンダー名も`12sound`として登録されます。

DAWでプラグインを再スキャンし、マスター出力の最終段へ`Taskbar Audio Tap`を1つ挿してください。本体の`Input mode`を`Auto Mix (Windows + VST)`にすると、Windows音声とASIOへ送るDAW音声を切り替えずにまとめて解析できます。

複数のDAWや複数トラックでTapが同時に動作した場合は、最初に音声送信を開始した1インスタンスだけが採用されます。そのTapが約1秒停止すると、次に動作しているTapへ自動的に切り替わります。

ミックスにはリミッターや自動ノーマライズを適用しません。合算結果が0dBFSを超えた場合は、TPにもその値がそのまま表示されます。

## ディレクトリ構成

```text
TaskbarAudioAnalyzer/
├─ src/
│  ├─ TaskbarAudioAnalyzer/   # WPFアプリ本体
│  └─ TaskbarAudioTap/        # VST3プラグイン
├─ scripts/                   # 起動・ビルド・インストール
├─ external/                  # 外部SDK
├─ tools/                     # ローカル開発ツール
└─ artifacts/                 # ビルド生成物や出力ファイル
```

WPFアプリだけをビルドする場合:

```powershell
dotnet build .\src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj
```

## ライセンス

このプロジェクトは[MIT License](LICENSE)で公開されています。著作権表示とライセンス文を保持することで、商用・非商用を問わず利用、改変、再配布できます。

配布物に含まれる外部コンポーネントのライセンスは[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)を参照してください。
