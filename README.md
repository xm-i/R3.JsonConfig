# R3.JsonConfig

リアクティブモデル (`ReactiveProperty<T>` / `ObservableList<T>`) を JSON 用 DTO (末尾 `ForJson`) に自動生成するソースジェネレータ。

## プロジェクト構成
- `R3.JsonConfig` : 属性とユーティリティ。
- `R3.JsonConfig.Generators` : 基本ジェネレータ `DefaultJsonDtoGenerator`。
- `R3.JsonConfig.Demo` : 利用例アプリとモデル。

## 仕組み
対象属性を付与したモデルクラスに対し、自動で DTO クラス (末尾 `ForJson`) を生成:
- 公開 setter 付きプロパティ + `ReactiveProperty<T>` + `ObservableList<T>` を解析。
- `ReactiveProperty<T>` → `T?` もしくは `NestedModelForJson?`。
- `ObservableList<T>` → `T[]?` もしくは `NestedModelForJson[]?`。
- ネストされた対象モデルは再帰的に DTO 化。

## 属性
| 属性 | 対象 | 役割 |
|------|------|------|
| `GenerateR3JsonConfigDtoAttribute` | クラス | DTO 生成トリガー |
| `ExcludePropertyAttribute` | プロパティ | DTO 生成から除外 |

### ExcludePropertyAttribute
特定のプロパティを DTO に含めたくない場合に使用:
```csharp
[GenerateR3JsonConfigDto]
public class MyModel {
    public string Name { get; set; } = "";

    [ExcludeProperty]
    public string Secret { get; set; } = ""; // DTO に含まれない
}
```

## 生成される API 例
`ParentModel` → `ParentModelForJson` に以下が生成:
- プロパティ群 (nullable)。
- `static ParentModel? CreateModel(ParentModelForJson? json, IServiceProvider sp)` : DI でモデル取得し反映 (ObservableList は Clear 後に再追加)。
- `static ParentModelForJson? CreateJson(ParentModel? model)` : モデルから DTO 作成 (ObservableList は配列へ変換)。

## 利用手順
1. csproj にジェネレータを Analyzer として参照:
```xml
<ItemGroup>
  <ProjectReference Include="..\R3.JsonConfig.Generators\R3.JsonConfig.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```
2. モデルへ属性付与:
```csharp
[GenerateR3JsonConfigDto]
public class ParentModel {
    public string Name { get; set; } = "Default";
    public ReactiveProperty<string> Title { get; } = new("Hello");
    public ObservableList<int> Numbers { get; } = new();
}
```
3. 基本的な読み書き:
```csharp
var model = sp.GetRequiredService<ParentModel>();
var dto = ParentModelForJson.CreateJson(model);
File.WriteAllText("config.json", JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));

var loadedDto = JsonSerializer.Deserialize<ParentModelForJson>(File.ReadAllText("config.json"));
ParentModelForJson.CreateModel(loadedDto, sp);
```
### Source Generated SerializerContext を使う場合
`JsonSerializerContext` の部分クラスを定義して AOT/リフレクションコストを削減できます。
```csharp
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ParentModelForJson))]
public partial class ConfigJsonSerializerContext : JsonSerializerContext { }
```
利用例:
```csharp
var dto = ParentModelForJson.CreateJson(model);
var json = JsonSerializer.Serialize(dto, ConfigJsonSerializerContext.Default.ParentModelForJson);
File.WriteAllText("config.json", json);

var loaded = JsonSerializer.Deserialize(File.ReadAllText("config.json"), ConfigJsonSerializerContext.Default.ParentModelForJson);
ParentModelForJson.CreateModel(loaded, sp);
```

## 変更点 (以前の Convert 機能撤廃後)
- 型変換ルール (Color → Hex 等) を削除。変換は利用側が別途コンバータ/専用フィールドで対応。
- `List<T>` ではなく `T[]` を出力。

## 制限 / TODO
- 循環参照未対応。
- Null 許容性は単純化 (全て nullable)。
- 詳細な型メタ情報 (readonly / init など) 未反映。
- パフォーマンス最適化 (辞書キャッシュ等) 未実装。

## 開発
```bash
 dotnet build
```
生成コードは `obj/Debug/net*/generated/` 下。

## ライセンス
検討中。
