# Mesh Normal Baker (Unity Editor 拡張)

メッシュの法線を UV 空間へ焼き込み、**法線マップ（テクスチャ）として出力**する Unity エディタ拡張です。
Shading Baker の「メッシュ→法線テクスチャ」機能を単体ツールとして切り出したものです。
**単一メッシュのサブメッシュ選択**に対応しています。

## 導入

1. `MeshNormalBaker` フォルダをプロジェクトの `Assets` 以下にコピーします。
   - `Assets/MeshNormalBaker/Editor/*.cs`
   - `Assets/MeshNormalBaker/Shaders/*.shader`
2. コンパイル後、メニューに **Tools > Mesh Normal Baker** が追加されます。

> Shading Baker と同時に入れても、シェーダ名を別（`Hidden/MeshNormalBaker/*`）にしているので競合しません。

## 使い方

1. **Tools > Mesh Normal Baker** を開く。
2. `メッシュ / GameObject` に、MeshFilter・SkinnedMeshRenderer を持つ GameObject か、Mesh を指定。
3. `サブメッシュ` のトグルで、ベイクするサブメッシュを選択（`全選択` / `全解除` あり）。
   - Renderer がある場合は各サブメッシュのマテリアル名も表示します。
4. `解像度` `法線空間` `縁の埋め` `Y反転` を設定し、`法線マップをベイク` を押す。
5. `法線マップをPNG保存` で出力（データマップなので sRGB は自動 OFF）。

## 各設定

| 項目 | 説明 |
|------|------|
| サブメッシュ | ベイク対象のサブメッシュを個別に選択。選んだサブメッシュのみ焼き込む |
| 解像度 | 出力テクスチャの一辺サイズ (256〜4096) |
| 法線空間 | `Object`=配置に依存しない / `World`=シーン上の向きを反映（オブジェクト空間法線＝虹色系） |
| 縁の埋め(px) | UVアイランド外周を埋めてシーム（縫い目）のにじみを軽減 |
| Y反転 | 出力が上下反転する場合にON |

## メモ

- 頂点シェーダで UV を直接クリップ座標へ変換して焼き込むため、MVP 行列に依存せず安定してベイクできます。
- サブメッシュ選択は、選択したサブメッシュのみを同じUV空間へ描画します（UVは共有前提）。
- パラメータ（解像度・法線空間・縁の埋め・Y反転）は EditorPrefs で保持します。保存先も他ツールと共通で記憶します。

## フォルダ構成

```
MeshNormalBaker/
├── Editor/
│   ├── MeshNormalBakerCore.cs     # ベイク・ディレーション・保存
│   └── MeshNormalBakerWindow.cs   # EditorWindow (Tools > Mesh Normal Baker)
└── Shaders/
    ├── NormalBake.shader          # メッシュ法線をUVへベイク
    └── Dilation.shader            # 縁埋め
```
