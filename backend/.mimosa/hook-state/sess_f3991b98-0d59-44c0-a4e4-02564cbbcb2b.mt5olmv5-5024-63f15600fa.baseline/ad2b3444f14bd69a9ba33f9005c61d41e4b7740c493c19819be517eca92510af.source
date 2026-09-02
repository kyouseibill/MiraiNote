using MiraiNote.CLI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace MiraiNote.CLI;

/// <summary>
/// 将 Spectre.Console.Cli 的 ITypeResolver 桥接到 Microsoft.Extensions.DependencyInjection。
/// </summary>
public sealed class DependencyInjectionRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _builder;
    public DependencyInjectionRegistrar(IServiceCollection builder) { _builder = builder; }

    public ITypeResolver Build()
        => new DependencyInjectionResolver(_builder.BuildServiceProvider());

    public void Register(Type service, Type implementation)
        => _builder.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation)
        => _builder.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory)
        => _builder.AddSingleton(service, _ => factory());
}

public sealed class DependencyInjectionResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;
    public DependencyInjectionResolver(IServiceProvider provider) { _provider = provider; }

    public object? Resolve(Type? type)
        => type == null ? null : _provider.GetService(type);

    public void Dispose()
        => (_provider as IDisposable)?.Dispose();
}
