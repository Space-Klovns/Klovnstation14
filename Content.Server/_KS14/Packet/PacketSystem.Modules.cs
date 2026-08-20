using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._KS14.Packet.Components;

namespace Content.Server._KS14.Packet;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class PacketSystem
{
    /// <summary>
    ///
    /// </summary>
    private Dictionary<string, Module> _modules = new();

    /// <summary>
    ///
    /// </summary>
    private Dictionary<string, List<ModuleMethod>> _methods = new();

    private void InitializeModules()
    {
        var moduleType = typeof(Module);

        var modules = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => moduleType.IsAssignableFrom(type)
                           && type.IsClass
                           && !type.IsAbstract)
            .ToList();

        foreach (var module in modules)
        {
            object[] args = [EntityManager, _protoMan, this];

            if (Activator.CreateInstance(module, args) is not Module moduleInstance)
                continue;

            _modules.Add(moduleInstance.ModuleId, moduleInstance);
        }

        InitializeMethods();
    }

    private void InitializeMethods()
    {
        var methodType = typeof(ModuleMethod);

        var methods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => methodType.IsAssignableFrom(type)
                           && type.IsClass
                           && !type.IsAbstract)
            .ToList();

        foreach (var method in methods)
        {
            if (Attribute.GetCustomAttribute(method, typeof(ModuleMethodAttribute)) is not ModuleMethodAttribute methodData)
                continue;

            if (!TryGetModule($"{methodData.Method}", out var module)
                || Activator.CreateInstance(method, module) is not ModuleMethod methodInstance)
                continue;

            if (!_methods.ContainsKey($"{methodData.Method}"))
                _methods.Add($"{methodData.Method}", [methodInstance]);
            else
                _methods[$"{methodData.Method}"].Add(methodInstance);
        }
    }

    public bool TryGetModule(string moduleName, [NotNullWhen(returnValue: true)] out Module? module)
    {
        return _modules.TryGetValue(moduleName, out module);
    }

    public void LoadMethods(Entity<ExecutorComponent> ent)
    {
        var engine = EnsureEngine(ent);

        foreach (var moduleName in ent.Comp.Modules)
        {
            if (!_methods.TryGetValue(moduleName, out var methods))
                continue;

            foreach (var method in methods)
            {
                engine.SetValue(method.Id, method.ModuleExec);
            }
        }
    }
}
