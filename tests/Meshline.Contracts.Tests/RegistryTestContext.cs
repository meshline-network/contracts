using System.Numerics;
using System.Text;
using Neo;
using Neo.Extensions;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using StackItem = Neo.VM.Types.StackItem;
using VMInteger = Neo.VM.Types.Integer;
using static Meshline.Contracts.Tests.RegistryAssert;

namespace Meshline.Contracts.Tests;

// VM test classes share the "Registry VM" collection to serialize NeoSystem lifetimes.
internal sealed class RegistryTestContext : IDisposable
{
    internal const string SeedEndpoint = "https://seed.example/meshline/v1";
    internal const ulong SeedTimestamp = 1_730_000_000_000;
    internal readonly UInt160 Owner = UInt160.Parse("0x2222222222222222222222222222222222222222");
    internal readonly UInt160 Relay = UInt160.Parse("0x1111111111111111111111111111111111111111");
    internal readonly StoreCache Snapshot;
    internal readonly ContractState Contract;
    internal readonly byte[] NefBytes;
    internal readonly string ManifestJson;
    internal ulong Time = 1_730_000_100_000;
    internal WitnessScope Scope = WitnessScope.CustomContracts;

    private readonly ProtocolSettings settings;
    private readonly NeoSystem system;

    internal RegistryTestContext()
    {
        var artifacts = ContractArtifacts.Load();
        NefBytes = File.ReadAllBytes(artifacts.NefPath);
        ManifestJson = File.ReadAllText(artifacts.ManifestPath);
        Contract = new ContractState
        {
            Id = 1,
            Hash = UInt160.Parse("0x4444444444444444444444444444444444444444"),
            Nef = NefFile.Parse(NefBytes, verify: true),
            Manifest = ContractManifest.Parse(ManifestJson)
        };
        var committee = new Neo.Wallets.KeyPair(Convert.FromHexString(
            "0000000000000000000000000000000000000000000000000000000000000001"));
        settings = ProtocolSettings.Default with
        {
            Network = 860833102,
            StandbyCommittee = [committee.PublicKey],
            ValidatorsCount = 1
        };
        system = new NeoSystem(settings);
        Snapshot = system.GetSnapshotCache();
        Snapshot.Add(StorageKey.Create(NativeContract.ContractManagement.Id, 8, Contract.Hash),
            StorageItem.CreateSealed(Contract));
        Snapshot.Add(Key("owner"), new StorageItem(Owner.GetSpan().ToArray()));
    }

    internal ApplicationEngine Invoke(string method, UInt160? signer, params StackItem[] arguments) =>
        InvokeOn(Snapshot, method, signer, arguments);

    // Fault tests use an invocation snapshot and discard it, as a failed transaction does.
    internal ApplicationEngine InvokeOn(DataCache snapshot, string method, UInt160? signer, params StackItem[] arguments)
    {
        var contract = NativeContract.ContractManagement.GetContract(snapshot, Contract.Hash)!;
        var descriptor = contract.Manifest.Abi.GetMethod(method, arguments.Length)
            ?? throw new InvalidOperationException($"Compiled contract is missing {method}.");
        var engine = CreateEngine(snapshot, signer, Scope);
        var execution = engine.LoadContract(contract, descriptor, CallFlags.All);
        for (var index = arguments.Length - 1; index >= 0; index--)
            execution.EvaluationStack.Push(arguments[index]);
        engine.Execute();
        return engine;
    }

    internal ApplicationEngine TransferGas(DataCache snapshot, UInt160 payer, BigInteger amount, string data)
    {
        using var builder = new ScriptBuilder();
        builder.EmitDynamicCall(NativeContract.GAS.Hash, "transfer", CallFlags.All,
            payer, Contract.Hash, amount, data);
        var script = builder.ToArray();
        var engine = CreateEngine(snapshot, payer, WitnessScope.Global, script);
        engine.LoadScript(script);
        engine.Execute();
        return engine;
    }

    private ApplicationEngine CreateEngine(DataCache snapshot, UInt160? signer, WitnessScope scope,
        byte[]? script = null)
    {
        var transaction = signer is null ? null : new Transaction
        {
            Signers = [new Signer
            {
                Account = signer,
                Scopes = scope,
                AllowedContracts = scope == WitnessScope.CustomContracts
                    ? [Contract.Hash, NativeContract.GAS.Hash] : null
            }],
            Attributes = [],
            Script = script ?? ReadOnlyMemory<byte>.Empty,
            Witnesses = []
        };
        return ApplicationEngine.Create(TriggerType.Application, transaction, snapshot,
            persistingBlock: new Block
            {
                Header = new Header
                {
                    Version = 0,
                    PrevHash = UInt256.Zero,
                    MerkleRoot = UInt256.Zero,
                    Timestamp = Time,
                    Nonce = 0,
                    Index = 1,
                    PrimaryIndex = 0,
                    NextConsensus = UInt160.Zero,
                    Witness = new Witness
                    {
                        InvocationScript = ReadOnlyMemory<byte>.Empty,
                        VerificationScript = ReadOnlyMemory<byte>.Empty
                    }
                },
                Transactions = []
            }, settings: settings);
    }

    internal void SetFee(long amount)
    {
        using var engine = Invoke("setRelayRegistrationFee", Owner, new VMInteger(amount));
        BooleanResult(engine, true);
    }

    internal void Seed(UInt160 id, string status, string endpoint = SeedEndpoint)
    {
        var entry = new Neo.VM.Types.Struct
        {
            Id(id), Text(endpoint), Text(status), new VMInteger(SeedTimestamp)
        };
        Snapshot.Add(Key("relay", id),
            new StorageItem(BinarySerializer.Serialize(entry, ExecutionEngineLimits.Default)));
    }

    internal void SeedGasBalance(UInt160 account, BigInteger amount) =>
        Snapshot.Add(StorageKey.Create(NativeContract.GAS.Id, (byte)20, account),
            new StorageItem(new AccountState { Balance = amount }));

    internal BigInteger GasBalance(UInt160 account) => NativeContract.GAS.BalanceOf(Snapshot, account);
    internal byte[] RecordBytes(UInt160 id) => Snapshot[Key("relay", id)].Value.ToArray();

    internal StorageKey Key(string prefix, UInt160? id = null) => new()
    {
        Id = Contract.Id,
        Key = id is null ? Encoding.UTF8.GetBytes(prefix)
            : Encoding.UTF8.GetBytes(prefix).Concat(id.GetSpan().ToArray()).ToArray()
    };

    public void Dispose()
    {
        Snapshot.Dispose();
        system.Dispose();
    }
}
