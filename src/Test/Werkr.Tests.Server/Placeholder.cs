using System.Reflection;

namespace Werkr.Tests.Server;

/// <summary>
/// Contains a basic smoke test to verify that the Werkr.Tests.Server project
/// compiles and can reference key types from dependent projects such as
/// <c>Werkr.Common.Configuration</c>. This test class acts as a minimal
/// viability check for the test project configuration.
/// </summary>
[TestClass]
public class Placeholder {
    /// <summary>
    /// Verifies that the <see cref="WerkrConfiguration"/> type from the
    /// <c>Werkr.Common.Configuration</c> project is loadable and that its
    /// containing assembly reference resolves correctly at runtime.
    /// </summary>
    [TestMethod]
    public void ServerProjectCompiles( ) {
        Assembly result = typeof( Werkr.Common.Configuration.WerkrConfiguration ).Assembly;
        Assert.IsNotNull( result );
    }
}
