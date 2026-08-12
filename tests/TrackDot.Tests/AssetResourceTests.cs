using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Build-time / load-time checks for the assets the popover and
/// tray icon depend on. These guard the contract that the XAML
/// references resolve at runtime — a missing asset surfaces as an
/// obscure <c>XamlParseException</c> at the first show, which is
/// too late to catch during a fresh checkout.
/// </summary>
/// <remarks>
/// <para>
/// The popover uses two embedded resources:
/// <list type="bullet">
///   <item><c>Assets/AppIcon.ico</c> — the tray icon, referenced by
///     <c>App.xaml</c> via <c>pack://application:,,,/Assets/AppIcon.ico</c>.</item>
///   <item><c>Assets/PlaceholderArt.png</c> — the album-art fallback
///     referenced by the popover when the source has no artwork.</item>
/// </list>
/// </para>
/// <para>
/// The csproj declares both files as <c>Resource</c> items with a
/// <c>Condition=&quot;Exists(...)&quot;</c> guard so a missing file
/// does not break the build. The tests below pin the
/// <strong>existence</strong> contract: if either file is missing
/// from the repo, the test fails with a clear message rather than
/// the production failure (a missing-image exception at first show).
/// </para>
/// <para>
/// The tests also verify the asset is actually <strong>embedded</strong>
/// in the built assembly via the WPF <c>.g.resources</c> stream. The
/// manifest check is non-trivial because WPF <c>&lt;Resource&gt;</c>
/// items do <em>not</em> become individual CLR manifest resources —
/// they are packed into a single binary <c>TrackDot.g.resources</c>
/// stream and looked up by the WPF resource manager at runtime
/// (against the original project-relative path, lowercased, with
/// forward slashes). A surviving file on disk that is not compiled
/// into the resource graph would still surface as a runtime failure;
/// the <c>.g.resources</c> check is the only signal that catches
/// both regressions. See the <c>Resources embedded in the built
/// assembly</c> section below for the rationale.
/// </para>
/// </remarks>
public sealed class AssetResourceTests
{
    // Repository root, computed from the test assembly's location
    // rather than hard-coded. The test project always lives under
    // <repo>/tests/TrackDot.Tests/ so the directory containing
    // TrackDot.csproj IS the repo root.
    private static string RepositoryRoot
    {
        get
        {
            var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            // Walk up from bin/x64/Debug/net8.0-windows10.0.19041.0/
            // to the repo root — the directory that contains
            // TrackDot.csproj. The csproj is the marker because
            // it is unique to the TrackDot repo (no other csproj
            // shares the filename).
            var dir = new DirectoryInfo(testDir);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrackDot.csproj")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string AssetsDir => Path.Combine(RepositoryRoot, "Assets");

    // -------------------------------------------------------------------
    // File existence on disk
    // -------------------------------------------------------------------

    [Fact]
    public void AppIcon_ico_exists_on_disk()
    {
        var path = Path.Combine(AssetsDir, "AppIcon.ico");
        Assert.True(File.Exists(path),
            $"Tray icon asset missing at '{path}'. The App.xaml TrayIcon resource references this path; a missing icon would surface as a runtime XamlParseException.");
    }

    [Fact]
    public void PlaceholderArt_png_exists_on_disk()
    {
        // The placeholder art is the popover's fallback when the
        // SMTC source has no embedded thumbnail. The csproj
        // declares it as a Resource with a Condition="Exists(...)"
        // guard so a missing file silently degrades the build — the
        // test promotes the file existence to a hard requirement.
        var path = Path.Combine(AssetsDir, "PlaceholderArt.png");
        Assert.True(File.Exists(path),
            $"Placeholder art asset missing at '{path}'. The popover references this as the fallback artwork; a missing file would surface as a runtime XamlParseException on the first show with no source artwork.");
    }

    // -------------------------------------------------------------------
    // File sanity
    // -------------------------------------------------------------------

    [Fact]
    public void AppIcon_ico_is_a_non_empty_file_with_ICO_magic()
    {
        // A 0-byte file or a file with the wrong magic would
        // fail to load as a tray icon. The ICO magic is the
        // 4-byte little-endian "0x00 0x00 0x01 0x00" header.
        var path = Path.Combine(AssetsDir, "AppIcon.ico");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0, "AppIcon.ico is empty.");
        Assert.True(
            bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0,
            $"AppIcon.ico does not begin with the ICO magic (00 00 01 00). First 4 bytes: {bytes[0]:x2} {bytes[1]:x2} {bytes[2]:x2} {bytes[3]:x2}.");
    }

    [Fact]
    public void PlaceholderArt_png_is_a_non_empty_file_with_PNG_magic()
    {
        // The PNG magic is the 8-byte signature
        // 89 50 4E 47 0D 0A 1A 0A. A missing or wrong-format
        // asset would fail to load as a WPF ImageSource.
        var path = Path.Combine(AssetsDir, "PlaceholderArt.png");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0, "PlaceholderArt.png is empty.");
        var pngMagic = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var actualMagic = bytes.Take(8).ToArray();
        Assert.True(actualMagic.SequenceEqual(pngMagic),
            $"PlaceholderArt.png does not begin with the PNG magic. First 8 bytes: {BitConverter.ToString(actualMagic)}.");
    }

