var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureCosmosDB("cosmos").RunAsEmulator();

builder.Build().Run();
