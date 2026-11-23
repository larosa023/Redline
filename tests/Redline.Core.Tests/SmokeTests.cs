using Xunit;

namespace Redline.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.Equal("Redline.Core", typeof(Redline.Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