    // -------------------------------------------------------------------
    // Resources embedded in the built assembly
    // -------------------------------------------------------------------
    //
    // WPF <Resource Include="..."> items are NOT embedded as individual
    // CLR manifest resources. The WPF build pipeline packs them into a
    // single binary .g.resources stream that is itself a CLR manifest
    // resource named "<AssemblyName>.g.resources". The pack URI
    // "pack://application:,,,/Assets/PlaceholderArt.png" resolves at
    // runtime by the WPF resource manager looking up the original
    // project-relative path (lowercase, forward slashes) as a key in
    // that .g.resources stream.
    //
    // That means these three tests cannot use GetManifestResourceStream
    // against "TrackDot.Assets.PlaceholderArt.png" — the assembly has
    // no such resource. They read the .g.resources stream directly via
    // a System.Resources.ResourceReader and assert the keys the WPF
    // runtime actually uses. The end-of-test contract is identical to
    // the original intent: if the build silently dropped the asset,
    // the asserted key would be missing and the test would fail.

    [Fact]
    public void TrackDot_assembly_contains_AppIcon_ico_resource()
    {
        // The csproj's <Resource Include="Assets\AppIcon.ico" /> is
        // packed into the .g.resources stream under the original
        // project-relative path, lowercased, with forward slashes.
        var asm = LoadTrackDotAssembly();

        var entries = EnumerateWpfResourceEntries(asm);
        Assert.Contains("assets/appicon.ico", entries.Keys);
        Assert.True(entries["assets/appicon.ico"] > 0,
            "assets/appicon.ico in the .g.resources stream is empty.");
    }

    [Fact]
    public void TrackDot_assembly_contains_PlaceholderArt_png_resource()
    {
        // Same check for the placeholder art. WPF uses the same
        // lowercased / forward-slash project-relative path as the
        // lookup key, identical to what the XAML pack URI resolves.
        var asm = LoadTrackDotAssembly();

        var entries = EnumerateWpfResourceEntries(asm);
        Assert.Contains("assets/placeholderart.png", entries.Keys);
        Assert.True(entries["assets/placeholderart.png"] > 0,
            "assets/placeholderart.png in the .g.resources stream is empty.");
    }

