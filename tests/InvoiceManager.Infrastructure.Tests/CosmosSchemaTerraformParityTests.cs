using System.Text.RegularExpressions;
using InvoiceManager.Infrastructure.CosmosDb;

namespace InvoiceManager.Infrastructure.Tests;

/// <summary>
/// Guards the split-ownership decision: the local Aspire bootstrap creates the emulator
/// schema from <see cref="CosmosSchema"/>, while Terraform provisions the same containers
/// in the cloud (<c>infra/terraform/main.tf</c>). This test fails if the two definitions
/// drift so a change to one forces a matching change to the other.
/// </summary>
public sealed class CosmosSchemaTerraformParityTests
{
    private static readonly Regex ContainerBlock = new(
        """resource\s+"azurerm_cosmosdb_sql_container"\s+"[^"]+"\s*\{(?<body>.*?)\blifecycle\b""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex NameLine = new(
        """name\s*=\s*"(?<name>[^"]+)""" + "\"", RegexOptions.Compiled);

    private static readonly Regex PartitionKeyLine = new(
        """partition_key_paths\s*=\s*\[\s*"(?<path>[^"]+)"\s*\]""", RegexOptions.Compiled);

    [Fact]
    public void EveryCosmosSchemaContainerMatchesTerraform()
    {
        var terraform = ReadTerraformMainTf();
        var terraformContainers = ParseContainers(terraform);

        foreach (var container in CosmosSchema.Containers)
        {
            Assert.True(
                terraformContainers.TryGetValue(container.Name, out var partitionKeyPath),
                $"Terraform has no azurerm_cosmosdb_sql_container named '{container.Name}'. " +
                $"CosmosSchema and infra/terraform/main.tf have drifted.");

            Assert.Equal(container.PartitionKeyPath, partitionKeyPath);
        }
    }

    private static Dictionary<string, string> ParseContainers(string terraform)
    {
        var containers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match block in ContainerBlock.Matches(terraform))
        {
            var body = block.Groups["body"].Value;
            var name = NameLine.Match(body);
            var partitionKey = PartitionKeyLine.Match(body);

            Assert.True(name.Success, $"Container block is missing a name:\n{body}");
            Assert.True(partitionKey.Success, $"Container block is missing partition_key_paths:\n{body}");

            containers[name.Groups["name"].Value] = partitionKey.Groups["path"].Value;
        }

        return containers;
    }

    private static string ReadTerraformMainTf()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "infra", "terraform", "main.tf");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate infra/terraform/main.tf by walking up from the test output directory.");
    }
}
