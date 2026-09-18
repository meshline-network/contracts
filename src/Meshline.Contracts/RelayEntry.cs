using Neo.SmartContract.Framework;

namespace Meshline.Contracts;

public class RelayEntry
{
    public UInt160 RelayId;
    public string Endpoint;
    public string Status;
    public ulong UpdatedAt;
}
