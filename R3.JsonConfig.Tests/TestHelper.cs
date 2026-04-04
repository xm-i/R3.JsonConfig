using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using R3.JsonConfig.Generators;
using System.Collections.Immutable;
using System.Reflection;

namespace R3.JsonConfig.Tests;

public static class TestHelper {
	public static Task<(GeneratorDriverRunResult RunResult, ImmutableArray<Diagnostic> Diagnostics)> RunGenerator(string source) {
		var syntaxTree = CSharpSyntaxTree.ParseText(source);

		var references = new[] {
			MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(System.Runtime.Serialization.DataContractAttribute).Assembly.Location),
			MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
			MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll")),
			MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(ObservableCollections.ObservableList<>).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(R3.ReactiveProperty<>).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute).Assembly.Location),
		};

		var compilation = CSharpCompilation.Create(
			"Tests",
			new[] { syntaxTree },
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var generator = new DefaultJsonDtoGenerator();
		var driver = CSharpGeneratorDriver.Create(generator);

		driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

		return Task.FromResult((driver.GetRunResult(), diagnostics));
	}
}
