// Each test in this assembly starts a full Aspire AppHost with its own Cosmos emulator.
// Running them in parallel starves the machine and the emulator returns 503 / times out,
// so run this assembly's tests sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
