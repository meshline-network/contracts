using System.Text.Json;
using Xunit;

namespace Meshline.Contracts.Tests;

public sealed class RegistryAbiTests
{
    [Fact]
    public void MethodsExposeExpectedParametersReturnTypesAndSafeFlags()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ContractArtifacts.Load().ManifestPath));
        var methods = manifest.RootElement.GetProperty("abi").GetProperty("methods")
            .EnumerateArray().ToDictionary(method => method.GetProperty("name").GetString()!, StringComparer.Ordinal);

        Assert.Equal(16, methods.Count);
        AssertMethod(methods, "getRelay", "Any", true, ("relayId", "Hash160"));
        AssertMethod(methods, "listRelays", "InteropInterface", true);
        AssertMethod(methods, "getRelayRegistrationFee", "Integer", true);
        AssertMethod(methods, "registerRelay", "Boolean", false, ("relayId", "Hash160"), ("endpoint", "String"));
        AssertMethod(methods, "updateRelayEndpoint", "Boolean", false, ("relayId", "Hash160"), ("endpoint", "String"));
        AssertMethod(methods, "setRelayEnabled", "Boolean", false, ("relayId", "Hash160"), ("enabled", "Boolean"));
        AssertMethod(methods, "hasRelay", "Boolean", true, ("relayId", "Hash160"));
        AssertMethod(methods, "getOwner", "Hash160", true);
        AssertMethod(methods, "setOwner", "Boolean", false, ("newOwner", "Hash160"));
        AssertMethod(methods, "setRelayRegistrationFee", "Boolean", false, ("amount", "Integer"));
        AssertMethod(methods, "suspendRelay", "Boolean", false, ("relayId", "Hash160"));
        AssertMethod(methods, "restoreRelay", "Boolean", false, ("relayId", "Hash160"));
        AssertMethod(methods, "_deploy", "Void", false, ("data", "Any"), ("update", "Boolean"));
        AssertMethod(methods, "verify", "Boolean", false);
        AssertMethod(methods, "update", "Void", false, ("nefFile", "ByteArray"), ("manifest", "String"));
        AssertMethod(methods, "onNEP17Payment", "Void", false, ("from", "Hash160"), ("amount", "Integer"), ("data", "Any"));
    }

    [Fact]
    public void EventsExposeExpectedParameterNamesTypesAndOrder()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ContractArtifacts.Load().ManifestPath));
        var events = manifest.RootElement.GetProperty("abi").GetProperty("events")
            .EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!, StringComparer.Ordinal);

        Assert.Equal(5, events.Count);
        AssertParameters(events["RelayRegistered"],
            ("relayId", "Hash160"), ("endpoint", "String"), ("status", "String"), ("updatedAt", "Integer"));
        AssertParameters(events["RelayRegistrationFeeChanged"],
            ("previousAmount", "Integer"), ("newAmount", "Integer"), ("updatedAt", "Integer"));
        AssertParameters(events["RelayStatusChanged"],
            ("relayId", "Hash160"), ("status", "String"), ("updatedAt", "Integer"));
        AssertParameters(events["RelayEndpointChanged"],
            ("relayId", "Hash160"), ("oldEndpoint", "String"), ("newEndpoint", "String"), ("updatedAt", "Integer"));
        AssertParameters(events["OwnerChanged"], ("previousOwner", "Hash160"), ("newOwner", "Hash160"));
    }

    private static void AssertMethod(IReadOnlyDictionary<string, JsonElement> methods, string name,
        string returnType, bool safe, params (string Name, string Type)[] parameters)
    {
        Assert.True(methods.TryGetValue(name, out var method), $"Expected contract method '{name}'.");
        Assert.Equal(returnType, method.GetProperty("returntype").GetString());
        Assert.Equal(safe, method.GetProperty("safe").GetBoolean());
        AssertParameters(method, parameters);
    }

    private static void AssertParameters(JsonElement descriptor, params (string Name, string Type)[] parameters)
    {
        var actual = descriptor.GetProperty("parameters").EnumerateArray()
            .Select(parameter => (parameter.GetProperty("name").GetString()!, parameter.GetProperty("type").GetString()!))
            .ToArray();
        Assert.Equal(parameters, actual);
    }
}
