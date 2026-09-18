using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Meshline.Contracts;

[DisplayName("MeshlineRegistry")]
[ContractAuthor("Meshline", "dev@meshline.org")]
[ContractVersion("2.0.0")]
[ContractDescription("Meshline Relay Registry")]
[ManifestExtra("Protocol", "meshline.protocol.v1.0")]
[ContractPermission("0xd2a4cff31913016155e38e474a2c06d08be276cf", "transfer")]
public class MeshlineRegistry : SmartContract
{
    private const string OwnerKey = "owner";
    private const string RelayRegistrationFeeKey = "registrationFee";
    private const string RelayPrefix = "relay";
    private const string PendingRelayPrefix = "pendingRelay";
    private const long DefaultRelayRegistrationFee = 10_000_000_000;
    private const int MaxEndpointLength = 512;
    private const string EndpointPrefix = "https://";

    public delegate void RelayRegisteredHandler(UInt160 relayId, string endpoint, string status, ulong updatedAt);
    public delegate void RelayRegistrationFeeChangedHandler(BigInteger previousAmount, BigInteger newAmount, ulong updatedAt);
    public delegate void RelayStatusChangedHandler(UInt160 relayId, string status, ulong updatedAt);
    public delegate void RelayEndpointChangedHandler(UInt160 relayId, string oldEndpoint, string newEndpoint, ulong updatedAt);
    public delegate void OwnerChangedHandler(UInt160 previousOwner, UInt160 newOwner);

    public static event RelayRegisteredHandler RelayRegistered;
    public static event RelayRegistrationFeeChangedHandler RelayRegistrationFeeChanged;
    public static event RelayStatusChangedHandler RelayStatusChanged;
    public static event RelayEndpointChangedHandler RelayEndpointChanged;
    public static event OwnerChangedHandler OwnerChanged;

    private static StorageMap RelayRegistry => new(RelayPrefix);
    private static StorageMap PendingRelays => new(PendingRelayPrefix);

