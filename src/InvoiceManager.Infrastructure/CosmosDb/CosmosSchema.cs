namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// The single source of truth for the Cosmos DB container topology this service
/// depends on: container name and partition key path. Repositories reference these
/// definitions so container names never drift from the schema, and the local Aspire
/// bootstrap (the Seeder's <c>--ensure-schema</c> mode) creates exactly these
/// containers against the emulator.
/// </summary>
/// <remarks>
/// In the cloud these containers are provisioned by Terraform
/// (<c>infra/terraform/main.tf</c>), which remains the sole owner of cloud schema.
/// A guard test asserts that the definitions here match the Terraform resources so
/// the two cannot silently diverge.
/// </remarks>
public static class CosmosSchema
{
    /// <summary>Stores <c>InvoiceConfiguration</c> documents.</summary>
    public static ContainerDefinition InvoiceConfigurations { get; } =
        new("invoice-configurations", "/partitionKey");

    /// <summary>Stores <c>InvoiceRecord</c> documents.</summary>
    public static ContainerDefinition InvoiceRecords { get; } =
        new("invoice-records", "/configurationId");

    /// <summary>Every container this service requires.</summary>
    public static IReadOnlyList<ContainerDefinition> Containers { get; } =
        [InvoiceConfigurations, InvoiceRecords];
}

/// <summary>
/// A Cosmos DB container's identity: its name and the partition key path. Both are
/// always present — a container cannot exist without them.
/// </summary>
public sealed record ContainerDefinition(string Name, string PartitionKeyPath);
