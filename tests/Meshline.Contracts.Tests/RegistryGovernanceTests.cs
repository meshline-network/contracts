using System.Numerics;
using Neo;
using Neo.Extensions;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Xunit;
using VMByteString = Neo.VM.Types.ByteString;
using VMInteger = Neo.VM.Types.Integer;
using static Meshline.Contracts.Tests.RegistryAssert;

namespace Meshline.Contracts.Tests;

[Collection("Registry VM")]
public sealed class RegistryGovernanceTests
{
    [Fact]
    public void FeeChangesRequireOwnerAndNotifyOnlyWhenTheAmountChanges()
    {
        using var context = new RegistryTestContext();
        const long changedFee = 2_000_000_000;
        using (var update = context.Invoke("setRelayRegistrationFee", context.Owner, new VMInteger(changedFee)))
        {
            BooleanResult(update, true);
            var notification = Assert.Single(update.Notifications);
            Assert.Equal("RelayRegistrationFeeChanged", notification.EventName);
            Assert.Equal(new BigInteger(10_000_000_000), notification.State[0].GetInteger());
            Assert.Equal(new BigInteger(changedFee), notification.State[1].GetInteger());
            Assert.Equal(new BigInteger(context.Time), notification.State[2].GetInteger());
        }
        context.Time += 1000;
        using (var noOp = context.Invoke("setRelayRegistrationFee", context.Owner, new VMInteger(changedFee)))
        {
            BooleanResult(noOp, true);
            Assert.Empty(noOp.Notifications);
        }
        using (var denied = context.Invoke("setRelayRegistrationFee", context.Relay, new VMInteger(0)))
            BooleanResult(denied, false);
        using var queried = context.Invoke("getRelayRegistrationFee", null);
        Halted(queried, "getRelayRegistrationFee");
        Assert.Equal(new BigInteger(changedFee), queried.ResultStack.Pop().GetInteger());
    }

    [Fact]
    public void NegativeRegistrationFeeFaultsAndKeepsTheCurrentFee()
    {
        using var context = new RegistryTestContext();
        context.SetFee(0);
        using (var rejected = context.Invoke("setRelayRegistrationFee", context.Owner, new VMInteger(-1)))
            Fault(rejected, "cannot be negative");
        using var queried = context.Invoke("getRelayRegistrationFee", null);
        Halted(queried, "getRelayRegistrationFee");
        Assert.Equal(BigInteger.Zero, queried.ResultStack.Pop().GetInteger());
    }

    [Fact]
    public void ContractUpdateRequiresOwner()
    {
        using var context = new RegistryTestContext();
        using var rejected = context.Invoke("update", context.Relay, new VMByteString(context.NefBytes), Text(context.ManifestJson));
        Fault(rejected, "No authorization");
        Assert.Equal(0, NativeContract.ContractManagement.GetContract(context.Snapshot, context.Contract.Hash)!.UpdateCounter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(4_200_000_000L)]
    public void ContractUpdatePreservesOwnerRelayRecordsAndConfiguredFee(long? storedFee)
    {
        using var context = new RegistryTestContext();
        context.Seed(context.Relay, "active");
        var originalRecord = context.RecordBytes(context.Relay);
        var feeKey = context.Key("registrationFee");
        if (storedFee.HasValue)
            context.Snapshot.Add(feeKey, new StorageItem(new BigInteger(storedFee.Value)));

        using (var updated = context.Invoke("update", context.Owner, new VMByteString(context.NefBytes), Text(context.ManifestJson)))
            Halted(updated, "update");
        var contract = NativeContract.ContractManagement.GetContract(context.Snapshot, context.Contract.Hash);
        Assert.NotNull(contract);
        Assert.Equal(context.Contract.Hash, contract.Hash);
        Assert.Equal(1, contract.UpdateCounter);
        using (var owner = context.Invoke("getOwner", null))
        {
            Halted(owner, "getOwner after update");
            Assert.Equal(context.Owner, new UInt160(owner.ResultStack.Pop().GetSpan()));
        }
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "active", RegistryTestContext.SeedTimestamp);
        Assert.Equal(originalRecord, context.RecordBytes(context.Relay));
        using var fee = context.Invoke("getRelayRegistrationFee", null);
        Halted(fee, "getRelayRegistrationFee after update");
        Assert.Equal(new BigInteger(storedFee ?? 10_000_000_000L), fee.ResultStack.Pop().GetInteger());
        Assert.Equal(storedFee.HasValue, context.Snapshot.Contains(feeKey));
    }
}
