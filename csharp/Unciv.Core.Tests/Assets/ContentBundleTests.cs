using System.IO.Compression;
using Unciv.Assets.Packaging;

namespace Unciv.Tests.Assets;

// T276: content delivery without an executable folder (Android): a zip riding in the pck is extracted once to the user folder, versioned.
public sealed class ContentBundleTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("unciv-bundle-").FullName;

    public void Dispose() => Directory.Delete(temp, true);

    private static Func<Stream> Zip(params (string Name, string Text)[] files) => () =>
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        }
        ms.Position = 0;
        return ms;
    };

    private static readonly (string, string)[] Tree =
    [
        ("data/jsons/Civ V - Vanilla/Units.json", "[]"),
        ("data/jsons/translations/German.properties", "a = b"),
        ("Packs/unciv/manifest.json", "{}"),
        ("Assets/fonts/OFL.txt", "licence"),
    ];

    [Fact]
    public void DesktopBuildWithDataBesideTheExecutableIsUnchanged()
    {
        var exe = Path.Combine(temp, "app");
        Directory.CreateDirectory(ContentLocator.BundledJsons(exe));
        var staging = Path.Combine(temp, "user", "content");
        var root = ContentBundle.Resolve(exe, Directory.Exists, () => throw new InvalidOperationException("must not look for a bundle"), () => throw new InvalidOperationException(), staging);
        Assert.Equal(exe, root);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void NoExecutableFolderExtractsTheBundleAndTheLocatorsFindIt()
    {
        var exe = Path.Combine(temp, "apk-process"); // nothing beside it: the Android case
        var staging = Path.Combine(temp, "user", "content");
        var root = ContentBundle.Resolve(exe, Directory.Exists, () => "v1", Zip(Tree), staging);
        Assert.Equal(staging, root);
        Assert.Equal(ContentLocator.BundledJsons(staging), ContentLocator.FindJsons(root!, "/nowhere", Directory.Exists));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(staging, "data", "jsons", "Civ V - Vanilla", "Units.json")));
        Assert.True(File.Exists(Path.Combine(staging, "Packs", "unciv", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(staging, "Assets", "fonts", "OFL.txt")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(staging, ContentBundle.StampFile)));
        Assert.False(Directory.Exists(staging + ".tmp"));
    }

    [Fact]
    public void NoBundleAndNoDataFolderIsNull()
    {
        Assert.Null(ContentBundle.Resolve(Path.Combine(temp, "x"), Directory.Exists, () => null, () => throw new InvalidOperationException(), Path.Combine(temp, "s")));
        Assert.Null(ContentBundle.Resolve(Path.Combine(temp, "x"), Directory.Exists, () => "  ", () => throw new InvalidOperationException(), Path.Combine(temp, "s")));
    }

    [Fact]
    public void SecondStartDoesNotExtractAgain()
    {
        var staging = Path.Combine(temp, "content");
        Assert.True(ContentBundle.EnsureExtracted("v1", Zip(Tree), staging));
        Assert.False(ContentBundle.EnsureExtracted("v1", () => throw new InvalidOperationException("must not open"), staging));
    }

    [Fact]
    public void NewVersionReplacesTheOldFilesCompletely()
    {
        var staging = Path.Combine(temp, "content");
        ContentBundle.EnsureExtracted("v1", Zip(Tree), staging);
        Assert.True(ContentBundle.EnsureExtracted("v2", Zip(("data/jsons/new.json", "{}")), staging));
        Assert.True(File.Exists(Path.Combine(staging, "data", "jsons", "new.json")));
        Assert.False(File.Exists(Path.Combine(staging, "Packs", "unciv", "manifest.json")));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(staging, ContentBundle.StampFile)));
    }

    [Fact]
    public void InterruptedExtractionIsRedone()
    {
        var staging = Path.Combine(temp, "content");
        Directory.CreateDirectory(Path.Combine(staging, "data", "jsons")); // no stamp: a crash left a partial folder
        Directory.CreateDirectory(staging + ".tmp");
        File.WriteAllText(Path.Combine(staging + ".tmp", "stale"), "x");
        Assert.True(ContentBundle.EnsureExtracted("v1", Zip(Tree), staging));
        Assert.False(File.Exists(Path.Combine(staging, "stale")));
        Assert.True(File.Exists(Path.Combine(staging, "Packs", "unciv", "manifest.json")));
    }

    [Fact]
    public void StampWithoutJsonsIsNotTrusted()
    {
        var staging = Path.Combine(temp, "content");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, ContentBundle.StampFile), "v1");
        Assert.True(ContentBundle.EnsureExtracted("v1", Zip(Tree), staging));
    }

    [Fact]
    public void EntryEscapingTheFolderIsRefusedAndLeavesNothingBehind()
    {
        var staging = Path.Combine(temp, "content");
        Assert.Throws<InvalidDataException>(() => ContentBundle.EnsureExtracted("v1", Zip(("data/jsons/a.json", "{}"), ("../evil.txt", "x")), staging));
        Assert.False(File.Exists(Path.Combine(temp, "evil.txt")));
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(staging + ".tmp"));
    }

    [Fact]
    public void FailedExtractionKeepsThePreviousVersionUsable()
    {
        var staging = Path.Combine(temp, "content");
        ContentBundle.EnsureExtracted("v1", Zip(Tree), staging);
        Assert.Throws<InvalidDataException>(() => ContentBundle.EnsureExtracted("v2", Zip(("../evil.txt", "x")), staging));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(staging, ContentBundle.StampFile)));
    }
}
