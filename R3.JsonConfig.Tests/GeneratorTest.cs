using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace R3.JsonConfig.Tests;

public class GeneratorTest {
	/// <summary>
	/// 正常なクラス定義から、期待されるDTOのソースコードが正しく生成されることを検証します。
	/// </summary>
	[Fact]
	public async Task ShouldGenerateDto_FromValidClass() {
		// Arrange
		var source = """
using R3;
using R3.JsonConfig.Attributes;
using ObservableCollections;

namespace TestNamespace;

[GenerateR3JsonConfigDto]
public partial class MyConfig {
	public ReactiveProperty<string> Name { get; set; } = new("");
	public ObservableList<int> Scores { get; set; } = new();
	public int Version { get; set; }

""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("コンパイルエラーが発生してはいけません");

		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.ShouldNotBeEmpty("ソースコードが生成される必要があります");

		var generatedSource = generatedSources.First().SourceText.ToString();

		// 生成されたコードの検証
		generatedSource.Contains("public partial class MyConfigForJson").ShouldBeTrue("DTOクラス名が正しく生成されている必要があります");
		generatedSource.Contains("public string? Name").ShouldBeTrue("ReactivePropertyがstring?に変換されている必要があります");
		generatedSource.Contains("public int[]? Scores").ShouldBeTrue("ObservableListがint[]?に変換されている必要があります");
		generatedSource.Contains("public int? Version").ShouldBeTrue("intがint?に変換されている必要があります");
	}

	/// <summary>
	/// GenerateR3JsonConfigDto属性が付与されていないクラスの場合、ソースコードが生成されないことを検証します。
	/// </summary>
	[Fact]
	public async Task ShouldNotGenerateSource_WhenAttributeIsMissing() {
		// Arrange
		var source = """
using R3;
using ObservableCollections;

namespace TestNamespace;

public partial class MyConfig {
	public ReactiveProperty<string> Name { get; set; } = new("");

""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("コンパイルエラーが発生してはいけません");

		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.ShouldBeEmpty("属性がない場合はソースコードが生成されてはいけません");
	}

	/// <summary>
	/// ExcludeProperty属性が付与されたプロパティが、生成されるDTOから除外されることを検証します。
	/// </summary>
	[Fact]
	public async Task ShouldExcludeProperty_WhenExcludePropertyAttributeIsUsed() {
		// Arrange
		var source = """
using R3;
using R3.JsonConfig.Attributes;

namespace TestNamespace;

[GenerateR3JsonConfigDto]
public partial class MyConfig {
	public int IncludedProperty { get; set; }

	[ExcludeProperty]
	public int ExcludedProperty { get; set; }

""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("コンパイルエラーが発生してはいけません");

		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.ShouldNotBeEmpty("ソースコードが生成される必要があります");

		var generatedSource = generatedSources.First().SourceText.ToString();

		generatedSource.Contains("public int? IncludedProperty").ShouldBeTrue("ExcludePropertyがないプロパティは生成される必要があります");
		generatedSource.Contains("ExcludedProperty").ShouldBeFalse("ExcludePropertyがあるプロパティは生成されてはいけません");
	}


	/// <summary>
	/// GenerateR3JsonConfigDtoが付与されたネストされたクラスのプロパティ（ReactivePropertyおよびObservableListを含む）が
	/// 正しくDTOの型（末尾にForJsonが付く型）に変換されることを検証します。
	/// </summary>
	[Fact]
	public async Task ShouldGenerateNestedDtoTypes_WhenNestedClassesHaveGenerateR3JsonConfigDtoAttribute() {
		// Arrange
		var source = """
using R3;
using R3.JsonConfig.Attributes;
using ObservableCollections;

namespace TestNamespace;

[GenerateR3JsonConfigDto]
public class ChildConfig {
	public string Name { get; set; } = "";
}

[GenerateR3JsonConfigDto]
public class ParentConfig {
	public ChildConfig DirectChild { get; set; }
	public ReactiveProperty<ChildConfig> ReactiveChild { get; set; } = new();
	public ObservableList<ChildConfig> ChildList { get; set; } = new();

""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("コンパイルエラーが発生してはいけません");

		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.Length.ShouldBe(2, "2つのクラス(ParentとChild)のソースコードが生成される必要があります");

		var parentGeneratedSource = generatedSources.First(x => x.HintName == "ParentConfigForJson.g.cs").SourceText.ToString();

		parentGeneratedSource.Contains("public TestNamespace.ChildConfigForJson? DirectChild").ShouldBeTrue("ネストされたクラスがForJson付きの型に変換される必要があります");
		parentGeneratedSource.Contains("public TestNamespace.ChildConfigForJson? ReactiveChild").ShouldBeTrue("ReactiveProperty内のネストされたクラスがForJson付きの型に変換される必要があります");
		parentGeneratedSource.Contains("public TestNamespace.ChildConfigForJson[]? ChildList").ShouldBeTrue("ObservableList内のネストされたクラスがForJson付きの配列型に変換される必要があります");
	}


	/// <summary>
	/// privateやprotectedなsetterを持つプロパティ、およびnull許容型プロパティに対する
	/// DTO生成の挙動を検証します。public setterを持たないものは除外され、null許容型は適切に処理されるべきです。
	/// </summary>
	[Fact]
	public async Task ShouldHandleAccessibilityAndNullableProperties_Correctly() {
		// Arrange
		var source = """
using R3;
using R3.JsonConfig.Attributes;

namespace TestNamespace;

[GenerateR3JsonConfigDto]
public class AccessibilityConfig {
	public int PublicProperty { get; set; }
	public int PrivateSetterProperty { get; private set; }
	public int ProtectedSetterProperty { get; protected set; }
	public int InitOnlyProperty { get; init; }
	public int? NullableIntProperty { get; set; }
	public string? NullableStringProperty { get; set; }
}
""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("コンパイルエラーが発生してはいけません");

		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.ShouldNotBeEmpty("ソースコードが生成される必要があります");

		var generatedSource = generatedSources.First().SourceText.ToString();

		// 生成されたコードの検証
		generatedSource.Contains("public int? PublicProperty").ShouldBeTrue("public setterを持つプロパティは生成される必要があります");

		// DefaultJsonDtoGenerator.cs のロジックでは setMethod が Public である必要がある
		generatedSource.Contains("PrivateSetterProperty").ShouldBeFalse("private setterを持つプロパティは生成されてはいけません");
		generatedSource.Contains("ProtectedSetterProperty").ShouldBeFalse("protected setterを持つプロパティは生成されてはいけません");
		// initアクセサは通常publicとみなされるため生成される可能性があるが、ジェネレータの実装に依存する。
		// 現在の DefaultJsonDtoGenerator.cs は DeclaredAccessibility == Accessibility.Public かつ settableProperty を確認している。
		// init は setMethod として取得され Public なら生成される。
		generatedSource.Contains("public int? InitOnlyProperty").ShouldBeTrue("initプロパティ(public)は生成される必要があります");

		// Nullable型は末尾の?が1つになるように処理されるべき
		generatedSource.Contains("public int? NullableIntProperty").ShouldBeTrue("null許容のintプロパティはint?として生成される必要があります");
		generatedSource.Contains("public string? NullableStringProperty").ShouldBeTrue("null許容のstringプロパティはstring?として生成される必要があります");
	}

	/// <summary>
	/// 生成されたDTOクラスに CreateModel メソッドおよび CreateJson メソッドが含まれており、
	/// 期待通りのシグネチャとロジックが生成されることを検証します。
	/// </summary>
	[Fact]
	public async Task ShouldGenerate_CreateModelAndCreateJson_Methods() {
		// Arrange
		var source = """
using R3;
using R3.JsonConfig.Attributes;

namespace TestNamespace;

[GenerateR3JsonConfigDto]
public class MethodCheckConfig {
	public int Value { get; set; }
}
""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("コンパイルエラーが発生してはいけません");

		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.ShouldNotBeEmpty();

		var generatedSource = generatedSources.First().SourceText.ToString();

		// CreateModel メソッドの存在とシグネチャ検証
		generatedSource.Contains("public static MethodCheckConfig? CreateModel(MethodCheckConfigForJson? json, System.IServiceProvider sp)").ShouldBeTrue("CreateModelメソッドが生成される必要があります");

		// CreateJson メソッドの存在とシグネチャ検証
		generatedSource.Contains("public static MethodCheckConfigForJson? CreateJson(MethodCheckConfig? model)").ShouldBeTrue("CreateJsonメソッドが生成される必要があります");

		// CreateModel 内で Value をセットするロジックの存在確認
		generatedSource.Contains("if (json.Value is { } notNullValue)").ShouldBeTrue("CreateModel内で値の割り当てロジックが生成される必要があります");
		generatedSource.Contains("model.Value = e;").ShouldBeTrue("CreateModel内でモデルへのプロパティ割り当てが行われる必要があります");

		// CreateJson 内で Value をセットするロジックの存在確認
		generatedSource.Contains("Value = model.Value,").ShouldBeTrue("CreateJson内でプロパティの初期化ロジックが生成される必要があります");
	}
}