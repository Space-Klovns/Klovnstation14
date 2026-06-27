using System.IO;
using System.Linq;
using Content.Shared._KS14.IoC;
using Robust.Shared.Collections;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Entry;

public sealed class KsEntryPoint : GameShared
{
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

    private readonly ResPath[] _replacementDirectories = [
        new("/_KsModule_ReplacedPrototypes/")
    ];

    private ISawmill _sawmill = null!;

    public override void PreInit()
    {
        base.PreInit();
        KsSharedContentIoC.Register(Dependencies);
    }

    public override void Init()
    {
        base.Init();
        Dependencies.BuildGraph();
        Dependencies.InjectDependencies(this);

        _sawmill = Logger.GetSawmill("ks.shared.entrypoint");
    }

    public override void PostInit()
    {
        base.PostInit();

        // Kindly wait until IPrototypeManager is all set up

        foreach (var replacementDirectory in _replacementDirectories)
            DoPrototypeReplacements(replacementDirectory);
    }

    public override void Shutdown()
    {
        _systemCollectionHookManager.Reset();
        base.Shutdown();
    }

    private void DoPrototypeReplacements(ResPath replacementDirectory)
    {
        var sequences = new ValueList<(SequenceDataNode, TextReader)>();
        try
        {
            if (!TrySearchDirectory(replacementDirectory, ref sequences))
                return;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Caught exception when trying to search directory {replacementDirectory} for prototype replacements! Processing has been aborted. Exception: {ex}");
            return;
        }

        var modifiedDict = new Dictionary<Type, HashSet<string>>();

        foreach (var (sequence, reader) in sequences)
        {
            modifiedDict.Clear();

            foreach (var node in sequence.Sequence)
            {
                var prototypeNode = (MappingDataNode)node;
                var prototypeKind = _prototypeManager.GetKindType(prototypeNode.Get<ValueDataNode>("type").Value)!;
                var prototypeId = prototypeNode.Get<ValueDataNode>(IdDataFieldAttribute.Name).Value!;

                // there's no HasIndex that takes Type
                // TODO LCDC ENGINE: IPrototypeManager.HasIndex(Type kind, string id)...
                if (!_prototypeManager.TryIndex(prototypeKind, prototypeId, out _))
                {
                    _sawmill.Error($"Tried to replace prototype of kind {prototypeKind.Name} and id {prototypeId}, however it did not exist!");
                    continue;
                }

                modifiedDict.GetOrNew(prototypeKind).Add(prototypeId);
            }

            _prototypeManager.LoadFromStream(reader, overwrite: true, modifiedDict);
            reader.Dispose();
        }

        _prototypeManager.ResolveResults();
        _sawmill.Debug($"Replaced {sequences.Count} file(s) worth of prototypes");
    }

    // its not copypasta its assetflip
    /// <summary>
    ///     MAKE SURE TO DISPOSE THE READER THAT GETS RETURNED!!!!
    /// </summary>
    private bool TrySearchDirectory(ResPath replacementDirectory, ref ValueList<(SequenceDataNode, TextReader)> sequences)
    {
        foreach (var path in _resourceManager.ContentFindFiles(replacementDirectory))
        {
            // Ignore non-yml files
            if (path.Extension != "yml")
                continue;

            var stream = _resourceManager.ContentFileRead(path);

            // leave open, so that the BaseStream can be used later
            var tempReader = new StreamReader(stream, EncodingHelpers.UTF8, leaveOpen: true);
            if (DataNodeParser.ParseYamlStream(tempReader).FirstOrDefault()?.Root is not SequenceDataNode rootNode)
            {
                // we didn't end up using it
                tempReader.BaseStream.Dispose();
                continue;
            }

            // technically could set basestream.position to 0 and DiscardBufferedData() on the reader. However DiscardBufferedData is out of sandbox.
            //      So, instead make a new StreamReader and seek to origin
            // TODO LCDC ENGINE: add DiscardBufferedData to sandbox

            var longTermReader = new StreamReader(tempReader.BaseStream, EncodingHelpers.UTF8);
            longTermReader.BaseStream.Seek(0, SeekOrigin.Begin); // reset to origin

            sequences.Add((rootNode, longTermReader));
        }

        return true;
    }
}
