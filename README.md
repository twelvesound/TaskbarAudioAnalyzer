# Taskbar Audio Analyzer

[日本語](README.md) | [English](README.en.md)

透明化したタスクバーの裏に置く、軽量な常駐オーディオアナライザーです。

表示:

- `LUFS-S`: ITU-R BS.1770のK-weightingとEBU Tech 3341の3秒窓によるShort-Term LUFS。表示は毎秒5回、0.2 LUのデッドバンド付き
- `TP`: 4倍補間による簡易トゥルーピーク推定値（dBFS）
- `PHASE`: 左右チャンネルの位相相関（−1〜+1）。約400msで平滑化し、毎秒10回更新
- Spectrum: 約60Hz〜16kHzを28本に分けた対数スペクトラム
- 入力状態: `WIN`、`VST`、またはAuto Mix中の`WIN+VST`

起動:

```powershell
git clone https://github.com/twelvesound/TaskbarAudioAnalyzer.git
cd TaskbarAudioAnalyzer
.\scripts\Start-Analyzer.ps1
```

必要環境はWindows 10以降と.NET 10 SDKです。

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
