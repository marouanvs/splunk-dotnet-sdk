using Xunit;

// The diagnostics tests observe process-global ActivitySource and Meter
// listeners, so test classes must not run in parallel with each other or the
// listeners would capture activities and measurements from unrelated tests.
// The original single-class test file ran serially by construction; this
// preserves that behavior now that tests are split into per-area classes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
