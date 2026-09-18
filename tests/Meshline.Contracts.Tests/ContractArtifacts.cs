namespace Meshline.Contracts.Tests;

internal sealed record ContractArtifacts(string NefPath, string ManifestPath)
{
    public static ContractArtifacts Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "contract");
        return new(
            Path.Combine(directory, "MeshlineRegistry.nef"),
            Path.Combine(directory, "MeshlineRegistry.manifest.json"));
    }
}
