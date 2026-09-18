using System.Numerics;
using Neo;
using Neo.Network.P2P.Payloads;
using Xunit;
using static Meshline.Contracts.Tests.RegistryAssert;

namespace Meshline.Contracts.Tests;

[Collection("Registry VM")]
public sealed class RegistryRegistrationTests
{
    [Fact]
    public void PaidRegistrationChargesDefaultFeeAndDuplicateLeavesRecordAndBalancesUnchanged()
    {
        using var context = new RegistryTestContext();
        const long initialBalance = 15_000_000_000;
        const long fee = 10_000_000_000;
        context.SeedGasBalance(context.Relay, initialBalance);
        using (var queried = context.Invoke("getRelayRegistrationFee", null))
        {
            Halted(queried, "getRelayRegistrationFee");
            Assert.Equal(new BigInteger(fee), queried.ResultStack.Pop().GetInteger());
        }

        using (var registered = context.Invoke("registerRelay", context.Relay, Id(context.Relay), Text(RegistryTestContext.SeedEndpoint)))
            BooleanResult(registered, true);
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "active", context.Time);
        Assert.Equal(new BigInteger(initialBalance - fee), context.GasBalance(context.Relay));
        Assert.Equal(new BigInteger(fee), context.GasBalance(context.Contract.Hash));
        Assert.False(context.Snapshot.Contains(context.Key("pendingRelay", context.Relay)));

        var before = context.RecordBytes(context.Relay);
        context.Time += 1000;
        using (var duplicate = context.Invoke("registerRelay", context.Relay, Id(context.Relay), Text("https://different.example/api")))
        {
            BooleanResult(duplicate, false);
            Assert.Empty(duplicate.Notifications);
        }
        Assert.Equal(before, context.RecordBytes(context.Relay));
        Assert.Equal(new BigInteger(initialBalance - fee), context.GasBalance(context.Relay));
        Assert.Equal(new BigInteger(fee), context.GasBalance(context.Contract.Hash));
    }

    [Fact]
    public void ZeroFeeRegistrationNeedsNoGasBalanceOrGasWitnessScope()
    {
        using var context = new RegistryTestContext { Scope = WitnessScope.CalledByEntry };
        context.SetFee(0);
        using (var queried = context.Invoke("getRelayRegistrationFee", null))
        {
            Halted(queried, "getRelayRegistrationFee");
            Assert.Equal(BigInteger.Zero, queried.ResultStack.Pop().GetInteger());
        }
        using (var registered = context.Invoke("registerRelay", context.Relay, Id(context.Relay), Text(RegistryTestContext.SeedEndpoint)))
            BooleanResult(registered, true);
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "active", context.Time);
        Assert.Equal(BigInteger.Zero, context.GasBalance(context.Relay));
        Assert.Equal(BigInteger.Zero, context.GasBalance(context.Contract.Hash));
    }

    [Fact]
    public void RegistrationChargesTheFeeAtExecutionEvenIfItChangedAfterQuerying()
    {
        using var context = new RegistryTestContext();
        const long initialBalance = 20_000_000_000;
        const long changedFee = 2_000_000_000;
        context.SeedGasBalance(context.Relay, initialBalance);
        using (var queried = context.Invoke("getRelayRegistrationFee", null))
        {
            Halted(queried, "getRelayRegistrationFee");
            Assert.Equal(new BigInteger(10_000_000_000), queried.ResultStack.Pop().GetInteger());
        }
        context.Time += 1000;
        context.SetFee(changedFee);
        using (var registered = context.Invoke("registerRelay", context.Relay, Id(context.Relay), Text(RegistryTestContext.SeedEndpoint)))
            BooleanResult(registered, true);
        Entry(context, context.Relay, RegistryTestContext.SeedEndpoint, "active", context.Time);
        Assert.Equal(new BigInteger(initialBalance - changedFee), context.GasBalance(context.Relay));
        Assert.Equal(new BigInteger(changedFee), context.GasBalance(context.Contract.Hash));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationWithoutRelayAuthorizationDoesNotWriteOrCharge(bool hasWrongSigner)
    {
        using var context = new RegistryTestContext();
        const long balance = 20_000_000_000;
        context.SeedGasBalance(context.Relay, balance);
        using var denied = context.Invoke("registerRelay", hasWrongSigner ? context.Owner : null,
            Id(context.Relay), Text(RegistryTestContext.SeedEndpoint));
        BooleanResult(denied, false);
        Assert.Empty(denied.Notifications);
        AssertNoRegistration(context);
        Assert.Equal(new BigInteger(balance), context.GasBalance(context.Relay));
        Assert.Equal(BigInteger.Zero, context.GasBalance(context.Contract.Hash));
    }

    [Fact]
    public void InsufficientGasFaultsAndRollsBackRegistration()
    {
        using var context = new RegistryTestContext();
        const long balance = 10_000_000_000 - 1;
        context.SeedGasBalance(context.Relay, balance);
        var invocation = context.Snapshot.CloneCache();
        using (var failed = context.InvokeOn(invocation, "registerRelay", context.Relay,
            Id(context.Relay), Text(RegistryTestContext.SeedEndpoint)))
            Fault(failed, "Relay registration payment failed");
        AssertNoRegistration(context);
        Assert.Equal(new BigInteger(balance), context.GasBalance(context.Relay));
        Assert.Equal(BigInteger.Zero, context.GasBalance(context.Contract.Hash));
    }

    [Fact]
    public void DirectGasPaymentWithoutPendingRegistrationFaultsAndRollsBackTransfer()
    {
        using var context = new RegistryTestContext();
        const long balance = 15_000_000_000;
        context.SeedGasBalance(context.Relay, balance);
        var invocation = context.Snapshot.CloneCache();
        using (var failed = context.TransferGas(invocation, context.Relay, 10_000_000_000, RegistryTestContext.SeedEndpoint))
            Fault(failed, "Relay registration is not pending");
        AssertNoRegistration(context);
        Assert.Equal(new BigInteger(balance), context.GasBalance(context.Relay));
        Assert.Equal(BigInteger.Zero, context.GasBalance(context.Contract.Hash));
    }

    private static void AssertNoRegistration(RegistryTestContext context)
    {
        Assert.False(context.Snapshot.Contains(context.Key("relay", context.Relay)));
        Assert.False(context.Snapshot.Contains(context.Key("pendingRelay", context.Relay)));
    }
}
