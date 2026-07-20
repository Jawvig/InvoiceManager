// Every test in the AdminWebAppHost collection shares one Aspire AppHost (Cosmos emulator,
// seeder, Functions, AdminWeb) via the collection fixture, but a second collection here would
// start its own AppHost and starve the machine running side by side with the first. Run this
// assembly's tests sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
