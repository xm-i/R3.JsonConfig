# R3.JsonConfig

R3 (ReactiveProperty) や ObservableCollections を利用したドメインモデルの JSON シリアライズ用 DTO を自動生成する Source Generator です。

---

## 解決する課題

通常のシリアライザ（System.Text.Json 等）でドメインモデルを保存・復元しようとすると、以下のような課題に直面します：

- **ReactiveProperty のシリアライズ**: `ReactiveProperty<T>` はラッパー型であるため、そのままでは `.Value` ではなくオブジェクトの内部構造がシリアライズされてしまいます。
- **循環参照のハンドリング**: 親子関係や相互参照を持つモデルをシリアライズすると、無限再帰が発生して実行時にクラッシュします。
- **DI（依存性注入）との連携**: コンストラクタインジェクションを利用しているモデルをデシリアライズで復元する際、標準のシリアライザだけではインスタンス化が困難です。

`R3.JsonConfig` は、Source Generator が最適な DTO を自動生成することで、これらの問題を解決します。

---

## 主な機能

- **Source Generator による生成**: 実行時のリフレクションを最小化。Native AOT (`JsonSourceGenerationContext`) にも対応した高速な動作。
- **型マッピング**: `ReactiveProperty<T>` → `T?`、`ObservableList<T>` → `T[]?` のように、JSON で扱いやすい型へ自動的にマッピング。
- **循環参照のサポート**: 内部的な `___Id` と `___Ref` トークンを用いて、オブジェクトの参照関係を保持したまま保存・復元が可能。
- **DI コンテナ連携**: 生成される `CreateModel` メソッドが `IServiceProvider` を受け取り、DI 経由でモデルを生成・注入。
- **ポリモーフィズム対応**: インターフェースや基底クラスをプロパティに持つ場合も、属性指定により派生型の識別と保存が可能。

---

## クイックスタート

### 1. インストール
`.csproj` にジェネレータを Analyzer として追加します。

```xml
<ItemGroup>
  <ProjectReference Include="..\R3.JsonConfig.Generators\R3.JsonConfig.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 2. モデルの定義
保存したいモデルに `[GenerateR3JsonConfigDto]` 属性を付与します。

```csharp
using R3;
using ObservableCollections;
using R3.JsonConfig.Attributes;

[GenerateR3JsonConfigDto]
public class MySettings {
    // ReactiveProperty は自動的に内部の型にマッピングされます
    public ReactiveProperty<string> UserName { get; } = new("Antigravity");

    // ObservableList は配列にマッピングされます
    public ObservableList<int> Scores { get; } = new();

    // 通常のプロパティ（public setter が必要）
    public bool IsEnabled { get; set; }
}
```

### 3. コンテキストの定義 (Native AOT 対応)
`JsonSerializerContext` を定義して、生成された DTO をシリアライズ対象に含めます。

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MySettingsForJson))]
public partial class MyJsonContext : JsonSerializerContext { }
```

### 4. 保存と読み込み
自動生成された `MySettingsForJson` と、定義した `MyJsonContext` を使用して変換を行います。

```csharp
// 1. モデルから DTO へ変換して保存
var model = new MySettings();
var dto = MySettingsForJson.CreateJson(model);
// ※ポリモーフィズムを使用する場合は JsonSerializerOptions の設定が必要（後述）
string json = JsonSerializer.Serialize(dto, MyJsonContext.Default.MySettingsForJson);
File.WriteAllText("settings.json", json);

// 2. JSON から DTO を経由してモデルを復元
string loadedJson = File.ReadAllText("settings.json");
var loadedDto = JsonSerializer.Deserialize(loadedJson, MyJsonContext.Default.MySettingsForJson);

// CreateModel は IServiceProvider を通じてモデルを生成します
MySettings restoredModel = MySettingsForJson.CreateModel(loadedDto, serviceProvider)!;
```

---

## 詳細な仕組みと型変換ルール

ジェネレータは `[GenerateR3JsonConfigDto]` が付与されたモデルクラスを解析し、以下の基準に基づいた DTO（末尾 `ForJson`）を生成します。

### 1. プロパティの収集ルール
モデル内の **public プロパティ** を走査し、以下の条件に合致するものを DTO の対象として収集します。

| プロパティの種類 | 収集条件 | 特記事項 |
| :--- | :--- | :--- |
| **ReactiveProperty<T>** | 常に収集 | getter のみ（Setter なし）でも対応可能 |
| **ObservableList<T>** | 常に収集 | getter のみ（Setter なし）でも対応可能 |
| **通常のプロパティ** | **public setter** が必須 | デシリアライズ時に値を代入するため |

