# MeshlineRegistry ABI

[Project](../README.md) · [Operations](operations.md)

This reference describes all 16 methods and 5 events exported by `MeshlineRegistry`.
Signatures use Neo manifest types; parameter order and the `safe` flag match the
generated `MeshlineRegistry.manifest.json`. Behavior follows the
[contract source](../src/Meshline.Contracts/MeshlineRegistry.cs).

The [Registry protocol](https://github.com/meshline-network/protocol/blob/main/v1/en/registry/methods.md)
requires six methods: `getRelay`, `listRelays`, `getRelayRegistrationFee`,
`registerRelay`, `updateRelayEndpoint`, and `setRelayEnabled`. They are marked
**Protocol** below. The other methods and all events are reference implementation
extensions.

## Types and validation

`Hash160` represents a Neo `UInt160`. Every `relayId` parameter, and `from` in the
payment callback, must be a non-null, valid 20-byte value; invalid IDs cause FAULT.
Protocol text represents a relay ID as `0x` followed by 40 lowercase hex digits.

`registerRelay` and `updateRelayEndpoint` apply three endpoint checks:

- A non-null VM ByteString, exposed as ABI `String`.
- At most 512 bytes.
- An exact `https://` prefix.

Failure causes FAULT. The contract preserves the supplied value. SDKs and
applications must validate UTF-8 and the complete
[protocol endpoint constraints](https://github.com/meshline-network/protocol/blob/main/v1/en/registry/core-objects.md#relayentry)
before submission and before using a retrieved endpoint.

All fee amounts are integers in the smallest GAS unit: `100000000` equals `1 GAS`.
Record and event timestamps use UTC Unix milliseconds from the executing block.
Methods marked `safe: true` are read-only and require no witness. For mutating
methods, `true` means the request succeeded, including any documented unchanged-value
case; `false` means the request was declined without changing business state.
Relay ID and endpoint validation run before witness checks in relay methods.
A FAULT rolls back record changes and registration transfers; transaction execution
fees still apply.

## RelayEntry

Records returned by `getRelay` and `listRelays` contain this ordered sequence,
defined by [RelayEntry.cs](../src/Meshline.Contracts/RelayEntry.cs):

| Position | Source field | Protocol field | Logical type | Meaning |
|---|---|---|---|---|
| 0 | `RelayId` | `relay_id` | UInt160 | Relay account script hash |
| 1 | `Endpoint` | `endpoint` | String | HTTPS discovery base address |
| 2 | `Status` | `status` | String | `active`, `disabled`, or `suspended` |
| 3 | `UpdatedAt` | `updated_at` | Unsigned integer | Most recent creation, endpoint change, or status change |

An `active` relay is a discovery candidate; `disabled` means the operator has
disabled membership; `suspended` means governance has suspended membership. Queries
retain all three states. Candidate use also requires the protocol's descriptor
and connection-identity checks.

## Queries

### getRelay

`getRelay(relayId: Hash160) -> Any` · **Protocol** · `safe: true` · No witness.

Returns the current `RelayEntry`, or null if no record exists. The manifest return
type is `Any`; the returned record has the field order above. Invalid `relayId`
causes FAULT. Repeated reads leave the record and timestamp unchanged.

### listRelays

`listRelays() -> InteropInterface` · **Protocol** · `safe: true` · No witness.

Returns a Neo Iterator of `RelayEntry` values in every state, including an empty
iterator when no relays exist. Enumeration order is unspecified. It returns neither
a Boolean nor a materialized array and does not update records. RPC traversal
depends on the node's iterator support; see [Inspect state](operations.md#inspect-state).

### getRelayRegistrationFee

`getRelayRegistrationFee() -> Integer` · **Protocol** · `safe: true` · No witness.

Returns the configured fee, or the default `10000000000` (`100 GAS`) when unset.
Zero means no registration fee. Reading the default does not store it. The result
reflects query time; `registerRelay` charges the fee at execution time.

### hasRelay

`hasRelay(relayId: Hash160) -> Boolean` · Extension · `safe: true` · No witness.

Returns `true` if a record exists in any state, otherwise `false`. Invalid `relayId`
causes FAULT. Repeated calls do not change the record.

### getOwner

`getOwner() -> Hash160` · Extension · `safe: true` · No witness.

Returns the current governance account initialized during deployment. Repeated
calls do not change it.

## Relay maintenance

### registerRelay

`registerRelay(relayId: Hash160, endpoint: String) -> Boolean` · **Protocol** · `safe: false` · Requires the `relayId` witness.

Creates an `active` record with the current block timestamp, emits `RelayRegistered`,
and returns `true`. A positive fee is transferred from the relay account to the
Registry atomically with registration; zero skips the transfer. See
[paid registration](operations.md#paid-registration) for signer scope requirements.

Returns `false` for insufficient authorization or an existing record, without
charging a registration fee or replacing the record. Invalid ID or endpoint,
failed payment, or an inconsistent payment callback causes FAULT. A repeated
registration therefore returns `false`, even when the endpoint is identical.

### updateRelayEndpoint

`updateRelayEndpoint(relayId: Hash160, endpoint: String) -> Boolean` · **Protocol** · `safe: false` · Requires the `relayId` witness.

For an existing record, changes the endpoint and timestamp, emits
`RelayEndpointChanged`, and returns `true`. An identical endpoint returns `true`
without changing any field or emitting an event. Status is preserved, including
`suspended`. Returns `false` for insufficient authorization or a missing record.
Invalid ID or endpoint causes FAULT.

### setRelayEnabled

`setRelayEnabled(relayId: Hash160, enabled: Boolean) -> Boolean` · **Protocol** · `safe: false` · Requires the `relayId` witness.

`enabled: true` requests `active`; `false` requests `disabled`. A change updates
the status and timestamp and emits `RelayStatusChanged`. An already matching
status returns `true` without changing the record or emitting an event.
Returns `false` for insufficient authorization, a missing record, or a suspended
relay. Invalid `relayId` causes FAULT. Supply `enabled` as an ABI Boolean.

## Governance

### setRelayRegistrationFee

`setRelayRegistrationFee(amount: Integer) -> Boolean` · Extension · `safe: false` · Requires the current owner witness.

Sets a non-negative registration fee, emits `RelayRegistrationFeeChanged`, and
returns `true`. Zero enables registration without a registration transfer. An
unchanged amount returns `true` without a write or event. Returns `false` when
authorization is insufficient; a negative amount causes FAULT. Existing relay
records are unaffected.

### suspendRelay

`suspendRelay(relayId: Hash160) -> Boolean` · Extension · `safe: false` · Requires the current owner witness.

Sets an existing record to `suspended`, updates its timestamp, emits
`RelayStatusChanged`, and returns `true`. An already suspended record returns
`true` unchanged, with no event. Returns `false` for insufficient authorization or
a missing record. Invalid `relayId` causes FAULT. The operator cannot enable or
disable membership while suspended.

### restoreRelay

`restoreRelay(relayId: Hash160) -> Boolean` · Extension · `safe: false` · Requires the current owner witness.

Changes a suspended record to `disabled`, updates its timestamp, emits
`RelayStatusChanged`, and returns `true`. The operator must separately enable it.
Returns `false` for insufficient authorization, a missing record, or any state
other than `suspended`. A repeated restore therefore returns `false` without a
change. Invalid `relayId` causes FAULT.

### setOwner

`setOwner(newOwner: Hash160) -> Boolean` · Extension · `safe: false` · Requires the current owner witness.

Stores the supplied governance account, emits `OwnerChanged`, and returns `true`.
Returns `false` for insufficient authorization; null `newOwner` causes FAULT.
The new account need not sign this call. Setting the same owner still writes the
value and emits the event.

## Lifecycle and payment callback

### _deploy

`_deploy(data: Any, update: Boolean) -> Void` · Extension · `safe: false` · Neo deployment/update callback.

On first deployment, `data` supplies the owner as a `Hash160` value. Null or data
that cannot be cast to `UInt160` causes FAULT. When `update` is `true`, the callback
returns immediately, preserving the current owner and records. It has no Boolean
result and emits no contract event.

### verify

`verify() -> Boolean` · Extension · `safe: false` · Contract account verification entry point.

Returns the result of checking the current owner witness: `true` when authorized,
otherwise `false`. It performs no writes and emits no event on repeated calls.
The manifest does not mark this method as safe.

### update

`update(nefFile: ByteArray, manifest: String) -> Void` · Extension · `safe: false` · Requires the current owner witness.

Updates the executable and manifest through native ContractManagement. `nefFile`
contains the compiled NEF bytes; `manifest` contains the manifest JSON text.
Insufficient authorization causes FAULT with `No authorization`; invalid upgrade
artifacts are rejected by the native contract. Success has no Boolean result and
emits no event defined by this contract. It preserves the contract hash and stored
state; repeating the call requests another native update rather than an unchanged-value
shortcut.

### onNEP17Payment

`onNEP17Payment(from: Hash160, amount: Integer, data: Any) -> Void` · Extension · `safe: false` · Native GAS callback during registration.

Accepts payment only from the native GAS contract during a pending `registerRelay`
call. `from` is the registering relay, `amount` must equal the current positive
fee, and `data` must match the pending endpoint. It consumes the pending payment;
`registerRelay` subsequently creates the record. It has no Boolean result or event.

A different caller, invalid payer, zero or mismatched fee, existing relay, absent
pending registration, or mismatched data causes FAULT. Direct transfers to the
Registry and repeated callbacks without a pending registration are rejected.

## Events

Event parameters are listed below with their exported names and ABI types, in
declaration order.

| Event | Parameters in ABI order | Emitted when |
|---|---|---|
| `RelayRegistered` | `relayId: Hash160`, `endpoint: String`, `status: String`, `updatedAt: Integer` | Registration succeeds; status is `active` |
| `RelayRegistrationFeeChanged` | `previousAmount: Integer`, `newAmount: Integer`, `updatedAt: Integer` | The fee actually changes |
| `RelayStatusChanged` | `relayId: Hash160`, `status: String`, `updatedAt: Integer` | An operator or owner changes membership status |
| `RelayEndpointChanged` | `relayId: Hash160`, `oldEndpoint: String`, `newEndpoint: String`, `updatedAt: Integer` | The endpoint actually changes |
| `OwnerChanged` | `previousOwner: Hash160`, `newOwner: Hash160` | An authorized owner assignment succeeds, including the same owner |
