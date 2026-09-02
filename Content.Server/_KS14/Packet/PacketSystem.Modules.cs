using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._KS14.Packet.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;

namespace Content.Server._KS14.Packet;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class PacketSystem
{
    /// <summary>
    ///
    /// </summary>
    private Dictionary<Entity<ExecutorComponent>, Dictionary<string, PacketModule>> _modules = new();

    /// <summary>
    ///
    /// </summary>
    private Dictionary<Entity<ExecutorComponent>, Dictionary<string, List<ModuleMethod>>> _methods = new();

    private List<Type> _moduleTypes = [];
    private List<Type> _methodTypes = [];

    private void PreInitJint()
    {
        _moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(PacketModule).IsAssignableFrom(type)
                           && type.IsClass
                           && !type.IsAbstract)
            .ToList();

        _methodTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(ModuleMethod).IsAssignableFrom(type)
                           && type.IsClass
                           && !type.IsAbstract)
            .ToList();
    }

    private void InitializeModules(Entity<ExecutorComponent> ent)
    {
        Dictionary<string, PacketModule> modDict = [];

        foreach (var module in _moduleTypes)
        {
            if (!ent.Comp.Modules.Contains(module.Name))
                continue;

            object[] args = [EntityManager, _prototypeManager, this];
            if (Activator.CreateInstance(module, args) is not PacketModule moduleInstance)
                continue;

            moduleInstance.Executor = ent;
            if (TryComp<PacketNetworkComponent>(moduleInstance.Executor, out var packetNetwork))
                moduleInstance.Executor.Comp2 = packetNetwork;

            Logger.Info(module.Name);

            modDict.Add(moduleInstance.ModuleId, moduleInstance);
        }
        _modules.Add(ent, modDict);

        InitializeMethods(ent);
    }

    private void InitializeMethods(Entity<ExecutorComponent> ent)
    {
        Dictionary<string, List<ModuleMethod>> methodDict = [];

        foreach (var method in _methodTypes)
        {
            if (Attribute.GetCustomAttribute(method, typeof(ModuleMethodAttribute)) is not ModuleMethodAttribute methodData
                || !ent.Comp.Modules.Contains($"{methodData.Method}"))
                continue;

            if (!TryGetModule(ent, $"{methodData.Method}", out var module)
                || Activator.CreateInstance(method, module) is not ModuleMethod methodInstance)
                continue;

            if (!methodDict.ContainsKey($"{methodData.Method}"))
                methodDict.Add($"{methodData.Method}", [methodInstance]);
            else
                methodDict[$"{methodData.Method}"].Add(methodInstance);

            Logger.Info(method.Name);
        }

        _methods.Add(ent, methodDict);
    }

    public bool TryGetModule(Entity<ExecutorComponent> ent, string moduleName, [NotNullWhen(returnValue: true)] out PacketModule? module)
    {
        module = null;

        if (!_modules.TryGetValue(ent, out var moduleDict))
            return false;

        return moduleDict.TryGetValue(moduleName, out module);
    }

    public bool TryGetMethods(Entity<ExecutorComponent> ent, string moduleName, [NotNullWhen(returnValue: true)] out List<ModuleMethod>? methods)
    {
        methods = [];

        if (!_methods.TryGetValue(ent, out var moduleDict))
            return false;

        return moduleDict.TryGetValue(moduleName, out methods);
    }

    public bool TryFindMethod(List<ModuleMethod> methods, Type methodType, [NotNullWhen(returnValue: true)] out ModuleMethod? foundMethod)
    {
        foundMethod = null;

        foreach (var method in methods)
        {
            if (method.GetType() != methodType)
                continue;

            foundMethod = method;
            return true;
        }

        return false;
    }

    public void LoadMethods(Entity<ExecutorComponent> ent)
    {
        var engine = EnsureEngine(ent);

        foreach (var moduleName in ent.Comp.Modules)
        {
            if (!TryGetMethods(ent, moduleName, out var methods))
                return;

            foreach (var method in methods)
            {
                engine.SetValue(method.Id, method.ModuleExec);
            }
        }
    }

    private void ReloadEngine(Entity<ExecutorComponent> ent, ItemSlotsComponent slotsComponent)
    {
        Logger.Info("Reaload");
        DisposeEngine(ent);
        ent.Comp.Modules.Add("BasePacketModule"); // Basic firmware.

        foreach (var moduleSlot in slotsComponent.Slots.Values)
        {
            if (!TryComp<ExecutorModuleComponent>(moduleSlot.Item, out var moduleName))
                return;

            ent.Comp.Modules.Add(moduleName.ModuleName);
        }

        EnsureEngine(ent);
    }

    private void DisposeEngine(Entity<ExecutorComponent> ent)
    {
        ent.Comp.Modules.Clear();
        RemComp<DeviceLinkSinkComponent>(ent);
        _modules.Remove(ent);
        _methods.Remove(ent);

        if (!_executorEntities.Remove(ent, out var engine))
            return;

        _engineCts.Remove(engine);
        engine.Dispose();
    }
}
