# Ramp Map Generator (Unity Editor 拡張)

2つの Gradient を使い、**RGBチャンネルとAlphaチャンネルに別々の情報**を持つ Ramp（グラデーション）マップを
生成する Unity エディタ拡張です。トゥーンのランプ、グラデーションマップ用LUT、データ用ランプなどに使えます。

- **RGB** ← 「RGB用 Gradient」の色
- **A** ← 「Alpha用 Gradient」から取り出した1チャンネル値（輝度 / R / G / B / Gradientのアルファ）

## 導入

1. `RampMapGenerator` フォルダをプロジェクトの `Assets` 以下にコピーします。
2. コンパイル後、メニューに **Tools > Ramp Map Generator** が追加されます。

## 使い方

1. **Tools > Ramp Map Generator** を開く。
2. `RGB用 Gradient` と `Alpha用 Gradient` をそれぞれ編集。
3. `Alphaの取り出し元` で、Alpha用Gradientのどの値をAに入れるか選択（既定=輝度）。
4. `方向`（横/縦）・`幅`・`高さ`・`sRGB` を設定。
5. `Rampマップを保存 (PNG)`。Assets 配下に保存すると、sRGB設定・WrapをClampに自動設定します。

## パラメータ

| 項目 | 説明 |
|------|------|
| RGB用 Gradient | 出力の RGB に使う色ランプ |
| Alpha用 Gradient | 出力の A に使うランプ（色を1チャンネルに落として使用） |
| Alphaの取り出し元 | `Luminance`（輝度）/ `R` / `G` / `B` / `GradientAlpha`（Gradientのアルファ） |
| 方向 | `Horizontal`=左→右 / `Vertical`=下→上 に t が進む |
| 幅 / 高さ | 出力解像度。ランプは 256×1〜16 程度が一般的 |
| sRGB | 色ランプはON推奨。データ用途はOFF(Linear) |

## メモ

- Gradient の Blend/Fixed（ステップ）モードはそのまま反映されます（`Gradient.Evaluate`）。
- 出力は RGBA PNG。Alpha は独立した情報として保存されます。
- Wrap は Clamp、`alphaIsTransparency` は OFF に設定して、ランプLUTとして素直に使えるようにしています。
- 幅=1 でも動作します（縦ランプ用など）。

## フォルダ構成

```
RampMapGenerator/
└── Editor/
    └── RampMapWindow.cs   # EditorWindow (Tools > Ramp Map Generator)
```
