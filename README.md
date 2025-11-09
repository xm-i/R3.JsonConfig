# R3.JsonConfig

リアクティブモデル (`ReactiveProperty<T>` / `ObservableList<T>`) を JSON 用 DTO (末尾 `ForJson`) に自動変換するソースジェネレータ。

## プロジェクト構成
- `R3.JsonConfig` : 属性とユーティリティ。
- `R3.JsonConfig.Generators` : 基本インクリメンタルジェネレータ `DefaultJsonDtoGenerator`。
- `R3.JsonConfig.Demo.Generators` : デモ用拡張ジェネレータ `JsonDtoGenerator` (Color の変換ルール追加)。
- `R3.JsonConfig.Demo` : 利用例アプリとモデル。

## 仕組み
対象属性を付与したモデルクラスに対し、自動で JSON フレンドリな DTO クラス (末尾 `ForJson`) を生成します。
- 公開 setter 付きプロパティとReactivePropertyを収集。
- `ReactiveProperty<T>` を `T?` にフラット化。
- `ObservableList<T>` を `List<T>?` に変換。要素型が同じく対象なら入れ子 DTO 化。
- 変換ルールがあれば型を JSON 文字列/数値等に置換。

## 属性
| 属性 | 定義場所 | 役割 |
|------|----------|------|
| `GenerateR3JsonConfigDefaultDtoAttribute` | ライブラリ | 既定ジェネレータ用トリガー |

## 生成される API 例
`ParentModel` → `ParentModelForJson` が生成され以下の静的メソッドを提供:
- `ParentModel CreateModel(ParentModelForJson json, IServiceProvider sp)` : DI からモデル取得し反映。
- `ParentModelForJson CreateJson(ParentModel model)` : モデルから DTO 作成。

コレクション反映時は既存 `ObservableList<T>` を `Clear()` して重複を防止します。

## リアクティブ & 変換ルール
`ReactiveProperty<T>` は内部値をそのまま DTO に。`ConversionRule` が登録されている型はコンバータ / 逆変換メソッドを使用。デモでは `System.Drawing.Color` → `#AARRGGBB` へ変換。

## 最小利用手順
1. csproj にジェネレータを Analyzer として参照 (デモ利用の場合、拡張ジェネレータのみで十分):
```xml
<ItemGroup>
  <ProjectReference Include="..\R3.JsonConfig.Demo.Generators\R3.JsonConfig.Demo.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

2. モデルへ属性付与:
```csharp
[GenerateR3JsonConfigDefaultDto] // あるいは、継承した独自ジェネレータ用属性
public class ParentModel {
    public string Name { get; set; } = "Default";
    public ReactiveProperty<string> Title { get; } = new("Hello");
    public ObservableList<int> Numbers { get; } = new();
}
```
3. シリアライズ / デシリアライズ:
```csharp
var pm = serviceProvider.GetRequiredService<ParentModel>();
var dto = ParentModelForJson.CreateJson(pm);
var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText("config.json", json);

var loaded = JsonSerializer.Deserialize<ParentModelForJson>(File.ReadAllText("config.json"));
if (loaded != null) {
    ParentModelForJson.CreateModel(loaded, serviceProvider);
}
```

## カスタム変換ルール
拡張ジェネレータを作成し、変換ルールを追加:
```csharp
[Generator]
public class JsonDtoGenerator : DefaultJsonDtoGenerator {
    public JsonDtoGenerator() {
        this.ConversionRules.Add(new("System.Drawing.Color", JsonDtoType.Text, "ColorToHex", "HexToColor"));
    }
    protected override string TargetAttribute => "Your.Namespace.YourAttribute";
}
```
コンバータ/逆変換メソッドは生成コードから参照可能なアクセス修飾子にすること。

## 制限 / TODO
- 未対応のケース多々あり。必要になったら追加する。

## ライセンス
まともなライブラリになったら検討する。
