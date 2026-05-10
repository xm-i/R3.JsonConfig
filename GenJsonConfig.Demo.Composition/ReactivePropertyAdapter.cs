using GenJsonConfig;
using R3;

namespace GenJsonConfig.Demo.Composition;

/// <summary>
/// ReactiveProperty&lt;T&gt; 用の IJsonConfigWrapper アダプター。
/// [assembly: RegisterJsonConfigWrapper] で登録されることにより、ジェネレータが
/// ReactiveProperty を含むプロパティのアクセスコードを自動生成する際に参照される。
/// </summary>
public sealed class ReactivePropertyAdapter<T> : IJsonConfigWrapper<ReactiveProperty<T>, T> {
	/// <inheritdoc />
	public T Get(ReactiveProperty<T> wrapper) {
		return wrapper.Value;
	}

	/// <inheritdoc />
	public void Set(ReactiveProperty<T> wrapper, T value) {
		wrapper.Value = value;
	}
}