- **対象外となるプロパティ**:
    - `private`, `internal`, `protected` などの public ではないプロパティ。
    - `{ get; }` のみ、または `{ get; private set; }` の通常のプロパティ。
    - **`[ExcludeProperty]`** 属性が付与されているプロパティ。

---

### 2. 型マッピングと再帰変換
収集されたプロパティは、その内部型に応じて以下のルールで DTO 側の型にマッピングされます。すべて JSON での欠損（null）を許容するため、DTO 側の型は一律で **nullable** になります。

| モデル側の型 | DTO 側の型 | 分類 |
| :--- | :--- | :--- |
| `ReactiveProperty<T>` | `T?` | **通常型**: そのままの型 |
| `ReactiveProperty<TModel>` | `TModelForJson?` | **再帰型**: DTO 変換が連鎖 |
| `ObservableList<T>` | `T[]?` | **配列型**: 要素が通常型ならそのまま配列化 |
| `ObservableList<TModel>` | `TModelForJson[]?` | **配列×再帰型**: 各要素を DTO 変換して配列化 |
| `T`（通常プロパティ） | `T?` | **そのままのマッピング** |

#### 再帰変換の流れ
プロパティの型 `T` 自体に `[GenerateR3JsonConfigDto]` が付与されている場合、ジェネレータはその型を DTO 型（`TForJson`）へ置き替えます。
これにより、モデルのツリー構造全体が連鎖的に DTO 化され、`CreateJson` / `CreateModel` 内部でも再帰的に変換メソッドが呼び出されます。

> [!NOTE]
> `Color` のように `[GenerateR3JsonConfigDto]` を持たない外部ライブラリの型は、DTO 上でもそのままの型として出力されます。これらをシリアライズするには別途 `JsonConverter` の登録が必要です。

---

### 3. 生成される DTO の構造
ジェネレータによって出力されるクラスは以下の仕様を持ちます。

- **命名規則**: `[モデル名]ForJson` という名前で生成されます。
- **名前空間**: 元のモデルと **全く同じ名前空間** に生成されます。
- **物理構造**: 全て **`public partial class`** として生成されます。
    - `partial` であるため、ユーザー側で独自のメソッドやプロパティを追加して拡張することが可能です。
- **オブジェクト識別子**: 循環参照を解決するためのメタ情報として、すべての DTO クラスに自動的に `string? ___Id` と `string? ___Ref` プロパティが追加されます。

---

## 高度な機能

### 循環参照の解決
親子で互いに参照し合っている場合でも、標準でシリアライズ可能です。内部的に `___Id` と `___Ref` トークンが発行され、デシリアライズ時に同一のインスタンスとして復元されます。

### ポリモーフィズム（多態性）
インターフェースや基底クラス（抽象クラス）をプロパティに持つ場合も、属性による識別により型情報を保持したまま保存できます。

#### 1. 属性の付与
基底型と派生型のそれぞれに適切な属性を付与します。

```csharp
// 基底型: [GenerateR3JsonConfigDto] を付与
[GenerateR3JsonConfigDto]
public interface IEffect { }

// 派生型: [GenerateR3JsonConfigDto] と [JsonConfigDerivedType] を付与
[GenerateR3JsonConfigDto]
[JsonConfigDerivedType("Blur")]
public class BlurEffect : IEffect {
    public ReactiveProperty<float> Radius { get; } = new(0f);
}
```

> [!NOTE]
> `[JsonConfigDerivedType]` に指定した文字列（型識別子）は、JSON 内の `___Type` プロパティとして出力されます。
> 各派生型はアプリケーション起動時に `ModuleInitializer` を通じて自動的にレジストリへ登録されます。

#### 2. シリアライズ設定
ポリモーフィズムを有効にするには、`JsonSerializerOptions` に対して以下の設定（モディファイアの追加）が必要です。

```csharp
var options = new JsonSerializerOptions() {
    // 実行時レジストリに登録された派生型情報を追加する設定
    TypeInfoResolver = MyJsonContext.Default.WithAddedModifier(ForJsonConverterRegistry.ApplyPolymorphism)
};
```

これを行わない場合、`___Type` 識別子が JSON に出力されず、デシリアライズ時に実際の型を解決できなくなります。

