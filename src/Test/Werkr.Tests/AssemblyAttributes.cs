// Integration tests share a single Testcontainer database and authenticated
// HttpClient via AppHostFixture.  Running them in parallel would cause
// cross-test interference, so parallelisation is explicitly disabled.
[assembly: DoNotParallelize]
