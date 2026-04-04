using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace R3.JsonConfig.Tests;

public class GeneratorTest {
	[Fact]
	public async Task 正常なクラスからDTOが生成されること() {
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
}
""";

		// Act
		var (runResult, diagnostics) = await TestHelper.RunGenerator(source);

		// Assert
		diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty("コンパイルエラーが発生してはいけません");
		
		var generatedSources = runResult.Results.SelectMany(r => r.GeneratedSources).ToArray();
		generatedSources.Should().NotBeEmpty("ソースコードが生成される必要があります");
		
		var generatedSource = generatedSources.First().SourceText.ToString();
		
		// 生成されたコードの検証
		generatedSource.Should().Contain("public partial class MyConfigForJson", "DTOクラス名が正しく生成されている必要があります");
		generatedSource.Should().Contain("public string? Name", "ReactivePropertyがstring?に変換されている必要があります");
		generatedSource.Should().Contain("public int[]? Scores", "ObservableListがint[]?に変換されている必要があります");
		generatedSource.Should().Contain("public int? Version", "intがint?に変換されている必要があります");
	}
}
