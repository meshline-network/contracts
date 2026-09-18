# Meshline Contracts

Neo N3 reference contracts for the [Meshline Protocol](https://github.com/meshline-network/protocol).
`MeshlineRegistry` stores public-relay identities, discovery endpoints, and membership
status. The [Registry specification](https://github.com/meshline-network/protocol/blob/main/v1/en/registry/README.md)
defines the interoperable data and interfaces.

## Build and test

Install .NET SDK **10.0.401**, as specified in [global.json](global.json), then run
from the repository root:

```powershell
dotnet tool restore
dotnet restore Meshline.Contracts.slnx
dotnet build Meshline.Contracts.slnx --configuration Release --no-restore
dotnet test --project tests/Meshline.Contracts.Tests/Meshline.Contracts.Tests.csproj --configuration Release --no-build --no-restore
```

The local tool manifest pins the `nccs` compiler to 3.10.0; the contract uses
Neo.SmartContract.Framework 3.9.1. Tests execute the generated NEF in a local Neo VM.
[CI](.github/workflows/ci.yml) restores, builds in Release, and runs the tests on
Windows and Linux.

## Build outputs

The build writes both deployment files to `src/Meshline.Contracts/bin/sc/`:

| File | Purpose |
|---|---|
| `MeshlineRegistry.nef` | Compiled Neo VM executable |
| `MeshlineRegistry.manifest.json` | Exported ABI, permissions, and contract metadata |

## Repository layout

| Path | Contents |
|---|---|
| [src/Meshline.Contracts/](src/Meshline.Contracts/) | Contract source and build configuration |
| [tests/Meshline.Contracts.Tests/](tests/Meshline.Contracts.Tests/) | Contract and ABI tests |
| [docs/abi.md](docs/abi.md) | Complete method, event, and record reference |
| [docs/operations.md](docs/operations.md) | Deployment, upgrades, relay maintenance, and governance |
