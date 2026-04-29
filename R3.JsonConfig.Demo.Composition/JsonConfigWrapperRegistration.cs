using R3;
using R3.JsonConfig.Attributes;
using R3.JsonConfig.Demo.Composition;

[assembly: RegisterJsonConfigWrapper(typeof(ReactiveProperty<>), typeof(ReactivePropertyAdapter<>))]