    [Fact]
    public void PlaceholderArt_png_resource_starts_with_PNG_magic()
    {
        // Cross-check: the bytes embedded in the .g.resources
        // stream are actually a PNG. A csproj that picks up the
        // file but mangles the encoding (e.g. via CopyToOutputDirectory)
        // would survive the key-existence check but fail to load as
        // an ImageSource.
        var asm = LoadTrackDotAssembly();

        var bytes = ReadWpfResourceBytes(asm, "assets/placeholderart.png");
        Assert.NotNull(bytes);
        var pngMagic = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.True(bytes!.Take(8).SequenceEqual(pngMagic),
            "Embedded PlaceholderArt.png starts with bytes that are not the PNG magic. The csproj may have picked up a non-PNG file.");
    }

    // -------------------------------------------------------------------
    // CSProj item group coverage
    // -------------------------------------------------------------------

    [Fact]
    public void TrackDot_csproj_declares_AppIcon_ico_as_Resource()
    {
        // The csproj must declare the icon as a <Resource> with
        // an Exists() condition so the build does not break
        // when the file is briefly moved during a refactor.
        // This is a structural check, not a file-exists check.
        var csproj = File.ReadAllText(Path.Combine(RepositoryRoot, "TrackDot.csproj"));

        Assert.Contains("Assets\\AppIcon.ico", csproj);
        Assert.Contains("Resource", csproj);
        // The exact line is:
        //   <Resource Include="Assets\AppIcon.ico" Condition="Exists('Assets\AppIcon.ico')" />
        // We assert the two pieces individually to avoid
        // matching the wrong line.
        Assert.Contains("Condition=\"Exists('Assets\\AppIcon.ico')\"", csproj);
    }

    [Fact]
    public void TrackDot_csproj_declares_PlaceholderArt_png_as_Resource()
    {
        // Same shape for the placeholder art. The csproj line
        // is part of the asset contract: any future change MUST
        // keep the <Resource> Include + Condition guard, or this
        // test will fail and the author will have to defend
        // why the build silently degraded.
        var csproj = File.ReadAllText(Path.Combine(RepositoryRoot, "TrackDot.csproj"));

        Assert.Contains("Assets\\PlaceholderArt.png", csproj);
        Assert.Contains("Resource", csproj);
        Assert.Contains("Condition=\"Exists('Assets\\PlaceholderArt.png')\"", csproj);
    }

    // -------------------------------------------------------------------
    // Pack URI resolution
    // -------------------------------------------------------------------

    [Fact]
    public void App_xaml_uses_pack_uri_for_AppIcon_ico()
    {
        // The handoff's gotcha: the TaskbarIcon.IconSource is
        // an ImageSource, and the string-to-ImageSource converter
        // accepts filesystem paths AND pack:// URIs. A bare
        // "Assets/AppIcon.ico" works in design-time but breaks
        // at runtime (the working directory is bin/.../, not the
        // project root).
        //
        // The pack URI must be fully-qualified with an explicit
        // assembly name when XAML is loaded from a non-entry
        // assembly (e.g. the TrackDot.Tests host): the relative
        // "pack://application:,,,/Assets/AppIcon.ico" form resolves
        // relative to the entry assembly and fails when the test
        // host is TrackDot.Tests.
        var appXaml = File.ReadAllText(Path.Combine(RepositoryRoot, "App.xaml"));

        Assert.Contains("pack://application:,,,/TrackDot;component/Assets/AppIcon.ico", appXaml);
    }

