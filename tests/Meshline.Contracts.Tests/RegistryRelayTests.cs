using Neo;
using Xunit;
using VMBoolean = Neo.VM.Types.Boolean;
using VMByteString = Neo.VM.Types.ByteString;
using StackItem = Neo.VM.Types.StackItem;
using static Meshline.Contracts.Tests.RegistryAssert;

namespace Meshline.Contracts.Tests;

[Collection("Registry VM")]
public sealed class RegistryRelayTests
{
    [Fact]
    public void RelayIdsMustBeTwentyByteStrings()
    {
        using var context = new RegistryTestContext();
        var invalidIds = InvalidScalarTypes().Concat(new StackItem[]
        {
            new VMByteString(System.Array.Empty<byte>()),
            new VMByteString(new byte[19]), new VMByteString(new byte[21]),
            new Neo.VM.Types.Buffer(new byte[20])
        });
        foreach (var id in invalidIds)
        {
            foreach (var method in new[] { "getRelay", "hasRelay", "registerRelay", "updateRelayEndpoint", "setRelayEnabled", "suspendRelay", "restoreRelay" })
            {
                StackItem[] arguments = method switch
                {
                    "registerRelay" or "updateRelayEndpoint" => [id, Text("https://relay.example/meshline/v1")],
                    "setRelayEnabled" => [id, VMBoolean.True],
                    _ => [id]
                };
                using var engine = context.Invoke(method, context.Relay, arguments);
                Fault(engine, "Invalid relay ID");
            }
        }
    }

    [Fact]
    public void EnabledChangesAndGovernanceRespectTimestampsAndNoOps()
    {
        using var context = new RegistryTestContext();
        context.Seed(context.Relay, "active");
        var previousStatus = "active";
        foreach (var enabled in new[] { true, false, false, true })
        {
            var before = context.RecordBytes(context.Relay);
            var nextStatus = enabled ? "active" : "disabled";
            context.Time += 1000;
            using var engine = context.Invoke("setRelayEnabled", context.Relay, Id(context.Relay), enabled ? VMBoolean.True : VMBoolean.False);
            BooleanResult(engine, true);
            if (previousStatus == nextStatus)
            {
                Assert.Equal(before, context.RecordBytes(context.Relay));
                Assert.Empty(engine.Notifications);
            }
            else Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, nextStatus, context.Time);
            previousStatus = nextStatus;
        }
        using (var suspend = context.Invoke("suspendRelay", context.Owner, Id(context.Relay)))
            BooleanResult(suspend, true);
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "suspended", context.Time);
        var suspended = context.RecordBytes(context.Relay);
        foreach (var enabled in new[] { VMBoolean.True, VMBoolean.False })
        {
            context.Time++;
            using var denied = context.Invoke("setRelayEnabled", context.Relay, Id(context.Relay), enabled);
            BooleanResult(denied, false);
            Assert.Equal(suspended, context.RecordBytes(context.Relay));
        }
        using (var restore = context.Invoke("restoreRelay", context.Owner, Id(context.Relay)))
            BooleanResult(restore, true);
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "disabled", context.Time);
        context.Time++;
        using (var enable = context.Invoke("setRelayEnabled", context.Relay, Id(context.Relay), VMBoolean.True))
            BooleanResult(enable, true);
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "active", context.Time);
    }

    [Theory]
    [InlineData("updateRelayEndpoint")]
    [InlineData("setRelayEnabled")]
    public void MissingOrUnauthorizedRelayMutationsLeaveRecordsUnchanged(string method)
    {
        using var context = new RegistryTestContext();
        StackItem value = method == "setRelayEnabled" ? VMBoolean.False : Text("https://changed.example/meshline/v1");
        using (var missing = context.Invoke(method, context.Relay, Id(context.Relay), value))
            BooleanResult(missing, false);
        Assert.False(context.Snapshot.Contains(context.Key("relay", context.Relay)));

        context.Seed(context.Relay, "active");
        var before = context.RecordBytes(context.Relay);
        context.Time += 1000;
        foreach (var signer in new UInt160?[] { null, context.Owner })
        {
            using var denied = context.Invoke(method, signer, Id(context.Relay), value);
            BooleanResult(denied, false);
            Assert.Empty(denied.Notifications);
            Assert.Equal(before, context.RecordBytes(context.Relay));
        }
    }
}