    [SuppressMessage("Style", "IDE1006")]
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        UInt160 owner = (UInt160)data ?? throw new Exception("owner is required");
        Storage.Put(OwnerKey, owner);
    }

    public static bool Verify()
    {
        return Runtime.CheckWitness(GetOwner());
    }

    [Safe]
    public static UInt160 GetOwner()
    {
        return (UInt160)Storage.Get(OwnerKey);
    }

    public static bool SetOwner(UInt160 newOwner)
    {
        if (newOwner == null) throw new Exception("New owner cannot be null");
        UInt160 current = GetOwner();
        if (!Runtime.CheckWitness(current)) return false;
        Storage.Put(OwnerKey, newOwner);
        OwnerChanged(current, newOwner);
        return true;
    }

    public static void Update(ByteString nefFile, string manifest)
    {
        if (!Runtime.CheckWitness(GetOwner())) throw new Exception("No authorization");
        ContractManagement.Update(nefFile, manifest);
    }

    [Safe]
    public static BigInteger GetRelayRegistrationFee()
    {
        ByteString stored = Storage.Get(RelayRegistrationFeeKey);
        return stored == null ? DefaultRelayRegistrationFee : (BigInteger)stored;
    }

    public static bool SetRelayRegistrationFee(BigInteger amount)
    {
        if (amount < 0) throw new Exception("Relay registration fee cannot be negative");
        if (!Runtime.CheckWitness(GetOwner())) return false;

        BigInteger previous = GetRelayRegistrationFee();
        if (amount == previous) return true;
        ulong updatedAt = Runtime.Time;
        Storage.Put(RelayRegistrationFeeKey, amount);
        RelayRegistrationFeeChanged(previous, amount, updatedAt);
        return true;
    }

    public static bool RegisterRelay(UInt160 relayId, string endpoint)
    {
        ValidateRelayId(relayId);
        ValidateEndpoint(endpoint);
        if (!Runtime.CheckWitness(relayId)) return false;
        if (HasRelay(relayId)) return false;

        BigInteger registrationFee = GetRelayRegistrationFee();
        if (registrationFee > 0)
        {
            PendingRelays.Put(relayId, endpoint);
            bool transferred = GAS.Transfer(
                relayId,
                Runtime.ExecutingScriptHash,
                registrationFee,
                endpoint);
            if (!transferred) throw new Exception("Relay registration payment failed");
            if (PendingRelays.Get(relayId) != null) throw new Exception("Relay registration is still pending");
            if (HasRelay(relayId)) throw new Exception("Relay was registered during registration payment");
        }

        ulong updatedAt = Runtime.Time;
        RelayEntry entry = new()
        {
            RelayId = relayId,
            Endpoint = endpoint,
            Status = "active",
            UpdatedAt = updatedAt
        };
        RelayRegistry.Put(relayId, StdLib.Serialize(entry));
        RelayRegistered(relayId, endpoint, "active", updatedAt);
        return true;
    }

    [DisplayName("onNEP17Payment")]
    public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
    {
        if (Runtime.CallingScriptHash != GAS.Hash) throw new Exception("Only GAS is accepted");
        ValidateRelayId(from);
        BigInteger registrationFee = GetRelayRegistrationFee();
        if (registrationFee == 0) throw new Exception("Relay registration payment is disabled");
        if (amount != registrationFee) throw new Exception("Invalid relay registration fee");
        if (HasRelay(from)) throw new Exception("Relay is already registered");

        ByteString pending = PendingRelays.Get(from);
        if (pending == null) throw new Exception("Relay registration is not pending");
        string endpoint = (string)data;
        if ((string)pending != endpoint)
            throw new Exception("Relay registration data does not match");

        PendingRelays.Delete(from);
    }

    public static bool UpdateRelayEndpoint(UInt160 relayId, string endpoint)
    {
        ValidateRelayId(relayId);
        ValidateEndpoint(endpoint);
        if (!Runtime.CheckWitness(relayId)) return false;

        ByteString data = RelayRegistry.Get(relayId);
        if (data == null) return false;

        RelayEntry entry = (RelayEntry)StdLib.Deserialize(data);
        if (entry.Endpoint == endpoint) return true;

        string oldEndpoint = entry.Endpoint;
        entry.Endpoint = endpoint;
        entry.UpdatedAt = Runtime.Time;
        RelayRegistry.Put(relayId, StdLib.Serialize(entry));
        RelayEndpointChanged(relayId, oldEndpoint, endpoint, entry.UpdatedAt);
        return true;
    }

    public static bool SetRelayEnabled(UInt160 relayId, bool enabled)
    {
        ValidateRelayId(relayId);
        if (!Runtime.CheckWitness(relayId)) return false;

        ByteString data = RelayRegistry.Get(relayId);
        if (data == null) return false;

        RelayEntry entry = (RelayEntry)StdLib.Deserialize(data);
        if (entry.Status == "suspended") return false;
        string status = enabled ? "active" : "disabled";
        if (entry.Status == status) return true;
        entry.Status = status;
        entry.UpdatedAt = Runtime.Time;
        RelayRegistry.Put(relayId, StdLib.Serialize(entry));
        RelayStatusChanged(relayId, status, entry.UpdatedAt);
        return true;
    }

    public static bool SuspendRelay(UInt160 relayId)
    {
        ValidateRelayId(relayId);
        if (!Runtime.CheckWitness(GetOwner())) return false;

        ByteString data = RelayRegistry.Get(relayId);
        if (data == null) return false;

        RelayEntry entry = (RelayEntry)StdLib.Deserialize(data);
        if (entry.Status == "suspended") return true;
        entry.Status = "suspended";
        entry.UpdatedAt = Runtime.Time;
        RelayRegistry.Put(relayId, StdLib.Serialize(entry));
        RelayStatusChanged(relayId, entry.Status, entry.UpdatedAt);
        return true;
    }

    public static bool RestoreRelay(UInt160 relayId)
    {
        ValidateRelayId(relayId);
        if (!Runtime.CheckWitness(GetOwner())) return false;

        ByteString data = RelayRegistry.Get(relayId);
        if (data == null) return false;

        RelayEntry entry = (RelayEntry)StdLib.Deserialize(data);
        if (entry.Status != "suspended") return false;
        entry.Status = "disabled";
        entry.UpdatedAt = Runtime.Time;
        RelayRegistry.Put(relayId, StdLib.Serialize(entry));
        RelayStatusChanged(relayId, entry.Status, entry.UpdatedAt);
        return true;
    }

    [Safe]
    public static RelayEntry GetRelay(UInt160 relayId)
    {
        ValidateRelayId(relayId);
        ByteString data = RelayRegistry.Get(relayId);
        if (data == null) return null;
        return (RelayEntry)StdLib.Deserialize(data);
    }

    [Safe]
    public static bool HasRelay(UInt160 relayId)
    {
        ValidateRelayId(relayId);
        return RelayRegistry.Get(relayId) != null;
    }

    [Safe]
    public static Iterator ListRelays()
    {
        return RelayRegistry.Find(FindOptions.ValuesOnly | FindOptions.DeserializeValues);
    }

    private static void ValidateEndpoint(string endpoint)
    {
        if ((object)endpoint is not string || endpoint.Length > MaxEndpointLength || !endpoint.StartsWith(EndpointPrefix))
            throw new Exception("Invalid endpoint");
    }

    private static void ValidateRelayId(UInt160 relayId)
    {
        if (relayId == null || !relayId.IsValid) throw new Exception("Invalid relay ID");
    }
}
