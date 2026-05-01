namespace Content.Shared.Atmos.EntitySystems;

public abstract partial class SharedAtmosphereSystem
{
    [Access(Other = AccessPermissions.Read)]
    public bool GasPrototypesAreInitialised = false;
}
