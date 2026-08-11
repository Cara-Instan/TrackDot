using System;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

public class PortableModeTests : IDisposable
{
    public PortableModeTests()
    {
        // Ensure default state
        PortableMode.IsPortable = false;
    }

    public void Dispose()
    {
        PortableMode.IsPortable = false;
    }

    [Fact]
    public void IsPortable_DefaultsToFalseInTests()
    {
        Assert.False(PortableMode.IsPortable);
    }

    [Fact]
    public void IsPortable_CanBeOverriddenForTests()
    {
        PortableMode.IsPortable = true;
        Assert.True(PortableMode.IsPortable);
    }
}
