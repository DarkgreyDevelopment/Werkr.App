using System.Reflection;

namespace Werkr.Tests.Server;

[TestClass]
public class Placeholder {
    [TestMethod]
    public void ServerProjectCompiles( ) {
        Assembly result = typeof( Werkr.Common.Configuration.WerkrConfiguration ).Assembly;
        Assert.IsNotNull( result );
    }
}
