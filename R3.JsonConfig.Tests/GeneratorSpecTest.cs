using Microsoft.CodeAnalysis;

using Shouldly;

using Xunit;

namespace R3.JsonConfig.Tests;

/// <summary>
/// README「ジェネレータ仕様 (DefaultJsonDtoGenerator)」セクションに記載されたルールを検証するテスト。
/// リージョン番号は README の見出しに対応している。
/// </summary>
public class GeneratorSpecTest {

	#region 1. Target class conditions

	/// <summary>
	/// [GenerateR3JsonConfigDto] がないクラスから DTO が生成されないことを検証する。
	/// </summary>
	[Fact]
	public async Task NoDtoGenerated_WhenAttributeIsMissing() {
		var source = """
			using R3;
			using ObservableCollections;

			namespace TestNamespace;

			public class NoAttributeModel {
				public string Name { get; set; } = "";
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
		runResult.Results.SelectMany(r => r.GeneratedSources).ShouldBeEmpty(
			"[GenerateR3JsonConfigDto] がないクラスからは DTO が生成されてはいけない");
	}

	/// <summary>
	/// struct に [GenerateR3JsonConfigDto] を付与してもジェネレータに無視されることを検証する。
	/// ジェネレータは ClassDeclarationSyntax ノードのみを処理するため。
	/// </summary>
	[Fact]
	public async Task NoDtoGenerated_WhenTypeIsStruct() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public struct MyStruct {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);

		runResult.Results.SelectMany(r => r.GeneratedSources).ShouldBeEmpty(
			"struct は対象外なので DTO が生成されてはいけない");
	}

	/// <summary>
	/// record に [GenerateR3JsonConfigDto] を付与してもジェネレータに無視されることを検証する。
	/// record は RecordDeclarationSyntax を使用するため ClassDeclarationSyntax の対象外となる。
	/// </summary>
	[Fact]
	public async Task NoDtoGenerated_WhenTypeIsRecord() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public record MyRecord(int Value);
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);

		runResult.Results.SelectMany(r => r.GeneratedSources).ShouldBeEmpty(
			"record は対象外なので DTO が生成されてはいけない");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] が付与されたクラスから DTO が生成され、コンパイルエラーが発生しないことを検証する。
	/// </summary>
	[Fact]
	public async Task DtoGenerated_WhenClassHasGenerateAttribute() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class SimpleModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
		runResult.Results.SelectMany(r => r.GeneratedSources).ShouldNotBeEmpty(
			"[GenerateR3JsonConfigDto] 付きクラスから DTO が生成される必要がある");
	}

	#endregion

	#region 2. DTO naming convention

	/// <summary>
	/// 生成される DTO クラス名が {ModelName}ForJson の形式になることを検証する。
	/// </summary>
	[Fact]
	public async Task DtoClassName_IsModelNameSuffixedWithForJson() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class PlayerSettings {
				public string Name { get; set; } = "";
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var generated = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();

		generated.ShouldNotBeEmpty();
		var code = generated.First().SourceText.ToString();
		code.Contains("public partial class PlayerSettingsForJson").ShouldBeTrue(
			"DTO クラス名は {ModelName}ForJson であるべき");
	}

	/// <summary>
	/// 生成されるファイル名が {ModelName}ForJson.g.cs になることを検証する。
	/// </summary>
	[Fact]
	public async Task GeneratedFileName_IsModelNameForJsonDotGDotCs() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class PlayerSettings {
				public string Name { get; set; } = "";
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var generated = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();

		generated.ShouldNotBeEmpty();
		generated.First().HintName.ShouldBe("PlayerSettingsForJson.g.cs",
			"生成ファイル名は {ModelName}ForJson.g.cs であるべき");
	}

	/// <summary>
	/// 生成される DTO がソースモデルと同じ名前空間に配置されることを検証する。
	/// </summary>
	[Fact]
	public async Task DtoNamespace_MatchesSourceModelNamespace() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace My.Deep.Namespace;

			[GenerateR3JsonConfigDto]
			public class DeepModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("namespace My.Deep.Namespace;").ShouldBeTrue(
			"DTO はモデルと同じ名前空間に生成されるべき");
	}

	/// <summary>
	/// モデルがグローバル名前空間にある場合、namespace 宣言が出力されないことを検証する。
	/// </summary>
	[Fact]
	public async Task NamespaceDeclaration_IsOmitted_WhenModelIsInGlobalNamespace() {
		var source = """
			using R3.JsonConfig.Attributes;

			[GenerateR3JsonConfigDto]
			public class GlobalModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("namespace ").ShouldBeFalse(
			"グローバル名前空間の場合は namespace 行が出力されないべき");
	}

