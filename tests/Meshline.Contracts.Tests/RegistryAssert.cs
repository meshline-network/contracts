using System.Numerics;
using System.Text;
using Neo;
using Neo.Extensions;
using Neo.SmartContract;
using Neo.VM;
using Xunit;
using VMArray = Neo.VM.Types.Array;
using VMBoolean = Neo.VM.Types.Boolean;
using VMByteString = Neo.VM.Types.ByteString;

namespace Meshline.Contracts.Tests;

internal static class RegistryAssert
{
    internal static VMByteString Text(string value) => new(Encoding.UTF8.GetBytes(value));
    internal static VMByteString Id(UInt160 value) => new(value.GetSpan().ToArray());

    internal static Neo.VM.Types.StackItem[] InvalidScalarTypes() =>
        [Neo.VM.Types.StackItem.Null, VMBoolean.True, new Neo.VM.Types.Integer(0), new VMArray()];

    internal static void Halted(ApplicationEngine engine, string operation)
    {
        Assert.True(engine.State == VMState.HALT,
            $"{operation} faulted: {engine.FaultException?.ToString() ?? "unknown Neo VM fault"}");
    }

    internal static void Fault(ApplicationEngine engine, string message)
    {
        Assert.Equal(VMState.FAULT, engine.State);
        Assert.Contains(message, engine.FaultException?.ToString() ?? "");
    }

    internal static void BooleanResult(ApplicationEngine engine, bool expected)
    {
        Halted(engine, "registry operation");
        Assert.Equal(expected, Assert.IsType<VMBoolean>(engine.ResultStack.Pop()).GetBoolean());
    }

    internal static void Entry(RegistryTestContext context, UInt160 id, string endpoint, string status, ulong timestamp)
    {
        using var engine = context.Invoke("getRelay", null, Id(id));
        Halted(engine, "getRelay");
        var entry = Assert.IsAssignableFrom<VMArray>(engine.ResultStack.Pop());
        Assert.Equal(4, entry.Count);
        Assert.Equal(id, new UInt160(entry[0].GetSpan()));
        Assert.Equal(endpoint, entry[1].GetString());
        Assert.Equal(status, entry[2].GetString());
        Assert.Equal(new BigInteger(timestamp), entry[3].GetInteger());
    }
}