    [Fact]
    public void App_xaml_contains_valid_SystemColors_static_keys()
    {
        // Guard against referencing non-existent SystemColors properties
        // (such as SystemColors.ControlHighlightBrushKey) which compile but throw
        // XamlParseException at runtime when App.xaml resources are loaded.
        var appXaml = File.ReadAllText(Path.Combine(RepositoryRoot, "App.xaml"));

        Assert.DoesNotContain("SystemColors.ControlHighlightBrushKey", appXaml);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Loads the TrackDot assembly directly from the test
    /// output directory. <c>Assembly.GetReferencedAssemblies</c>
    /// returns the <em>identity</em> of the referenced assembly,
    /// not the resolved one; using <c>Assembly.LoadFrom</c> on
    /// the sibling TrackDot.dll is the most reliable way to
    /// open the manifest for inspection.
    /// </summary>
    private static Assembly LoadTrackDotAssembly()
    {
        var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var trackDotDll = Path.Combine(testDir, "TrackDot.dll");
        if (File.Exists(trackDotDll))
        {
            return Assembly.LoadFrom(trackDotDll);
        }
        // Fallback: walk the referenced assemblies. The
        // test project always has a ProjectReference to the
        // main project, so the reference is always there.
        var refName = Assembly.GetExecutingAssembly()
            .GetReferencedAssemblies()
            .FirstOrDefault(a => a.Name == "TrackDot")
            ?? throw new InvalidOperationException("TrackDot assembly not referenced by the test project.");
        return Assembly.Load(refName);
    }

    /// <summary>
    /// Enumerates every entry in the WPF <c>.g.resources</c> stream
    /// that ships with the assembly. Returns a case-sensitive
    /// dictionary mapping the WPF resource key (the
    /// project-relative path, lowercased, forward-slashes) to the
    /// embedded stream length in bytes. Throws if the
    /// .g.resources stream is missing — a missing build artifact
    /// would surface as a runtime XamlParseException, so the
    /// helper fails fast and loud instead.
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, long> EnumerateWpfResourceEntries(Assembly asm)
    {
        // The single CLR manifest resource produced by the WPF
        // build pipeline. <AssemblyName>.g.resources is the
        // canonical name; if it is absent the build never ran
        // the MarkupCompilePass2 / ResourcesGenerator target.
        var gResourceName = asm.GetName().Name + ".g.resources";
        using var stream = asm.GetManifestResourceStream(gResourceName)
            ?? throw new InvalidOperationException(
                $"TrackDot assembly does not contain a '{gResourceName}' manifest resource. The WPF build pipeline did not produce the .g.resources stream — the file is most likely built without UseWPF=true or the MarkupCompilePass2 target did not run.");

        using var reader = new ResourceReader(stream);
        var result = new System.Collections.Generic.Dictionary<string, long>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in reader)
        {
            var key = entry.Key?.ToString()
                ?? throw new InvalidOperationException("Encountered a null key in the .g.resources stream.");
            if (entry.Value is Stream embedded)
            {
                result[key] = embedded.Length;
            }
            else
            {
                // Non-stream entries (BAML compiled XAML, etc.)
                // record a sentinel length so the test still
                // observes the key is present without forcing
                // a Stream cast.
                result[key] = -1;
            }
        }
        return result;
    }

    /// <summary>
    /// Reads the bytes of a single WPF resource entry out of the
    /// .g.resources stream. Returns <c>null</c> if the key is not
    /// present. The helper copies the embedded stream into a
    /// freshly-allocated <c>byte[]</c> — the ResourceReader
    /// returns streams whose underlying buffer is reused, so the
    /// caller must not retain the stream reference past the
    /// next iteration of the outer ResourceReader.
    /// </summary>
    private static byte[]? ReadWpfResourceBytes(Assembly asm, string wpfResourceKey)
    {
        var gResourceName = asm.GetName().Name + ".g.resources";
        using var stream = asm.GetManifestResourceStream(gResourceName)
            ?? throw new InvalidOperationException(
                $"TrackDot assembly does not contain a '{gResourceName}' manifest resource.");
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            if (!string.Equals(entry.Key?.ToString(), wpfResourceKey, StringComparison.Ordinal))
            {
                continue;
            }
            if (entry.Value is not Stream embedded)
            {
                throw new InvalidOperationException(
                    $"WPF resource '{wpfResourceKey}' is not a stream — it is a {entry.Value?.GetType().FullName ?? "null"}. A BAML entry may have shadowed the expected asset; inspect the .g.resources stream.");
            }
            using var ms = new MemoryStream();
            embedded.CopyTo(ms);
            return ms.ToArray();
        }
        return null;
    }
}
