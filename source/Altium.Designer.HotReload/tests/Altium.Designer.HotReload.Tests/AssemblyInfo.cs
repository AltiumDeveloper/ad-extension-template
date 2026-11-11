using Xunit;

// The tests check that unloaded generations are garbage collected. A test running in parallel can hold any
// generation for a moment (e.g. while it enumerates the loaded assemblies), so run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
