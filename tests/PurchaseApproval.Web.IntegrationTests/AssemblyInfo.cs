// All classes share one physical test database, so test classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
