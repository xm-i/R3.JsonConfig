namespace R3.JsonConfig.Attributes;

/// <summary>
/// アセンブリ内の任意のオープンジェネリック型をラッパーとして JSON 設定ジェネレータに登録する。
/// ジェネレータはこの属性を参照アセンブリを含めて収集し、<see cref="R3.JsonConfig.IJsonConfigWrapper{TWrapper,TInner}"/>
/// を実装したアダプター経由でラッパーのアクセスコードを生成する。
/// </summary>
/// <example>
/// <code>
/// [assembly: RegisterJsonConfigWrapper(typeof(ReactiveProperty&lt;&gt;), typeof(ReactivePropertyAdapter&lt;&gt;))]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RegisterJsonConfigWrapperAttribute : Attribute {
	/// <summary>登録するラッパー型のオープンジェネリック定義（例: <c>typeof(ReactiveProperty&lt;&gt;)</c>）。</summary>
	public Type WrapperOpenGeneric {
		get;
	}

	/// <summary>
	/// <see cref="IJsonConfigWrapper{TWrapper,TInner}"/> を実装したアダプター型のオープンジェネリック定義
	/// （例: <c>typeof(ReactivePropertyAdapter&lt;&gt;)</c>）。
	/// </summary>
	public Type AdapterOpenGeneric {
		get;
	}

	/// <param name="wrapperOpenGeneric">ラッパー型のオープンジェネリック定義。</param>
	/// <param name="adapterOpenGeneric">アダプター型のオープンジェネリック定義。</param>
	public RegisterJsonConfigWrapperAttribute(Type wrapperOpenGeneric, Type adapterOpenGeneric) {
		this.WrapperOpenGeneric = wrapperOpenGeneric;
		this.AdapterOpenGeneric = adapterOpenGeneric;
	}
}