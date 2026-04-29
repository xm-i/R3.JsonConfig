namespace R3.JsonConfig;

/// <summary>
/// JSON 設定ジェネレータが任意のラッパー型を扱うためのアダプター契約。
/// ジェネレータはこのインターフェースを実装したアダプター型を介してラッパーの値を読み書きする。
/// </summary>
/// <typeparam name="TWrapper">ラッパー型（例: ReactiveProperty&lt;T&gt;）。</typeparam>
/// <typeparam name="TInner">ラッパーが保持する内部値の型。</typeparam>
public interface IJsonConfigWrapper<TWrapper, TInner> {
	/// <summary>ラッパーから内部値を取得する。</summary>
	public TInner Get(TWrapper wrapper);

	/// <summary>ラッパーへ内部値を設定する。</summary>
	public void Set(TWrapper wrapper, TInner value);
}