### DI コンテナのスコープ制御
生成される `CreateModel` メソッド内で子モデル（ネストされた `ForJson` 型）を処理する際、デフォルトでは引数として渡された `IServiceProvider` がそのまま引き継がれます。
これに対し、**`[JsonConfigCreateScope]`** 属性をプロパティに付与することで、そのプロパティのモデル生成時に毎回新しい DI スコープ (`IServiceScope`) を作成し、それを渡すように制御することが可能です。

```csharp
[GenerateR3JsonConfigDto]
public class ParentModel {
    // このプロパティの生成時のみ新しく DI スコープを生成する
    [JsonConfigCreateScope]
    public ChildModel ChildA { get; set; }

    // こちらは sp がそのまま引き継がれる (デフォルト)
    public ChildModel ChildB { get; set; }
}
```

パフォーマンス上のオーバーヘッドを避けるため、デフォルトではスコープは作成されません。スコープ付きサービス (`Scoped`) を個別に生成・注入する必要があるプロパティに対してのみこの属性を適用してください。

### カスタム JsonConverter の利用
`Color` 型のように、ジェネレータ側で DTO 化をサポートしていない型を扱う場合は、System.Text.Json 標準の `JsonConverter` を利用します。

```csharp
// JsonSourceGenerationContext 等に Converter を登録
[JsonSourceGenerationOptions(Converters = [typeof(ColorJsonConverter)])]
[JsonSerializable(typeof(MySettingsForJson))]
public partial class MyJsonContext : JsonSerializerContext { }
```

#### 利用方法
定義した `MyJsonContext` を使用して、リフレクションを最小化したシリアライズ・デシリアライズを行います。ポリモーフィズムを併用する場合は、オプションを介してコンテキストを生成します。

```csharp
// 1. ポリモーフィズム設定を含んだオプションを作成
var options = new JsonSerializerOptions(MyJsonContext.Default.Options) {
    TypeInfoResolver = MyJsonContext.Default.WithAddedModifier(ForJsonConverterRegistry.ApplyPolymorphism)
};

// 2. シリアライズの実行 (options を渡すことでモディファイアが適用される)
string json = JsonSerializer.Serialize(dto, options);

// 3. デシリアライズの実行
var loadedDto = JsonSerializer.Deserialize<MySettingsForJson>(json, options);
```

---

## 生成されるコードの仕様

`[GenerateR3JsonConfigDto]` を付与したクラスに対し、`obj/` ディレクトリ配下に `[モデル名]ForJson.g.cs` という名前でソースコードが自動生成されます。

### 変換のイメージ（Before / After）

以下のようなドメインモデルを定義した場合の、生成コードの構造です。

#### 元のモデル定義
```csharp
[GenerateR3JsonConfigDto]
public class MySettings {
    public ReactiveProperty<string> UserName { get; } = new("");
    public string Theme { get; set; } = "Dark";
}
```

#### 自動生成されるコードのイメージ
```csharp
// MySettingsForJson.g.cs (抜粋)
public partial class MySettingsForJson {
    public string? ___Id { get; set; }
    public string? ___Ref { get; set; }

    public string? UserName { get; set; }
    public string? Theme { get; set; }

    // DTO からモデルへの変換
    public static MySettings? CreateModel(MySettingsForJson? json, IServiceProvider sp, ReferenceResolver? resolver = null) {
        if (json is null) return null;
        if (json.___Ref is { } @ref) return resolver?.Resolve<MySettings>(@ref);

        resolver ??= new ReferenceResolver();
        var model = sp.GetRequiredService<MySettings>();
        if (json.___Id is { } id) resolver.Add(id, model);

        // 各プロパティの値を反映
        if (json.UserName is { } v1) model.UserName.Value = v1;
        if (json.Theme is { } v2) model.Theme = v2;

        return model;
    }

    // モデルから DTO への変換
    public static MySettingsForJson? CreateJson(MySettings? model, ReferenceTracker? tracker = null) {
        if (model is null) return null;

        tracker ??= new ReferenceTracker();
        if (tracker.GetOrAddId(model) is { } refId) {
            return new MySettingsForJson { ___Ref = refId };
        }

        return new MySettingsForJson {
            ___Id = tracker.GetId(model),
            UserName = model.UserName.Value,
            Theme = model.Theme
        };
    }
}
```

---

## 注意事項と制限事項

- **サポート対象**: `class` および `interface` のみが対象です。`record` や `struct` には非対応です。
- **デシリアライズの要件**: 通常のプロパティは `public setter` を持っている必要があります。
- **DI 前提**: `CreateModel` は `IServiceProvider` を利用してインスタンスを生成する設計になっています。

---

## ライセンス
本プロジェクトは MIT ライセンスの下で公開されています。