	/// <summary>
	/// 生成される DTO が partial class として宣言されることを検証する。
	/// partial にすることで、利用側が別ファイルでメンバーを追加できる。
	/// </summary>
	[Fact]
	public async Task GeneratedDto_IsPartialClass() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class SomeModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public partial class SomeModelForJson").ShouldBeTrue(
			"DTO は partial class として生成されるべき");
	}

	#endregion

	#region 3. Property collection rules

	/// <summary>
	/// public 以外（internal・protected・private）のプロパティが DTO に含まれないことを検証する。
	/// </summary>
	[Fact]
	public async Task NonPublicProperties_AreSkipped() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class VisibilityModel {
				public int PublicProp { get; set; }
				internal int InternalProp { get; set; }
				protected int ProtectedProp { get; set; }
				private int PrivateProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("PublicProp").ShouldBeTrue("public プロパティは収集されるべき");
		code.Contains("InternalProp").ShouldBeFalse("internal プロパティはスキップされるべき");
		code.Contains("ProtectedProp").ShouldBeFalse("protected プロパティはスキップされるべき");
		code.Contains("PrivateProp").ShouldBeFalse("private プロパティはスキップされるべき");
	}

	/// <summary>
	/// [ExcludeProperty] が付与されたプロパティが、型・アクセシビリティに関わらず DTO から除外されることを検証する。
	/// </summary>
	[Fact]
	public async Task PropertiesWithExcludeAttribute_AreSkipped() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class ExcludeModel {
				public int Included { get; set; }

				[ExcludeProperty]
				public int Excluded { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("Included").ShouldBeTrue("ExcludeProperty なしプロパティは収集されるべき");
		code.Contains("Excluded").ShouldBeFalse("[ExcludeProperty] 付きプロパティは DTO から除外されるべき");
	}

	/// <summary>
	/// ReactiveProperty&lt;T&gt; は .Value 経由で値を設定するため、public setter がなくても収集されることを検証する。
	/// </summary>
	[Fact]
	public async Task ReactiveProperty_IsCollected_EvenWithoutSetter() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class RpNoSetterModel {
				public ReactiveProperty<string> Title { get; } = new("");
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public string? Title").ShouldBeTrue(
			"ReactiveProperty<T> は getter のみでも収集されるべき");
	}

	/// <summary>
	/// ObservableList&lt;T&gt; は .Clear()/.Add() 経由で操作するため、public setter がなくても収集されることを検証する。
	/// </summary>
	[Fact]
	public async Task ObservableList_IsCollected_EvenWithoutSetter() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class OlNoSetterModel {
				public ObservableList<int> Numbers { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public int[]? Numbers").ShouldBeTrue(
			"ObservableList<T> は getter のみでも収集されるべき");
	}

	/// <summary>
	/// 通常プロパティは public setter がない場合（getter のみ・private setter など）にスキップされることを検証する。
	/// CreateModel で直接代入するには public setter が必須となる。
	/// </summary>
	[Fact]
	public async Task PlainProperty_IsSkipped_WhenNoPublicSetter() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class SetterModel {
				public string WithSetter { get; set; } = "";
				public string ReadOnly { get; } = "fixed";
				public string PrivateSetter { get; private set; } = "";
				public string ProtectedSetter { get; protected set; } = "";
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("WithSetter").ShouldBeTrue("public setter ありは収集されるべき");
		code.Contains("ReadOnly").ShouldBeFalse("getter のみはスキップされるべき");
		code.Contains("PrivateSetter").ShouldBeFalse("private setter はスキップされるべき");
		code.Contains("ProtectedSetter").ShouldBeFalse("protected setter はスキップされるべき");
	}

	/// <summary>
	/// [ExcludeProperty] が ReactiveProperty&lt;T&gt; にも適用され、DTO から除外されることを検証する。
	/// </summary>
	[Fact]
	public async Task ExcludeAttribute_AlsoAppliesTo_ReactiveProperty() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class RpExcludeModel {
				public ReactiveProperty<string> Kept { get; } = new("");

				[ExcludeProperty]
				public ReactiveProperty<string> Removed { get; } = new("");
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("Kept").ShouldBeTrue("ExcludeProperty なしは収集されるべき");
		code.Contains("Removed").ShouldBeFalse(
			"[ExcludeProperty] は ReactiveProperty にも適用されるべき");
	}

	/// <summary>
	/// [ExcludeProperty] が ObservableList&lt;T&gt; にも適用され、DTO から除外されることを検証する。
	/// </summary>
	[Fact]
	public async Task ExcludeAttribute_AlsoAppliesTo_ObservableList() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class OlExcludeModel {
				public ObservableList<int> Kept { get; } = new();

				[ExcludeProperty]
				public ObservableList<int> Removed { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("Kept").ShouldBeTrue("ExcludeProperty なしは収集されるべき");
		code.Contains("Removed").ShouldBeFalse(
			"[ExcludeProperty] は ObservableList にも適用されるべき");
	}

	#endregion

	#region 4. Type conversion rules

	/// <summary>
	/// 通常プロパティの型（int・string・double など）が DTO では nullable（T?）にマッピングされることを検証する。
	/// </summary>
	[Fact]
	public async Task PlainProperty_IsMappedTo_NullableT() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class PlainTypeModel {
				public int IntProp { get; set; }
				public string StringProp { get; set; } = "";
				public double DoubleProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public int? IntProp").ShouldBeTrue("int → int?");
		code.Contains("public string? StringProp").ShouldBeTrue("string → string?");
		code.Contains("public double? DoubleProp").ShouldBeTrue("double → double?");
	}

	/// <summary>
	/// ReactiveProperty&lt;T&gt; が DTO では nullable な T? プロパティにマッピングされることを検証する。
	/// </summary>
	[Fact]
	public async Task ReactivePropertyOfT_IsMappedTo_NullableT() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class RpTypeModel {
				public ReactiveProperty<string> StringRp { get; } = new("");
				public ReactiveProperty<int> IntRp { get; } = new(0);
				public ReactiveProperty<bool> BoolRp { get; } = new(false);
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public string? StringRp").ShouldBeTrue("ReactiveProperty<string> → string?");
		code.Contains("public int? IntRp").ShouldBeTrue("ReactiveProperty<int> → int?");
		code.Contains("public bool? BoolRp").ShouldBeTrue("ReactiveProperty<bool> → bool?");
	}

	/// <summary>
	/// ObservableList&lt;T&gt; が DTO では nullable な配列 T[]? にマッピングされることを検証する。
	/// </summary>
	[Fact]
	public async Task ObservableListOfT_IsMappedTo_NullableTArray() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class OlTypeModel {
				public ObservableList<int> IntList { get; } = new();
				public ObservableList<string> StringList { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public int[]? IntList").ShouldBeTrue("ObservableList<int> → int[]?");
		code.Contains("public string[]? StringList").ShouldBeTrue("ObservableList<string> → string[]?");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持つ型を通常プロパティとして保持する場合、
	/// DTO 側では対応する ForJson 型（例: Child → ChildForJson?）にマッピングされることを検証する。
	/// </summary>
	[Fact]
	public async Task NestedModelWithAttribute_IsMappedTo_ForJsonType_AsPlainProperty() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public Child ChildProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::TestNamespace.ChildForJson? ChildProp").ShouldBeTrue(
			"ネストされた [GenerateR3JsonConfigDto] 付きモデルは ForJson 型に変換されるべき");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持つ型を ReactiveProperty&lt;T&gt; で保持する場合、
	/// DTO 側では TForJson? にマッピングされることを検証する。
	/// </summary>
	[Fact]
	public async Task NestedModelWithAttribute_IsMappedTo_ForJsonType_AsReactiveProperty() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public ReactiveProperty<Child> ChildRp { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::TestNamespace.ChildForJson? ChildRp").ShouldBeTrue(
			"ReactiveProperty<NestedModel> は NestedModelForJson? に変換されるべき");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持つ型を ObservableList&lt;T&gt; で保持する場合、
	/// DTO 側では TForJson[]? にマッピングされることを検証する。
	/// </summary>
	[Fact]
	public async Task NestedModelWithAttribute_IsMappedTo_ForJsonType_AsObservableList() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public ObservableList<Child> Children { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::TestNamespace.ChildForJson[]? Children").ShouldBeTrue(
			"ObservableList<NestedModel> は NestedModelForJson[]? に変換されるべき");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持たないネスト型は ForJson 化されず、
	/// 通常プロパティ・ReactiveProperty・ObservableList のいずれでも元の型のまま DTO に出力されることを検証する。
	/// </summary>
	[Fact]
	public async Task NestedModelWithoutAttribute_IsKeptAsOriginalType() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			public class PlainChild {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public PlainChild DirectProp { get; set; }
				public ReactiveProperty<PlainChild> RpProp { get; } = new();
				public ObservableList<PlainChild> ListProp { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		// [GenerateR3JsonConfigDto] がない型は ForJson 化されず元の型のまま
		code.Contains("global::TestNamespace.PlainChild? DirectProp").ShouldBeTrue(
			"属性なし通常プロパティは PlainChild? のまま");
		code.Contains("global::TestNamespace.PlainChild? RpProp").ShouldBeTrue(
			"属性なし ReactiveProperty は PlainChild? のまま");
		code.Contains("global::TestNamespace.PlainChild[]? ListProp").ShouldBeTrue(
			"属性なし ObservableList は PlainChild[]? のまま");
	}

	/// <summary>
	/// 既に T? と宣言されている型が DTO 出力で T?? にならない（二重 ? が発生しない）ことを検証する。
	/// </summary>
	[Fact]
	public async Task AlreadyNullableType_DoesNotProduceDoubleQuestionMark() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class NullableModel {
				public int? NullableInt { get; set; }
				public string? NullableString { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public int? NullableInt").ShouldBeTrue("int? は int? のまま");
		code.Contains("public string? NullableString").ShouldBeTrue("string? は string? のまま");
		code.Contains("???").ShouldBeFalse("三重の ? が含まれてはいけない"); // Changed from ?? to ??? because ?? is now used for null-coalescing
	}

	/// <summary>
	/// ReactiveProperty・ObservableList・通常プロパティのいずれも、生成される DTO プロパティが
	/// { get; set; } を持つことを検証する。
	/// </summary>
	[Fact]
	public async Task AllDtoProperties_HaveGetterAndSetter() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class FullModel {
				public ReactiveProperty<string> RpProp { get; } = new("");
				public ObservableList<int> ListProp { get; } = new();
				public int PlainProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		// DTO のプロパティはすべて { get; set; } を持つべき
		var lines = code.Split('\n').Select(l => l.Trim()).ToArray();
		var propNames = new[] { "RpProp", "ListProp", "PlainProp" };
		foreach (var name in propNames) {
			var idx = Array.FindIndex(lines, l => l.Contains(name));
			idx.ShouldBeGreaterThan(-1, $"{name} が生成コードに含まれるべき");
			var block = string.Join(" ", lines.Skip(idx).Take(4));
			block.Contains("get;").ShouldBeTrue($"{name} の DTO プロパティに getter があるべき");
			block.Contains("set;").ShouldBeTrue($"{name} の DTO プロパティに setter があるべき");
		}
	}

	#endregion

	#region 5. Conversion methods

	/// <summary>
	/// CreateModel のシグネチャが
	/// public static TModel? CreateModel(TModelForJson? json, System.IServiceProvider sp)
	/// であることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateModel_HasCorrectSignature() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public static global::TestNamespace.MyModel? CreateModel(global::TestNamespace.MyModelForJson? json, global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver? resolver = null)").ShouldBeTrue(
			"CreateModel は (DtoForJson? json, IServiceProvider sp) → Model? のシグネチャであるべき");
	}

	/// <summary>
	/// CreateJson のシグネチャが
	/// public static TModelForJson? CreateJson(TModel? model)
	/// であることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_HasCorrectSignature() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("public static global::TestNamespace.MyModelForJson? CreateJson(global::TestNamespace.MyModel? model, global::R3.JsonConfig.ReferenceTracker? tracker = null)").ShouldBeTrue(
			"CreateJson は (Model? model) → DtoForJson? のシグネチャであるべき");
	}

	/// <summary>
	/// CreateModel が new ではなく sp.GetRequiredService&lt;TModel&gt;() を使って
	/// DI コンテナからモデルを取得することを検証する。
	/// </summary>
	[Fact]
	public async Task CreateModel_ResolvesModelFromDiContainer() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::TestNamespace.MyModel>(sp)").ShouldBeTrue(
			"CreateModel は IServiceProvider.GetRequiredService でモデルを取得すべき");
	}

	/// <summary>
	/// CreateModel・CreateJson の両方に [NotNullIfNotNull] 属性が付与されており、
	/// null 入力に対する null 返却が静的解析で正しく伝搬することを検証する。
	/// </summary>
	[Fact]
	public async Task BothMethods_HaveNotNullIfNotNullAttribute() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]").ShouldBeTrue(
			"CreateModel に NotNullIfNotNull(json) が付与されるべき");
		code.Contains("[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]").ShouldBeTrue(
			"CreateJson に NotNullIfNotNull(model) が付与されるべき");
	}

	/// <summary>
	/// CreateModel に null ガード（json が null なら即 null を返す）が生成されることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateModel_HasNullGuard_ForNullInput() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("if(json is null)").ShouldBeTrue("CreateModel に null ガードがあるべき");
	}

	/// <summary>
	/// CreateJson に null ガード（model が null なら即 null を返す）が生成されることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_HasNullGuard_ForNullInput() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("if (model is null)").ShouldBeTrue("CreateJson に null ガードがあるべき");
	}

	/// <summary>
	/// 通常プロパティに対する CreateModel の反映ロジックが model.Prop = e の直接代入であることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateModel_AssignsDirectly_ForPlainProperty() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class PlainModel {
				public int Score { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("model.Score = e;").ShouldBeTrue(
			"通常プロパティは model.Prop = value で直接代入されるべき");
	}

	/// <summary>
	/// ReactiveProperty に対する CreateModel の反映ロジックが model.Prop.Value = e であることを検証する。
	/// ReactiveProperty インスタンス自体を差し替えるのではなく、Value を更新する。
	/// </summary>
	[Fact]
	public async Task CreateModel_AssignsToValue_ForReactiveProperty() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class RpModel {
				public ReactiveProperty<string> Title { get; } = new("");
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("model.Title.Value = e;").ShouldBeTrue(
			"ReactiveProperty は model.Prop.Value = value で Value を更新すべき");
	}

	/// <summary>
	/// ObservableList に対する CreateModel の反映ロジックが Clear() → Add() の順で
	/// リストを再構築することを検証する。既存のリストインスタンスは維持される。
	/// </summary>
	[Fact]
	public async Task CreateModel_ClearsAndAdds_ForObservableList() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class OlModel {
				public ObservableList<int> Items { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("model.Items.Clear()").ShouldBeTrue(
			"ObservableList は Clear() でリストをクリアすべき");
		code.Contains("model.Items.Add(").ShouldBeTrue(
			"ObservableList は Add() で要素を再追加すべき");
	}

	/// <summary>
	/// 通常プロパティに対する CreateJson の変換ロジックが model.Prop をそのまま代入することを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_CopiesDirectly_ForPlainProperty() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class PlainModel {
				public int Score { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("Score = model.Score,").ShouldBeTrue(
			"CreateJson で通常プロパティは model.Prop でそのまま取得されるべき");
	}

	/// <summary>
	/// ReactiveProperty に対する CreateJson の変換ロジックが model.Prop.Value で
	/// 内部値を取り出すことを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_ExtractsValue_ForReactiveProperty() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class RpModel {
				public ReactiveProperty<string> Title { get; } = new("");
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("Title = model.Title.Value,").ShouldBeTrue(
			"CreateJson で ReactiveProperty は model.Prop.Value で値を取得すべき");
	}

	/// <summary>
	/// ObservableList に対する CreateJson の変換ロジックが .ToArray() で配列に変換することを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_CallsToArray_ForObservableList() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class OlModel {
				public ObservableList<int> Items { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("Items = global::System.Linq.Enumerable.ToArray(model.Items),").ShouldBeTrue(
			"CreateJson で ObservableList は .ToArray() で配列に変換されるべき");
	}

	/// <summary>
	/// ネストされた ForJson 型を持つプロパティに対して、CreateModel 内で
	/// 子 DTO の CreateModel が再帰的に呼び出されることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateModel_RecursivelyCallsChildCreateModel_ForNestedForJsonType() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public Child ChildProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::TestNamespace.ChildForJson.CreateModel(e, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver)").ShouldBeTrue(
			"ネストされた ForJson 型の CreateModel 内で子の CreateModel が再帰呼び出しされるべき");
	}

	/// <summary>
	/// ネストされた ForJson 型を持つプロパティに対して、CreateJson 内で
	/// 子 DTO の CreateJson が再帰的に呼び出されることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_RecursivelyCallsChildCreateJson_ForNestedForJsonType() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public Child ChildProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::TestNamespace.ChildForJson.CreateJson(model.ChildProp, tracker)").ShouldBeTrue(
			"ネストされた ForJson 型の CreateJson 内で子の CreateJson が再帰呼び出しされるべき");
	}

	/// <summary>
	/// ネストされた ForJson 型の CreateModel では sp.CreateScope().ServiceProvider, resolver で
	/// 新しい DI スコープが作成されることを検証する。スコープ付きサービスがネスト単位で分離される。
	/// </summary>
	[Fact]
	public async Task CreateModel_CreatesNewDiScope_ForNestedForJsonType() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public Child ChildProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver").ShouldBeTrue(
			"ネストされた ForJson 型の CreateModel では新しい DI スコープが作成されるべき");
	}

	/// <summary>
	/// ObservableList のネスト ForJson 型に対する CreateJson で
	/// .Select(x => TForJson.CreateJson(x)).ToArray() が生成されることを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_UsesSelectAndToArray_ForObservableListOfNestedForJsonType() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class Child {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class Parent {
				public ObservableList<Child> Children { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var parentCode = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ParentForJson.g.cs")
			.SourceText.ToString();

		parentCode.Contains("global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(model.Children, x => global::TestNamespace.ChildForJson.CreateJson(x, tracker)))").ShouldBeTrue(
			"ObservableList<NestedModel> の CreateJson では Select + CreateJson + ToArray が使われるべき");
	}

	/// <summary>
	/// 生成されたソースファイルの先頭に #nullable enable ディレクティブが含まれることを検証する。
	/// </summary>
	[Fact]
	public async Task GeneratedCode_ContainsNullableEnableDirective() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("#nullable enable").ShouldBeTrue("生成コードに #nullable enable が含まれるべき");
	}

	/// <summary>
	/// 生成されたソースファイルに // &lt;auto-generated /&gt; コメントが含まれることを検証する。
	/// ツールが自動生成ファイルと認識できるようにするための標準コメント。
	/// </summary>
	[Fact]
	public async Task GeneratedCode_ContainsAutoGeneratedComment() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class MyModel {
				public int Value { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("<auto-generated />").ShouldBeTrue("生成コードに auto-generated コメントが含まれるべき");
	}

	#endregion

	#region Combined scenario

	/// <summary>
	/// 通常プロパティ・ReactiveProperty・ObservableList・ネスト ForJson・ExcludeProperty・getter のみ
	/// のすべての種別を含む単一のモデルから、DTO が正しく生成されることを統合的に検証する。
	/// 属性付きモデルが 2 つあるため、DTO ファイルも 2 つ生成されることも確認する。
	/// </summary>
	[Fact]
	public async Task AllPropertyKindsCombined_ProduceCorrectDto() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public class ChildModel {
				public string Name { get; set; } = "";
			}

			[GenerateR3JsonConfigDto]
			public class FullModel {
				public string PlainStr { get; set; } = "";
				public int PlainInt { get; set; }
				public ReactiveProperty<string> RpStr { get; } = new("");
				public ReactiveProperty<int> RpInt { get; } = new(0);
				public ReactiveProperty<ChildModel> RpChild { get; } = new();
				public ObservableList<int> OlInt { get; } = new();
				public ObservableList<string> OlStr { get; } = new();
				public ObservableList<ChildModel> OlChild { get; } = new();
				public ChildModel NestedChild { get; set; }

				[ExcludeProperty]
				public string Excluded { get; set; } = "";

				public string NoSetter { get; } = "";
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
		var sources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		sources.Length.ShouldBe(2, "FullModelForJson と ChildModelForJson の 2 ファイルが生成されるべき");

		var fullCode = sources.First(s => s.HintName == "FullModelForJson.g.cs").SourceText.ToString();

		// 通常プロパティ
		fullCode.Contains("public string? PlainStr").ShouldBeTrue("通常 string → string?");
		fullCode.Contains("public int? PlainInt").ShouldBeTrue("通常 int → int?");

		// ReactiveProperty
		fullCode.Contains("public string? RpStr").ShouldBeTrue("ReactiveProperty<string> → string?");
		fullCode.Contains("public int? RpInt").ShouldBeTrue("ReactiveProperty<int> → int?");
		fullCode.Contains("global::TestNamespace.ChildModelForJson? RpChild").ShouldBeTrue(
			"ReactiveProperty<ChildModel> → ChildModelForJson?");

		// ObservableList
		fullCode.Contains("public int[]? OlInt").ShouldBeTrue("ObservableList<int> → int[]?");
		fullCode.Contains("public string[]? OlStr").ShouldBeTrue("ObservableList<string> → string[]?");
		fullCode.Contains("global::TestNamespace.ChildModelForJson[]? OlChild").ShouldBeTrue(
			"ObservableList<ChildModel> → ChildModelForJson[]?");

		// ネストされた通常プロパティ
		fullCode.Contains("global::TestNamespace.ChildModelForJson? NestedChild").ShouldBeTrue(
			"ChildModel → ChildModelForJson?");

		// 除外
		fullCode.Contains("Excluded").ShouldBeFalse("[ExcludeProperty] 付きは除外されるべき");

		// setter なし通常プロパティ
		fullCode.Contains("NoSetter").ShouldBeFalse("public setter なし通常プロパティは除外されるべき");

		// CreateModel / CreateJson の両方が存在する
		fullCode.Contains("public static global::TestNamespace.FullModel? CreateModel(").ShouldBeTrue("CreateModel が生成されるべき");
		fullCode.Contains("public static global::TestNamespace.FullModelForJson? CreateJson(").ShouldBeTrue("CreateJson が生成されるべき");
	}

	#endregion

	#region Custom type with JsonConverter

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持たないカスタム型を ReactiveProperty&lt;T?&gt; で保持する場合、
	/// DTO 側では T? のままマッピングされることを検証する（ForJson 化されない）。
	/// Color など JsonConverter で扱う型はこのルートを通る。
	/// </summary>
	[Fact]
	public async Task CustomType_WithoutAttribute_IsMappedTo_NullableT_AsReactiveProperty() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public ReactiveProperty<HexColor?> ColorRp { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("global::TestNamespace.HexColor? ColorRp").ShouldBeTrue(
			"ReactiveProperty<HexColor?> は HexColor? にマッピングされるべき（ForJson 化されない）");
		code.Contains("HexColorForJson").ShouldBeFalse(
			"[GenerateR3JsonConfigDto] がない型は ForJson 化されてはいけない");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持たないカスタム型を ObservableList&lt;T?&gt; で保持する場合、
	/// DTO 側では T?[]? のままマッピングされることを検証する（ForJson 化されない）。
	/// Color など JsonConverter で扱う型はこのルートを通る。
	/// </summary>
	[Fact]
	public async Task CustomType_WithoutAttribute_IsMappedTo_NullableTArray_AsObservableList() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public ObservableList<HexColor?> ColorList { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("global::TestNamespace.HexColor?[]? ColorList").ShouldBeTrue(
			"ObservableList<HexColor?> は HexColor?[]? にマッピングされるべき（ForJson 化されない）");
		code.Contains("HexColorForJson").ShouldBeFalse(
			"[GenerateR3JsonConfigDto] がない型は ForJson 化されてはいけない");
	}

	/// <summary>
	/// [GenerateR3JsonConfigDto] を持たないカスタム型を通常プロパティとして保持する場合、
	/// DTO 側では T? のままマッピングされることを検証する（ForJson 化されない）。
	/// </summary>
	[Fact]
	public async Task CustomType_WithoutAttribute_IsMappedTo_NullableT_AsPlainProperty() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public HexColor? ColorProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("global::TestNamespace.HexColor? ColorProp").ShouldBeTrue(
			"HexColor? 通常プロパティ は HexColor? のままマッピングされるべき");
		code.Contains("HexColorForJson").ShouldBeFalse(
			"[GenerateR3JsonConfigDto] がない型は ForJson 化されてはいけない");
	}

	/// <summary>
	/// ReactiveProperty&lt;T?&gt; のカスタム型に対する CreateModel の反映ロジックが
	/// model.Prop.Value = e であることを検証する。
	/// JsonConverter が担う型変換はジェネレータのコードに含まれない。
	/// </summary>
	[Fact]
	public async Task CreateModel_AssignsToValue_ForReactivePropertyOfCustomNullableType() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public ReactiveProperty<HexColor?> ColorRp { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("model.ColorRp.Value = e;").ShouldBeTrue(
			"ReactiveProperty<HexColor?> は model.Prop.Value = e で値を設定すべき");
	}

	/// <summary>
	/// ReactiveProperty&lt;T?&gt; のカスタム型に対する CreateJson の変換ロジックが
	/// model.Prop.Value で内部値を取り出すことを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_ExtractsValue_ForReactivePropertyOfCustomNullableType() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public ReactiveProperty<HexColor?> ColorRp { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("ColorRp = model.ColorRp.Value,").ShouldBeTrue(
			"CreateJson で ReactiveProperty<HexColor?> は model.Prop.Value で値を取得すべき");
	}

	/// <summary>
	/// ObservableList&lt;T?&gt; のカスタム型に対する CreateJson の変換ロジックが
	/// .ToArray() で配列に変換することを検証する。
	/// </summary>
	[Fact]
	public async Task CreateJson_CallsToArray_ForObservableListOfCustomNullableType() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public ObservableList<HexColor?> ColorList { get; } = new();
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("ColorList = global::System.Linq.Enumerable.ToArray(model.ColorList),").ShouldBeTrue(
			"CreateJson で ObservableList<HexColor?> は .ToArray() で配列に変換されるべき");
	}

	/// <summary>
	/// カスタム型を含む全プロパティ種別（ReactiveProperty・ObservableList・通常）が
	/// 1 つのモデルに混在する場合でもコンパイルエラーなく DTO が生成されることを検証する。
	/// </summary>
	[Fact]
	public async Task AllCustomTypePropertyKinds_ProduceCorrectDto_WithoutCompileError() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			public struct HexColor { }

			[GenerateR3JsonConfigDto]
			public class Model {
				public ReactiveProperty<HexColor?> ColorRp   { get; } = new();
				public ObservableList<HexColor?>   ColorList  { get; } = new();
				public HexColor?                   ColorProp  { get; set; }
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
		var code = runResult.Results.SelectMany(r => r.GeneratedSources).First().SourceText.ToString();

		code.Contains("global::TestNamespace.HexColor? ColorRp").ShouldBeTrue("ReactiveProperty<HexColor?> → HexColor?");
		code.Contains("global::TestNamespace.HexColor?[]? ColorList").ShouldBeTrue("ObservableList<HexColor?> → HexColor?[]?");
		code.Contains("global::TestNamespace.HexColor? ColorProp").ShouldBeTrue("HexColor? 通常プロパティ → HexColor?");
	}

	#endregion

	#region 6. Polymorphism support

	/// <summary>
	/// [JsonConfigDerivedType] を持つ基底型（インターフェース/クラス）から、
	/// STJ のポリモーフィック属性を持つ DTO が生成されることを検証する。
	/// </summary>
	[Fact]
	public async Task PolymorphicBase_GeneratesDtoWithStjAttributes() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public interface IBase { }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("A")]
			public class SubA : IBase { public int A { get; set; } }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("B")]
			public class SubB : IBase { public int B { get; set; } }
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "IBaseForJson.g.cs")
			.SourceText.ToString();

		code.ShouldContain("[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"___Type\")]");
		code.ShouldContain("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(global::TestNamespace.SubAForJson), \"A\")]");
		code.ShouldContain("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(global::TestNamespace.SubBForJson), \"B\")]");
	}

	/// <summary>
	/// ポリモーフィックな基底型の CreateModel/CreateJson が、派生型へのディスパッチロジックを持つことを検証する。
	/// </summary>
	[Fact]
	public async Task PolymorphicBase_GeneratesDispatchLogic() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public interface IBase { }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("A")]
			public class SubA : IBase { public int A { get; set; } }
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "IBaseForJson.g.cs")
			.SourceText.ToString();

		// CreateModel のディスパッチ
		code.ShouldContain("if (json is global::TestNamespace.SubAForJson e_global_TestNamespace_SubA)");
		code.ShouldContain("return global::TestNamespace.SubAForJson.CreateModel(e_global_TestNamespace_SubA, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver)");

		// CreateJson のディスパッチ
		code.ShouldContain("if (model is global::TestNamespace.SubA m_global_TestNamespace_SubA)");
		code.ShouldContain("return global::TestNamespace.SubAForJson.CreateJson(m_global_TestNamespace_SubA, tracker)");

		// 未知の型へのガード
		code.ShouldContain("throw new global::System.InvalidOperationException($\"Unknown derived type: {json?.GetType().FullName}\");");
	}

	/// <summary>
	/// プロパティの型が [GenerateR3JsonConfigDto] を持つインターフェースである場合、
	/// DTO 側ではそのインターフェースの ForJson 型にマッピングされ、
	/// 変換メソッドが再帰的に呼ばれることを検証する。
	/// </summary>
	[Fact]
	public async Task PropertyOfPolymorphicInterface_IsMappedToInterfaceDto() {
		var source = """
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public interface IBase { }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("A")]
			public class SubA : IBase { public int A { get; set; } }

			[GenerateR3JsonConfigDto]
			public class Container {
				public IBase BaseProp { get; set; }
			}
			""";

		var (runResult, _) = await TestHelper.RunGenerator(source);
		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ContainerForJson.g.cs")
			.SourceText.ToString();

		// プロパティ型
		code.ShouldContain("global::TestNamespace.IBaseForJson? BaseProp");

		// 変換ロジック
		code.ShouldContain("global::TestNamespace.IBaseForJson.CreateModel(e, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver);");
		code.ShouldContain("BaseProp = global::TestNamespace.IBaseForJson.CreateJson(model.BaseProp, tracker)");
	}

	/// <summary>
	/// ReactiveProperty&lt;IInterface&gt; のジェネリクス型がポリモーフィックなインターフェースの場合、
	/// DTO 側ではそのインターフェースの ForJson 型（IBaseForJson?）にマッピングされ、
	/// CreateModel/CreateJson で再帰的に変換メソッドが呼ばれることを検証する。
	/// </summary>
	[Fact]
	public async Task ReactivePropertyOfPolymorphicInterface_IsMappedToInterfaceDto() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public interface IBase { }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("A")]
			public class SubA : IBase { public int A { get; set; } }

			[GenerateR3JsonConfigDto]
			public class Container {
				public ReactiveProperty<IBase> BaseRp { get; } = new();
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty(
			"ReactiveProperty<IInterface> でコンパイルエラーが発生してはいけない");

		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ContainerForJson.g.cs")
			.SourceText.ToString();

		// プロパティ型
		code.Contains("global::TestNamespace.IBaseForJson? BaseRp").ShouldBeTrue(
			"ReactiveProperty<IBase> は IBaseForJson? にマッピングされるべき");

		// CreateModel のロジック
		code.Contains("model.BaseRp.Value = global::TestNamespace.IBaseForJson.CreateModel(e, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver);").ShouldBeTrue(
			"CreateModel 内でインターフェースの ForJson 型の CreateModel が呼ばれるべき");

		// CreateJson のロジック
		code.Contains("BaseRp = global::TestNamespace.IBaseForJson.CreateJson(model.BaseRp.Value, tracker),").ShouldBeTrue(
			"CreateJson 内でインターフェースの ForJson 型の CreateJson が呼ばれるべき");
	}

	/// <summary>
	/// ObservableList&lt;IInterface&gt; のジェネリクス型がポリモーフィックなインターフェースの場合、
	/// DTO 側ではそのインターフェースの ForJson 配列型（IBaseForJson[]?）にマッピングされ、
	/// CreateModel では Clear/Add、CreateJson では Select + CreateJson + ToArray が使われることを検証する。
	/// </summary>
	[Fact]
	public async Task ObservableListOfPolymorphicInterface_IsMappedToInterfaceDtoArray() {
		var source = """
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public interface IBase { }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("A")]
			public class SubA : IBase { public int A { get; set; } }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("B")]
			public class SubB : IBase { public string B { get; set; } = ""; }

			[GenerateR3JsonConfigDto]
			public class Container {
				public ObservableList<IBase> BaseList { get; } = new();
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty(
			"ObservableList<IInterface> でコンパイルエラーが発生してはいけない");

		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ContainerForJson.g.cs")
			.SourceText.ToString();

		// プロパティ型
		code.Contains("global::TestNamespace.IBaseForJson[]? BaseList").ShouldBeTrue(
			"ObservableList<IBase> は IBaseForJson[]? にマッピングされるべき");

		// CreateModel のロジック
		code.Contains("model.BaseList.Clear()").ShouldBeTrue(
			"ObservableList の CreateModel では Clear() が呼ばれるべき");
		code.Contains("model.BaseList.Add(global::TestNamespace.IBaseForJson.CreateModel(e, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver))").ShouldBeTrue(
			"ObservableList の CreateModel では Add() 内でインターフェースの CreateModel が呼ばれるべき");

		// CreateJson のロジック
		code.Contains("global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(model.BaseList, x => global::TestNamespace.IBaseForJson.CreateJson(x, tracker)))").ShouldBeTrue(
			"ObservableList の CreateJson では Select + CreateJson + ToArray が使われるべき");
	}

	/// <summary>
	/// ReactiveProperty・ObservableList・通常プロパティのすべてでポリモーフィックなインターフェースを
	/// 型引数に持つ場合、1 つのモデルから DTO が正しく生成されることを統合的に検証する。
	/// </summary>
	[Fact]
	public async Task AllPropertyKindsWithPolymorphicInterface_ProduceCorrectDto() {
		var source = """
			using R3;
			using R3.JsonConfig.Attributes;
			using ObservableCollections;

			namespace TestNamespace;

			[GenerateR3JsonConfigDto]
			public interface IBase { }

			[GenerateR3JsonConfigDto]
			[JsonConfigDerivedType("A")]
			public class SubA : IBase { public int A { get; set; } }

			[GenerateR3JsonConfigDto]
			public class Container {
				public IBase PlainBase { get; set; }
				public ReactiveProperty<IBase> RpBase { get; } = new();
				public ObservableList<IBase> ListBase { get; } = new();
			}
			""";

		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();

		var code = runResult.Results
			.SelectMany(r => r.GeneratedSources)
			.First(s => s.HintName == "ContainerForJson.g.cs")
			.SourceText.ToString();

		code.Contains("global::TestNamespace.IBaseForJson? PlainBase").ShouldBeTrue(
			"通常プロパティ IBase → IBaseForJson?");
		code.Contains("global::TestNamespace.IBaseForJson? RpBase").ShouldBeTrue(
			"ReactiveProperty<IBase> → IBaseForJson?");
		code.Contains("global::TestNamespace.IBaseForJson[]? ListBase").ShouldBeTrue(
			"ObservableList<IBase> → IBaseForJson[]?");
	}

	#endregion
}