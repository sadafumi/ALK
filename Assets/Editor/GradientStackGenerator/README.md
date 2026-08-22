# Gradient Stack Generator (Unity Editor 拡張)

複数（既定6個）の Gradient を**縦に積み重ねて1枚のテクスチャ**にする Unity エディタ拡張です。
各 Gradient は横方向のランプになり、上から順に帯（バンド）として縦へ並びます。
ランプアトラス（1枚に複数ランプを収める）などに使えます。

## 導入

1. `GradientStackGenerator` フォルダをプロジェクトの `Assets` 以下にコピーします。
2. コンパイル後、メニューに **Tools > Gradient Stack Generator** が追加されます。

## 使い方

1. **Tools > Gradient Stack Generator** を開く。
2. `本数`（既定6）と各 `Gradient` を編集。上にあるものほど出力の上側の帯になります。
3. `幅` `1本あたりの高さ(px)` `GradientのAlpha` `sRGB` を設定。
4. `縦合成テクスチャを保存 (PNG)`。Assets 配下なら sRGB・Wrap(Clamp) を自動設定します。

## パラメータ

| 項目 | 説明 |
|------|------|
| 本数 | 積み重ねる Gradient の数（1〜8、既定6） |
| Gradient N | 各帯のランプ。Nが小さいほど上側 |
| 合成モード(Alphaへ) | 各帯の合成方法をプルダウンで選択（下記）。番号を Alpha に埋め込む |
| Alphaの内容 | `BlendMode`=合成モード番号を埋め込む / `GradientAlpha`=GradientのA / `Opaque`=不透明 |
| 幅 | 出力の横幅（各ランプの解像度） |
| 1本あたりの高さ(px) | 各帯の高さ。総高さ = 本数 × この値 |
| sRGB | 色ランプはON推奨 / データ用途はOFF(Linear)。BlendMode時は自動でsRGB OFF/Point |

## 合成モード（HLSL側で評価）

各帯に **6種類の合成モード** をプルダウンで指定できます。ツールは「どのモードか」だけを Alpha チャンネルに
番号として書き込み、**実際の合成計算は HLSL 側で行います**（同梱 `Shaders/GradientStackBlend.hlsl`）。

| 番号 | モード | 状態 |
|------|--------|------|
| 0 | 加算 (Add) | 実装済み |
| 1 | 乗算 (Multiply) | 実装済み |
| 2 | オーバーレイ (Overlay) | 実装済み |
| 3〜5 | (予約4〜6) | あとで実装（現状は src をそのまま返すスタブ） |

### エンコード / デコード

- エンコード（ツール側）: `alpha = (mode + 0.5) / 6`
- デコード（HLSL側）: `int mode = clamp((int)floor(alpha * 6), 0, 5);`

### HLSLでの使い方（例）

```hlsl
#include "GradientStackBlend.hlsl" // パスはプロジェクトに合わせて調整

// Built-in RP (sampler2D) の例:
float3 col = GS_CompositeTex2D(_StackTex, uvX, _BandCount, baseColor);

// もしくは各帯を自前で読む:
float4 c = tex2D(_StackTex, float2(uvX, (i + 0.5) / _BandCount));
int mode = GS_DecodeBlendMode(c.a);
baseColor = GS_ApplyBlend(mode, baseColor, c.rgb);
```

残り3種を実装する際は、`GradientStackBlend.hlsl` の `GS_Blend_Reserved4/5/6` を書き換えてください。

### マスクで処理をゲート（白=処理 / 黒=しない）

マスク値を渡すと、**白い所だけ合成し、黒い所は下地そのまま**にできます（中間値は部分適用）。

```hlsl
float mask = tex2D(_MaskTex, uvMask).r;      // 白=1(処理) / 黒=0(スキップ)
float3 col = GS_CompositeTex2DMasked(_StackTex, driver, _BandCount, baseColor, mask);

// 1バンドだけ手動で行う場合:
baseColor = GS_ApplyBlendMasked(mode, baseColor, c.rgb, mask); // lerp(base, blended, mask)
```

`mask <= 0` のときは合成計算自体をスキップして下地を返します（Texture2D/SamplerState 版は `GS_CompositeMasked`）。

### 実装サンプル

`Shaders/GradientStackSample.shader`（Built-in RP / Unlit）に、実際に縦積みテクスチャを使う例を用意しました。
マテリアルに `Sample/GradientStackSample` を割り当て、`Gradient Stack` に本ツールで作ったテクスチャを入れて動作を確認できます。

- `Base Color`（下地）から開始し、各帯を Alpha のモードに従って重ねます。
- 各帯を引く「ドライバ値(0..1)」は `UV.x`（タイリング可）か `Base輝度` を選べます。
- `Band Count` は本数に合わせてください。

> URP で使う場合は、`GS_Composite`（Texture2D/SamplerState版）を使い、`_StackTex`/`sampler_StackTex` を
> `TEXTURE2D`/`SAMPLER` で宣言してください。

## 保存と再編集（メタデータにプリセット保存）

保存時、**生成テクスチャ自身のメタデータ**に、Gradientや各設定を「編集プリセット」として書き込みます。
これを読み込むことで、あとから編集を再開できます。

- 保存先は2箇所（両方に書き込み）:
  - **PNGファイルの tEXt チャンク**（`GradientStackPreset`）… テクスチャファイル自体に埋め込むので移動しても付いてくる
  - **.meta の userData**（Assets 配下のフォールバック）
- 再編集: ウィンドウ上部の `読み込み（編集を再開）` に生成PNGを入れて `設定を読み込む` を押すと、
  Gradient・本数・合成モード・サイズ・sRGB などが復元されます。

> 埋め込むのは「編集用のGradientプリセット（数値データ）」です。画像のピクセル自体はそのままなので、
> シェーダでの利用には影響しません。

## メモ

- 出力サイズは `幅 × (1本あたりの高さ × 本数)`。例: 幅256 / 高さ16 / 6本 → 256×96。
- V座標で帯を選びます（6本なら `v = (index + 0.5) / 6`）。BlendMode時は Alpha にモード番号が入るため、
  にじみ防止に **Filter=Point / sRGB OFF** を推奨（保存時に自動設定）。
- パラメータ・合成モード・各 Gradient は EditorPrefs で保持します。保存先も他ツールと共通で記憶します。

## フォルダ構成

```
GradientStackGenerator/
├── Editor/
│   └── GradientStackWindow.cs   # EditorWindow (Tools > Gradient Stack Generator)
└── Shaders/
    ├── GradientStackBlend.hlsl   # Alphaからモード復号＋合成計算(3種実装/3種予約)
    └── GradientStackSample.shader # 実装サンプル (Sample/GradientStackSample)
```
