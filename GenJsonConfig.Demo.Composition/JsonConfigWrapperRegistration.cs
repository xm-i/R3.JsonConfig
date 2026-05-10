using GenJsonConfig.Attributes;
using GenJsonConfig.Demo.Composition;
using R3;

[assembly: RegisterJsonConfigWrapper(typeof(ReactiveProperty<>), typeof(ReactivePropertyAdapter<>))]