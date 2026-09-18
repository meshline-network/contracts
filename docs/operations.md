# MeshlineRegistry operations

[Project](../README.md) · [ABI reference](abi.md)

## Preparation

Build and test using the [README instructions](../README.md#build-and-test).
Use the NEF and manifest from the same build. Connect Neo-CLI and any RPC tools to
the intended network, and select that network's Registry contract.

The commands below run in the Neo-CLI interactive console. File paths are relative
to the contract repository root; adjust them if Neo-CLI runs from another directory.
Backticks around JSON are Neo-CLI raw-string delimiters.

| Placeholder | Value to supply |
|---|---|
| `<registry-hash>` | Registry contract script hash on the selected network |
| `<owner-hash>`, `<new-owner-hash>` | Current or new governance account script hash |
| `<relay-hash>` | Relay account script hash, matching its protocol identity |
| `<owner-address>`, `<relay-address>` | Corresponding Neo wallet address used as transaction sender |
| `<deployer-wallet.json>`, `<owner-wallet.json>`, `<relay-wallet.json>` | Wallet file paths |

Hash placeholders include the full `0x` prefix and 40 hex digits. Use `list address`
and `parse <owner-address>` or `parse <relay-address>` to identify accounts and
obtain their script hashes. The owner signs governance and upgrade operations;
the relay account signs registration and record maintenance.

For state-changing methods that return Boolean, require both `HALT` and a `true`
result from simulation before broadcasting. `HALT` with `false` means the request
was declined.
Deployment and upgrade have no Boolean success result. After broadcasting, wait
for confirmation, check execution through
[`getapplicationlog`](https://docs.neo.org/docs/n3/reference/rpc/getapplicationlog.html),
and read back the affected state. Simulation and a transaction hash alone do not
confirm an on-chain change. For read-only queries, choose `no` at any relay prompt.

## Deploy and upgrade

### First deployment

Pass the intended owner as deployment data:

```text
open wallet "<deployer-wallet.json>"
deploy --filePath src/Meshline.Contracts/bin/sc/MeshlineRegistry.nef --manifestPath src/Meshline.Contracts/bin/sc/MeshlineRegistry.manifest.json --data `{"type":"Hash160","value":"<owner-hash>"}`
```

Neo-CLI selects the deployment transaction sender from the open wallet. The
`--data` value separately selects the Registry's governance owner. Record the
reported contract hash as `<registry-hash>`. After confirmation, inspect the
contract with RPC
[`getcontractstate`](https://docs.neo.org/docs/n3/reference/rpc/getcontractstate.html)
and verify the owner:

```text
invoke <registry-hash> getOwner
```

### Upgrade an existing deployment

Keep the manifest name `MeshlineRegistry`, open the current owner's wallet, and
submit the new artifacts:

```text
open wallet "<owner-wallet.json>"
update <registry-hash> src/Meshline.Contracts/bin/sc/MeshlineRegistry.nef src/Meshline.Contracts/bin/sc/MeshlineRegistry.manifest.json <owner-address>
```

The current owner must sign. The update keeps the contract hash and stored state.
After confirmation, use `getcontractstate` to compare the deployed NEF checksum and
manifest with the build, then read back the owner, registration fee, and existing
relay records using [Inspect state](#inspect-state).

## Relay operations

### Prepare the relay account

```text
open wallet "<relay-wallet.json>"
list address
parse <relay-address>
invoke <registry-hash> getRelayRegistrationFee
```

The fee is an integer in the smallest GAS unit. The default is `10000000000`
(`100 GAS`); `0` means no registration fee. Fund the relay account for the
registration fee and transaction fees. The amount charged is the fee when
`registerRelay` executes, which can differ from the earlier query.

Validate the endpoint against the
[protocol](https://github.com/meshline-network/protocol/blob/main/v1/en/registry/core-objects.md#relayentry)
before submitting it. The examples use `https://relay.example.com/meshline/v1`;
replace it with the relay's discovery base address.

### Free registration

When the execution-time registration fee is zero, use:

```text
invoke <registry-hash> registerRelay `[{"type":"Hash160","value":"<relay-hash>"},{"type":"String","value":"https://relay.example.com/meshline/v1"}]` <relay-address>
```

Transaction fees still apply. A successful registration creates an `active`
record. An existing relay returns `false`, leaves its record unchanged, and pays
no registration fee. Use endpoint updates and enable/disable operations for an
existing record.

### Paid registration

Use a wallet or transaction tool that supports custom signer scopes. The relay
account must authorize both the Registry call and its nested native GAS transfer.
The standard Neo-CLI `invoke` command creates `CalledByEntry` signers and does not
expose a custom scope option, so it cannot perform this paid flow. See the
[Neo-CLI implementation](https://github.com/neo-project/neo-node/blob/master/src/Neo.CLI/CLI/MainService.Contracts.cs).

This RPC simulation request supplies the required invocation and signer
configuration. The second allowed contract is native GAS:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "invokefunction",
  "params": [
    "<registry-hash>",
    "registerRelay",
    [
      { "type": "Hash160", "value": "<relay-hash>" },
      { "type": "String", "value": "https://relay.example.com/meshline/v1" }
    ],
    [
      {
        "account": "<relay-hash>",
        "scopes": "CustomContracts",
        "allowedcontracts": [
          "<registry-hash>",
          "0xd2a4cff31913016155e38e474a2c06d08be276cf"
        ]
      }
    ]
  ]
}
```

[`invokefunction`](https://docs.neo.org/docs/n3/reference/rpc/invokefunction.html)
only simulates execution. After checking `HALT` and a `true` result, use the wallet
or transaction tool to build, sign, and broadcast the invocation with the relay
account as sender and the same signer scope. The Registry collects the fee during
registration; direct GAS transfers to the Registry are rejected. Failed payment
rolls back the transfer and record creation, while transaction fees still apply.
After confirmation, query `getRelay` to verify the stored identity, endpoint, and
`active` status.

### Change the endpoint

Use the relay account to update its record:

```text
invoke <registry-hash> updateRelayEndpoint `[{"type":"Hash160","value":"<relay-hash>"},{"type":"String","value":"https://new-relay.example.com/meshline/v1"}]` <relay-address>
```

The status stays unchanged, including while suspended. An identical endpoint
returns `true` without changing the record or timestamp. A real change updates the
endpoint and timestamp. Confirm the result with `getRelay`.

### Enable or disable membership

Enable the relay:

```text
invoke <registry-hash> setRelayEnabled `[{"type":"Hash160","value":"<relay-hash>"},{"type":"Boolean","value":true}]` <relay-address>
```

To disable it, use the same command with the Boolean value `false`. An already
matching status returns `true` with the entire record unchanged. A suspended relay
returns `false`; governance must restore it first. Confirm the status with `getRelay`.

## Governance

Open the current owner's wallet for these operations:

```text
open wallet "<owner-wallet.json>"
invoke <registry-hash> getOwner
```

### Set the registration fee

This example sets the fee to `10 GAS`:

```text
invoke <registry-hash> setRelayRegistrationFee `[{"type":"Integer","value":"1000000000"}]` <owner-address>
```

Use the integer string `"0"` for free registration. The amount must be non-negative;
setting the current amount returns `true` without a change. After confirmation,
read `getRelayRegistrationFee` to verify the new amount.

### Suspend or restore a relay

Suspend membership:

```text
invoke <registry-hash> suspendRelay `[{"type":"Hash160","value":"<relay-hash>"}]` <owner-address>
```

To lift the suspension:

```text
invoke <registry-hash> restoreRelay `[{"type":"Hash160","value":"<relay-hash>"}]` <owner-address>
```

Read `getRelay` after each confirmed operation. Restore changes `suspended` to
`disabled`; the relay operator must then use `setRelayEnabled` with `true` to become
active. Suspending an already suspended record returns `true` unchanged; restoring
a record that is not suspended returns `false`.

### Transfer ownership

Resolve the new account's script hash and submit the assignment using the current
owner:

```text
invoke <registry-hash> setOwner `[{"type":"Hash160","value":"<new-owner-hash>"}]` <owner-address>
```

The new owner does not sign this call. After confirmation, verify `getOwner` returns
the new account; subsequent governance and upgrade operations require that account.

## Inspect state

These read-only calls require no witness or transaction broadcast:

```text
invoke <registry-hash> getOwner
invoke <registry-hash> getRelayRegistrationFee
invoke <registry-hash> getRelay `[{"type":"Hash160","value":"<relay-hash>"}]`
invoke <registry-hash> hasRelay `[{"type":"Hash160","value":"<relay-hash>"}]`
```

Decode `getRelay` using the [RelayEntry field order](abi.md#relayentry).
Timestamps are UTC Unix milliseconds. Null means no record exists; a present
record can be `active`, `disabled`, or `suspended`.

For directory enumeration, call `listRelays` through RPC `invokefunction`. It
returns an iterator containing all states. If the node returns session and
iterator IDs, retrieve batches with
[`traverseiterator`](https://docs.neo.org/docs/n3/reference/rpc/traverseiterator.html)
and close the session with `terminatesession`. Otherwise, follow the node's
iterator configuration and result limits; an inline result may be truncated.
Only `active` records are discovery candidates, subject to the protocol's endpoint
and identity validation.
