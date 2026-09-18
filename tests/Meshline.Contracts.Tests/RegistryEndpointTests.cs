using System.Text;
using Neo;
using Xunit;
using VMByteString = Neo.VM.Types.ByteString;
using StackItem = Neo.VM.Types.StackItem;
using static Meshline.Contracts.Tests.RegistryAssert;

namespace Meshline.Contracts.Tests;

[Collection("Registry VM")]
public sealed class RegistryEndpointTests
{
    public static TheoryData<string> ValidEndpoints => new()
    {
        "https://relay.example/meshline/v1",
        "https://relay.example/meshline/v2",
        "https://RELAY.example:443/meshline/v1",
        "https://relay.example:0443/meshline/v1",
        "https://relay.example:08443/meshline/v1",
        "https://relay.example:65535/meshline/v1",
        "https://relay.example./meshline/v1",
        "https://bücher.example/meshline/v1",
        "https://XN--BCHER-KVA.example:443/meshline/v1",
        "https://faß.example/meshline/v1",
        "https://中继.example/meshline/v1",
        "https://192.0.2.1:443/meshline/v1",
        "https://[2001:0DB8:0000:0000:0000:0000:0000:0001]:443/meshline/v1",
        "https://[2001:db8::1]:8443/meshline/v1",
        "https://[::ffff:192.0.2.1]/meshline/v1",
        "https://[1:2:3:4:5:6:192.0.2.1]/meshline/v1",
        "https://[::1]/meshline/v1",
        "https://[1::]/meshline/v1",
        "https://[::]/meshline/v1",
        "https://relay.example/a/b/meshline/v1",
        "https://relay.example/中文/😀/meshline/v1",
        "https://relay.example/a%20b/%E4%B8%AD/meshline/v1",
        "https://relay.example/a@b/meshline/v1",
        "https://" + new string('a', 63) + ".example/meshline/v1",
        "https://" + string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61)) + "/meshline/v1",
        "https://" + string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61)) + "./meshline/v1",
        EndpointAtByteLength(512, false),
        EndpointAtByteLength(512, true)
    };

    public static TheoryData<string> InvalidEndpoints => new()
    {
        "", "https:", "https:/",
        "http://relay.example/meshline/v1", "wss://relay.example/meshline/v1",
        "HTTPS://relay.example/meshline/v1",
        EndpointAtByteLength(513, false),
        EndpointAtByteLength(513, true)
    };

    // Accepted by the contract's three basic checks; clients validate full URL syntax.
    public static TheoryData<string> EndpointFormatsDelegatedToClients => new()
    {
        "https://", "https:///", "https://:443",
        "https:///meshline/v1", "https://:443/meshline/v1",
        "https://user@relay.example/meshline/v1", "https://user:pass@relay.example/meshline/v1",
        "https://relay.example?x/meshline/v1", "https://relay.example?/meshline/v1",
        "https://relay.example#/meshline/v1", "https://relay.example/meshline/v1?",
        "https://relay.example/meshline/v1#", "https://relay.example\\alias/meshline/v1",
        "https://relay.example/a?b/meshline/v1", "https://relay.example/a#b/meshline/v1",
        "https://relay.example/a\\b/meshline/v1",
        "https://a", "https://relay.example", "https://relay.example/",
        "https://relay.example/api", "https://relay.example/api/",
        "https://relay.example/meshline/v1/",
        "https://relay.example:/meshline/v1", "https://relay.example:bad/meshline/v1",
        "https://relay.example:65536/meshline/v1", "https://relay..example/meshline/v1",
        "https://999.1.1.1/meshline/v1", "https://192.0.2.01/meshline/v1",
        "https://[gggg::1]/meshline/v1",
        "https://relay.example/%xx/meshline/v1", "https://relay.example/%/meshline/v1",
        "https://relay.example/\u00a0/meshline/v1", "https://relay.example/\u3000/meshline/v1",
        "https://" + new string('a', 64) + ".example/meshline/v1",
        "https://" + string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 62)) + "/meshline/v1"
    };

    [Theory]
    [MemberData(nameof(ValidEndpoints))]
    [MemberData(nameof(EndpointFormatsDelegatedToClients))]
    public void RegistrationAndUpdatesPreserveEndpointsPassingBasicChecks(string endpoint)
        => AssertEndpointPreserved(endpoint);

    private static void AssertEndpointPreserved(string endpoint)
    {
        using var context = new RegistryTestContext();
        context.SetFee(0);
        using (var engine = context.Invoke("registerRelay", context.Relay, Id(context.Relay), Text(endpoint)))
            BooleanResult(engine, true);
        Entry(context, context.Relay, endpoint, "active", context.Time);

        context.Time++;
        using (var engine = context.Invoke("updateRelayEndpoint", context.Relay, Id(context.Relay), Text("https://original.example/meshline/v1")))
            BooleanResult(engine, true);
        context.Time++;
        using (var engine = context.Invoke("updateRelayEndpoint", context.Relay, Id(context.Relay), Text(endpoint)))
            BooleanResult(engine, true);
        Entry(context, context.Relay, endpoint, "active", context.Time);
    }

    [Theory]
    [MemberData(nameof(InvalidEndpoints))]
    public void RegistrationAndUpdatesRejectInvalidEndpoints(string endpoint)
    {
        using var context = new RegistryTestContext();
        AssertInvalidEndpoint(context, Text(endpoint));
    }

    [Fact]
    public void EndpointsDelegateAsciiWhitespaceAndControlValidationToClients()
    {
        foreach (var c in Enumerable.Range(0, 33).Append(0x7f))
        {
            AssertEndpointPreserved($"https://relay{(char)c}.example/meshline/v1");
            AssertEndpointPreserved($"https://relay.example/{(char)c}/meshline/v1");
        }
    }

    [Fact]
    public void MalformedEndpointUtf8FaultsAtVmStringEvents()
    {
        byte[][] malformed = [[0xff], [0xc0, 0xaf], [0xe2, 0x82], [0xed, 0xa0, 0x80], [0xf4, 0x90, 0x80, 0x80]];
        foreach (var bytes in malformed)
        {
            using var context = new RegistryTestContext();
            context.SetFee(0);
            var endpoint = new VMByteString(Encoding.UTF8.GetBytes("https://relay.example/")
                .Concat(bytes).Concat(Encoding.UTF8.GetBytes("/meshline/v1")).ToArray());

            // The basic validator does not decode UTF-8. Neo checks the String
            // event parameter and faults; the invocation snapshot is discarded.
            var registration = context.Snapshot.CloneCache();
            using (var register = context.InvokeOn(registration, "registerRelay", context.Relay, Id(context.Relay), endpoint))
                Fault(register, "does not match the formal parameter");
            Assert.False(context.Snapshot.Contains(context.Key("relay", context.Relay)));

            context.Seed(context.Relay, "active");
            var before = context.RecordBytes(context.Relay);
            var updating = context.Snapshot.CloneCache();
            using (var update = context.InvokeOn(updating, "updateRelayEndpoint", context.Relay, Id(context.Relay), endpoint))
                Fault(update, "does not match the formal parameter");
            Assert.Equal(before, context.RecordBytes(context.Relay));
        }
    }

    [Fact]
    public void EndpointsRejectWrongVmTypes()
    {
        using var context = new RegistryTestContext();
        foreach (var item in InvalidScalarTypes()) AssertInvalidEndpoint(context, item);
        AssertInvalidEndpoint(context, new Neo.VM.Types.Buffer(Encoding.UTF8.GetBytes("https://relay.example/meshline/v1")));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("disabled")]
    [InlineData("suspended")]
    public void EndpointChangesPreserveStatusAndNoOpsPreserveWholeRecord(string status)
    {
        using var context = new RegistryTestContext();
        context.Seed(context.Relay, status);
        const string endpoint = "https://RELAY.example:443/meshline/v1";
        using (var update = context.Invoke("updateRelayEndpoint", context.Relay, Id(context.Relay), Text(endpoint)))
            BooleanResult(update, true);
        Entry(context, context.Relay, endpoint, status, context.Time);
        var before = context.RecordBytes(context.Relay);
        context.Time += 1000;
        using (var noOp = context.Invoke("updateRelayEndpoint", context.Relay, Id(context.Relay), Text(endpoint)))
        {
            BooleanResult(noOp, true);
            Assert.Empty(noOp.Notifications);
        }
        Assert.Equal(before, context.RecordBytes(context.Relay));

        // Equal origins do not make differently spelled endpoints equal.
        const string changed = "https://relay.example/meshline/v1";
        using (var update = context.Invoke("updateRelayEndpoint", context.Relay, Id(context.Relay), Text(changed)))
            BooleanResult(update, true);
        Entry(context, context.Relay, changed, status, context.Time);
    }

    private static string EndpointAtByteLength(int length, bool multibyte)
    {
        const string prefix = "https://relay.example/";
        const string suffix = "/meshline/v1";
        var remaining = length - Encoding.UTF8.GetByteCount(prefix + suffix);
        var path = multibyte ? new string('中', remaining / 3) + new string('a', remaining % 3) : new string('a', remaining);
        return prefix + path + suffix;
    }

    private static void AssertInvalidEndpoint(RegistryTestContext context, StackItem endpoint)
    {
        using (var register = context.Invoke("registerRelay", context.Relay, Id(context.Relay), endpoint))
            Fault(register, "Invalid endpoint");
        if (!context.Snapshot.Contains(context.Key("relay", context.Relay)))
            context.Seed(context.Relay, "active");
        var before = context.RecordBytes(context.Relay);
        using var update = context.Invoke("updateRelayEndpoint", context.Relay, Id(context.Relay), endpoint);
        Fault(update, "Invalid endpoint");
        Assert.Equal(before, context.RecordBytes(context.Relay));
    }
}
