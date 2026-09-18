using System.Numerics;
using Neo;
using Xunit;
using VMArray = Neo.VM.Types.Array;
using static Meshline.Contracts.Tests.RegistryAssert;

namespace Meshline.Contracts.Tests;

[Collection("Registry VM")]
public sealed class RegistryQueryTests
{
    [Fact]
    public void QueriesNeedNoWitnessAndIterateEveryStateWithoutOrderingAssumptions()
    {
        using var context = new RegistryTestContext();
        var expected = new Dictionary<UInt160, string>
        {
            [context.Relay] = "active",
            [UInt160.Parse("0x3333333333333333333333333333333333333333")] = "disabled",
            [UInt160.Parse("0x5555555555555555555555555555555555555555")] = "suspended"
        };
        foreach (var pair in expected)
        {
            context.Seed(pair.Key, pair.Value);
            using var exists = context.Invoke("hasRelay", null, Id(pair.Key));
            BooleanResult(exists, true);
            Entry(context, pair.Key, RegistryTestContext.SeedEndpoint, pair.Value, RegistryTestContext.SeedTimestamp);
        }
        using var engine = context.Invoke("listRelays", null);
        Halted(engine, "listRelays");
        var iterator = engine.ResultStack.Pop().GetInterface<Neo.SmartContract.Iterators.IIterator>();
        Assert.NotNull(iterator);
        var seen = new HashSet<UInt160>();
        while (iterator.Next())
        {
            var entry = Assert.IsAssignableFrom<VMArray>(iterator.Value(engine.ReferenceCounter));
            Assert.Equal(4, entry.Count);
            var id = new UInt160(entry[0].GetSpan());
            Assert.True(seen.Add(id));
            Assert.Equal(RegistryTestContext.SeedEndpoint, entry[1].GetString());
            Assert.Equal(expected[id], entry[2].GetString());
            Assert.Equal(new BigInteger(RegistryTestContext.SeedTimestamp), entry[3].GetInteger());
        }
        Assert.Equal(expected.Count, seen.Count);
        using var fee = context.Invoke("getRelayRegistrationFee", null);
        Halted(fee, "getRelayRegistrationFee");
        Assert.Equal(new BigInteger(10_000_000_000), fee.ResultStack.Pop().GetInteger());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(2_000_000_000L)]
    public void ConfiguredFeeDoesNotAppearInRelayIterator(long fee)
    {
        using var context = new RegistryTestContext();
        context.SetFee(fee);
        Assert.Empty(ReadRelayIds(context));

        var expected = new Dictionary<UInt160, string>
        {
            [context.Relay] = "active",
            [UInt160.Parse("0x3333333333333333333333333333333333333333")] = "disabled",
            [UInt160.Parse("0x5555555555555555555555555555555555555555")] = "suspended"
        };
        foreach (var relay in expected) context.Seed(relay.Key, relay.Value);
        var actual = ReadRelayIds(context);
        Assert.Equal(expected.Count, actual.Count);
        Assert.True(expected.Keys.ToHashSet().SetEquals(actual));
        using var feeQuery = context.Invoke("getRelayRegistrationFee", null);
        Halted(feeQuery, "getRelayRegistrationFee after iteration");
        Assert.Equal(new BigInteger(fee), feeQuery.ResultStack.Pop().GetInteger());
    }

    [Fact]
    public void MissingRelayQueriesNeedNoWitness()
    {
        using var context = new RegistryTestContext();
        using var missing = context.Invoke("getRelay", null, Id(UInt160.Zero));
        Halted(missing, "getRelay");
        Assert.True(missing.ResultStack.Pop().IsNull);
        using var exists = context.Invoke("hasRelay", null, Id(UInt160.Zero));
        BooleanResult(exists, false);
    }

    private static IReadOnlyList<UInt160> ReadRelayIds(RegistryTestContext context)
    {
        using var engine = context.Invoke("listRelays", null);
        Halted(engine, "listRelays");
        var iterator = engine.ResultStack.Pop().GetInterface<Neo.SmartContract.Iterators.IIterator>();
        Assert.NotNull(iterator);
        var ids = new List<UInt160>();
        while (iterator.Next())
        {
            var entry = Assert.IsAssignableFrom<VMArray>(iterator.Value(engine.ReferenceCounter));
            ids.Add(new UInt160(entry[0].GetSpan()));
        }
        return ids;
    }
}
