using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Smoke tests proving the project context loads correctly and the
/// companion TrackDot assembly resolves alongside the test assembly.
/// These run without starting up WPF/SMTC so they work on any CI worker.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void Test_assembly_loads()
    {
        var asm = typeof(SmokeTests).Assembly;
        Assert.NotNull(asm);
        Assert.Equal("TrackDot.Tests", asm.GetName().Name);
    }

    [Fact]
    public void Companion_TrackDot_assembly_is_resolvable()
    {
        // The test project references TrackDot; the build should have
        // produced TrackDot.dll in the same output directory.
        var testDir = Path.GetDirectoryName(typeof(SmokeTests).Assembly.Location)!;
        var trackDotDll = Path.Combine(testDir, "TrackDot.dll");
        Assert.True(File.Exists(trackDotDll),
            $"Expected companion TrackDot.dll at '{trackDotDll}'");

        var asmName = AssemblyName.GetAssemblyName(trackDotDll);
        Assert.Equal("TrackDot", asmName.Name);
    }

    [Fact]
    public void Smoke_contract_holds()
    {
        // A trivial passing assertion so the test discovery is symmetric.
        Assert.Equal(2, 1 + 1);
    }
}
