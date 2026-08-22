# PCSS Shadows for URP

URP標準のシャドウフィルタリング(固定カーネルPCF)を **PCSS (Percentage-Closer Soft Shadows)** に置き換えるRenderer Feature拡張です。遮蔽物からの距離に応じて影のボケ幅が変化する、物理的にもっともらしいソフトシャドウを実現します。

- 対応環境: Unity 6 (6000.x) / URP 17.x / Render Graph
- 対象: **メインライト(ディレクショナルライト)の影**(カスケード対応)
- シェーダー改変不要 — URP内蔵の Screen Space Shadows と同じ仕組みで、シーン内のすべてのLit系シェーダーに自動適用されます

## 必要パッケージ

この拡張が依存するのは **URPのみ** です。他のプロジェクトへ移植する場合は `Assets/PCSS` フォルダをコピーし、以下が入っていれば動作します:

| パッケージ | バージョン | 備考 |
|---|---|---|
| `com.unity.render-pipelines.universal` | 17.x (Unity 6000.x) | これだけ明示的に必要 |
| `com.unity.render-pipelines.core` | 17.x | URPが自動で連れてくる(手動追加不要) |

manifest.json にあるその他のパッケージ(Input System, Timeline, AI Navigation など)はURP 3Dテンプレートの標準構成であり、PCSS拡張には不要です。

> Godotで同等の影を再現する場合は [Godot/README.md](../../Godot/README.md) を参照(Godot 4はPCSS相当が組み込みのため、設定適用のみで再現可能)。

## 仕組み

1. **Blocker Search** — シャドウマップ上でピクセル周辺を探索し、遮蔽物の平均深度を求める
2. **Penumbra Estimation** — 受光面と遮蔽物の距離 × 光源の角直径からペナンブラ幅を推定
3. **Variable-Radius PCF** — 推定幅に応じた半径でVogelディスクサンプリング + ハードウェア比較フィルタ

結果はスクリーンスペースのシャドウテクスチャ(`_ScreenSpaceShadowmapTexture`)に書き込まれ、`_MAIN_LIGHT_SHADOWS_SCREEN` キーワードによって全マテリアルがこれを参照します。

## セットアップ

1. メニュー **Tools > PCSS > Add PCSS To All Renderers** を実行
   (アクティブなURPアセットの全Renderer — PC_Renderer / Mobile_Renderer など — に追加されます)
2. URPアセットで **Main Light の影が有効** になっていることを確認
   (Soft Shadowsのチェックは不要です — フィルタリングはPCSSが行います)

手動で追加する場合は、Rendererアセットの Inspector → *Add Renderer Feature* → **PCSS Shadows Feature**。

> ⚠️ URP内蔵の **Screen Space Shadows** Renderer Feature とは競合します。両方追加しないでください。

## パラメータ

| パラメータ | 説明 |
|---|---|
| Light Angular Diameter | 光源の見かけの角直径(度)。大きいほど距離に応じたボケが強い。実際の太陽は約0.53° |
| Min Penumbra Width | 最小ペナンブラ幅(m)。接地部のシャープさの下限 |
| Max Penumbra Width | 最大ペナンブラ幅(m)。ボケの上限(パフォーマンスとタイル漏れ防止) |
| Blocker Search Radius | ブロッカー探索半径(m)。これより遠い遮蔽物はボケに寄与しない |
| Blocker Depth Bias | ブロッカー判定バイアス。セルフシャドウのノイズが出たら少し上げる |
| Blocker / Filter Sample Count | 品質とコストのトレードオフ |

## 制限事項

- **メインライトのみ**: 追加ライト(ポイント/スポット)はURP標準のフィルタリングのまま
- **透明オブジェクト**: スクリーンスペース解決の制約上、透明キューは標準の影サンプリングにフォールバック(URP内蔵Screen Space Shadowsと同じ挙動)
- **カスケード境界**: 境界をまたぐ際にボケ幅がわずかに変化することがある
- Render Graph専用(URP Compatibility Modeは非対応)

## ファイル構成

```
Assets/PCSS/
├── Runtime/PCSSShadowsFeature.cs      # Renderer Feature + RenderGraphパス
├── Shaders/PCSSScreenSpaceShadows.shader  # PCSS本体
├── Editor/PCSSSetupMenu.cs            # 自動セットアップメニュー
└── README.md
```
