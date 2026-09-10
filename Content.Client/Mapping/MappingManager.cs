using System.IO;
using System.Linq; // KS14
using System.Text;
using System.Threading.Tasks;
using Content.Shared.Decals; // KS14
using Content.Shared.Mapping;
using Content.Shared._KS14.Mapping; // KS14
using Content.Shared.Maps; // KS14
using Robust.Client.UserInterface;
using Robust.Shared.Network;
using Robust.Shared.Prototypes; // KS14

namespace Content.Client.Mapping;

public sealed partial class MappingManager : IPostInjectInit
{
    [Dependency] private IFileDialogManager _file = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!; // KS14: mapping editor overhaul port

    private Stream? _saveStream;
    private MappingMapDataMessage? _mapData;
        // KS14 start: mapping editor favorites
    private List<IPrototype>? _favoritePrototypes;

    public event Action<List<IPrototype>>? OnFavoritePrototypesLoaded;
        // KS14 end

    public void PostInject()
    {
        _net.RegisterNetMessage<MappingSaveMapMessage>();
        _net.RegisterNetMessage<MappingSaveMapErrorMessage>(OnSaveError);
        _net.RegisterNetMessage<MappingMapDataMessage>(OnMapData);
        // KS14 start: mapping editor favorites
        _net.RegisterNetMessage<MappingFavoritesDataMessage>(OnFavoritesData);
        _net.RegisterNetMessage<MappingFavoritesSaveMessage>();
        // KS14 end
    }

    private void OnSaveError(MappingSaveMapErrorMessage message)
    {
        _saveStream?.DisposeAsync();
        _saveStream = null;
    }

    private async void OnMapData(MappingMapDataMessage message)
    {
        if (_saveStream == null)
        {
            _mapData = message;
            return;
        }

        await _saveStream.WriteAsync(Encoding.ASCII.GetBytes(message.Yml));
        await _saveStream.DisposeAsync();

        _saveStream = null;
        _mapData = null;
    }

        // KS14 start: mapping editor favorites
    private void OnFavoritesData(MappingFavoritesDataMessage message)
    {
        _favoritePrototypes = new List<IPrototype>();

        foreach (var prototype in message.PrototypeIDs)
        {
            if (_prototypeManager.TryIndex<EntityPrototype>(prototype, out var entity))
                _favoritePrototypes.Add(entity);
            else if (_prototypeManager.TryIndex<ContentTileDefinition>(prototype, out var tile))
                _favoritePrototypes.Add(tile);
            else if (_prototypeManager.TryIndex<DecalPrototype>(prototype, out var decal))
                _favoritePrototypes.Add(decal);
        }

        OnFavoritePrototypesLoaded?.Invoke(_favoritePrototypes);
    }

        // KS14 end
    public async Task SaveMap()
    {
        if (_saveStream != null)
            await _saveStream.DisposeAsync();

        var request = new MappingSaveMapMessage();
        _net.ClientSendMessage(request);

        var path = await _file.SaveFile();
        if (path is not { fileStream: var stream })
            return;

        if (_mapData != null)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(_mapData.Yml));
            _mapData = null;
            await stream.FlushAsync();
            await stream.DisposeAsync();
            return;
        }

        _saveStream = stream;
    }
        // KS14 start: mapping editor favorites

    public void SaveFavorites(List<MappingPrototype> prototypes)
    {
        // TODO: that doesnt save null prototypes (mapping templates and abstract parents)
        var msg = new MappingFavoritesSaveMessage()
        {
            PrototypeIDs = prototypes
                .FindAll(p => p.Prototype != null)
                .Select(p => p.Prototype!.ID)
                .ToList(),
        };
        _net.ClientSendMessage(msg);
    }

    public void LoadFavorites()
    {
        var request = new MappingFavoritesLoadMessage();
        _net.ClientSendMessage(request);
    }
        // KS14 end
}
