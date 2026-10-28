using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed class KsProcgenPrototypeTestContextTests : KsProcgenGameTest
{
    [TestCase("net")]
    [TestCase("map")]
    public async Task PrototypeContextRoundtripsInvalidDefaultsAndRejectsLiveWrites(string kind)
    {
        await AssertOnServer(() =>
        {
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var context = new PrototypeSaveTest.TestEntityUidContext(serialization);
            if (kind == "net")
            {
                var node = serialization.WriteValue(NetEntity.Invalid, context: context);
                Assert.That(serialization.Read<NetEntity>(node, context: context), Is.EqualTo(NetEntity.Invalid));
                Assert.That(serialization.ValidateNode<NetEntity>(node, context: context).Valid, Is.True);
                Assert.Catch<Exception>(() => serialization.WriteValue(new NetEntity(42), context: context));
            }
            else
            {
                var node = serialization.WriteValue(MapId.Nullspace, context: context);
                Assert.That(serialization.Read<MapId>(node, context: context), Is.EqualTo(MapId.Nullspace));
                Assert.That(serialization.ValidateNode<MapId>(node, context: context).Valid, Is.True);
                Assert.Catch<Exception>(() => serialization.WriteValue(new MapId(42), context: context));
            }
        });
    }

    [TestCase("net", "42")]
    [TestCase("net", "malformed")]
    [TestCase("map", "42")]
    [TestCase("map", "malformed")]
    public async Task PrototypeContextRejectsLiveAndMalformedInput(string kind, string value)
    {
        await AssertOnServer(() =>
        {
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var context = new PrototypeSaveTest.TestEntityUidContext(serialization);
            var node = new ValueDataNode(value);
            if (kind == "net")
            {
                Assert.That(serialization.ValidateNode<NetEntity>(node, context: context).Valid, Is.False);
                Assert.Catch<Exception>(() => serialization.Read<NetEntity>(node, context: context));
            }
            else
            {
                Assert.That(serialization.ValidateNode<MapId>(node, context: context).Valid, Is.False);
                Assert.Catch<Exception>(() => serialization.Read<MapId>(node, context: context));
            }
        });
    }
}
