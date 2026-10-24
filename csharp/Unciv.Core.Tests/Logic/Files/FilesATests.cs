using System.Text;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models;

namespace Unciv.Core.Tests.Logic.Files;

// Ports tests/src/com/unciv/logic/MediaFinderTests.kt (against an in-memory file tree instead of the real assets)
// and the file/zip parts of MapSaver; FileConversions has no Kotlin test.
public class FilesATests
{
    private sealed class FakeFile(FakeFile? parent, string name, bool isDirectory, bool isInternal) : IUncivFileHandle
    {
        private readonly Dictionary<string, FakeFile> children = [];
        public byte[] Content = [];
        public bool Exists { get; private set; }
        public string Name => name;
        public string NameWithoutExtension => name.Contains('.') ? name[..name.LastIndexOf('.')] : name;
        public bool IsInternal => isInternal;
        public bool IsDirectory => Exists && isDirectory;
        public IUncivFileHandle Parent => parent!;
        public long Length => Exists ? Content.Length : 0;
        public long LastModified => 0;
        public void MakeDirs() => Exists = true;
        public bool Delete()
        {
            var was = Exists;
            Exists = false;
            return was;
        }

        public IUncivFileHandle Child(string childName)
        {
            if (!children.TryGetValue(childName, out var child))
                children[childName] = child = new FakeFile(this, childName, false, isInternal);
            return child;
        }

        public FakeFile Dir(string dirName)
        {
            var d = new FakeFile(this, dirName, true, isInternal) { Exists = true };
            children[dirName] = d;
            Exists = true;
            return d;
        }

        public FakeFile Add(string fileName, string text = "")
        {
            var f = (FakeFile)Child(fileName);
            f.Exists = true;
            f.Content = Encoding.UTF8.GetBytes(text);
            return f;
        }

        public IReadOnlyList<IUncivFileHandle> List() =>
            children.Values.Where(it => it.Exists).OrderBy(it => it.Name, StringComparer.Ordinal).ToList();

        public Stream Read() => new MemoryStream(Content);

        public Stream Write(bool append) => new CaptureStream(this);

        private sealed class CaptureStream(FakeFile file) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                file.Content = ToArray();
                file.Exists = true;
                base.Dispose(disposing);
            }
        }
    }

    private sealed class FakeEnv : IMediaEnvironment
    {
        public readonly FakeFile Internal = new(null, "", true, true) { };
        public readonly FakeFile Mods = new(null, "mods", true, false) { };
        public List<string> Visual = [];
        public bool GameInitialized = true;
        public bool IsAndroid { get; set; }
        public bool IsRunFromJar { get; set; }
        public IEnumerable<string> VisualMods => Visual;
        public bool IsGameInitialized => GameInitialized;
        public IUncivFileHandle GetInternal(string path) => Internal.Child(path);
        public IUncivFileHandle GetModFolder(string modName) => Mods.Child(modName);
    }

    private static FakeEnv EnvWithChimes()
    {
        var env = new FakeEnv();
        env.Internal.Dir("sounds").Add("chimes.ogg");
        return env;
    }

    [Fact]
    public void SoundFinderFindsChimes()
    {
        var sounds = new Sounds(EnvWithChimes());
        Assert.NotNull(((IMediaFinder)sounds).FindMedia(UncivSound.Chimes.FileName));
    }

    [Fact]
    public void SoundFinderDoesNotFindBullshit()
    {
        var sounds = new Sounds(EnvWithChimes());
        Assert.Null(((IMediaFinder)sounds).FindMedia("bullshit.nul"));
    }

    [Fact]
    public void SoundListingIncludesChimes()
    {
        var sounds = new Sounds(EnvWithChimes());
        Assert.Contains(((IMediaFinder)sounds).ListMediaFiles(), it => it.NameWithoutExtension == "chimes");
    }

    [Fact]
    public void VisualModsComeBeforeBuiltin()
    {
        var env = EnvWithChimes();
        env.Visual = ["Mod1", "Mod2"];
        env.Mods.Dir("Mod1");
        env.Mods.Dir("Mod2").Dir("sounds").Add("chimes.wav");
        var finder = (IMediaFinder)new Sounds(env);
        Assert.Equal(["sounds", "sounds"], finder.ListMediaFolders().Select(it => it.Name));
        Assert.Equal("chimes.wav", finder.FindMedia("chimes")!.Name);
    }

    [Fact]
    public void VisualModsIgnoredWhenGameNotInitialized()
    {
        var env = EnvWithChimes();
        env.Visual = ["Mod1"];
        env.GameInitialized = false;
        env.Mods.Dir("Mod1").Dir("sounds").Add("chimes.wav");
        Assert.Equal("chimes.ogg", ((IMediaFinder)new Sounds(env)).FindMedia("chimes")!.Name);
    }

    [Fact]
    public void JarListingUsesInternalNamesAndChecksExistence()
    {
        var env = EnvWithChimes();
        env.IsRunFromJar = true;
        var listed = ((IMediaFinder)new Sounds(env)).ListMediaFiles().Select(it => it.Name).ToList();
        Assert.Equal(["chimes.ogg"], listed);
    }

    [Fact]
    public void JarListingWithoutInternalNamesThrows()
    {
        var env = new FakeEnv { IsRunFromJar = true };
        env.Internal.Dir("voices");
        Assert.Throws<NotSupportedException>(() => ((IMediaFinder)new Voices(env)).ListMediaFiles().ToList());
    }

    [Fact]
    public void EmptyExtensionSetFindsNothing()
    {
        var env = EnvWithChimes();
        var finder = new NoExtensions(env);
        Assert.Null(((IMediaFinder)finder).FindMedia("chimes"));
    }

    private sealed class NoExtensions(IMediaEnvironment env) : IMediaFinder
    {
        public IMediaEnvironment Environment => env;
        public IReadOnlySet<string> SupportedMediaExtensions => new HashSet<string>();
        public string MediaSubFolderName => "sounds";
    }

    [Fact]
    public void LabeledSoundsListsBuiltinsInPrettifyOrderWithoutExclusions()
    {
        var env = new FakeEnv { GameInitialized = false };
        var labeled = new LabeledSounds(env).GetLabeledSounds().ToList();
        Assert.Equal(["", "notification1", "notification2", "coin", "construction", "paper", "policy", "setup", "swap"], labeled.Take(9).Select(it => it.Key));
        Assert.Equal("None", labeled[0].Value);
        Assert.Contains(labeled, it => it is { Key: "chimes", Value: "Chimes" });
        Assert.DoesNotContain(labeled, it => it.Key is "nuke" or "fire" or "slider");
        Assert.Contains(labeled, it => it is { Key: "whoosh" });
    }

    [Fact]
    public void LabeledSoundsRejectsNormalApi()
    {
        var finder = (IMediaFinder)new LabeledSounds(new FakeEnv());
        Assert.Throws<NotSupportedException>(() => finder.FindMedia("x"));
    }

    [Fact]
    public void LabeledSoundsLabelsModSoundFiles()
    {
        var env = new FakeEnv();
        env.Visual = ["MyMod"];
        env.Mods.Dir("MyMod").Dir("sounds").Add("roar.ogg");
        var labeled = new LabeledSounds(env).GetLabeledSounds().ToDictionary(it => it.Key, it => it.Value);
        Assert.Equal("MyMod: Roar {}", labeled["roar"]);
    }

    [Fact]
    public void ZipUnzipRoundtrip()
    {
        const string data = "{\"a\": \"Ünïcode ✓\", \"b\": [1,2,3]}";
        var zipped = FileConversions.Zip(data);
        Assert.DoesNotContain('{', zipped);
        Assert.StartsWith("H4sI", zipped); // base64 of the gzip magic 1f 8b 08
        Assert.Equal(data, FileConversions.Unzip(zipped));
    }

    [Fact]
    public void UnzipRejectsPlainTextAndWhitespace()
    {
        Assert.ThrowsAny<Exception>(() => FileConversions.Unzip("{\"a\": 1}"));
        Assert.ThrowsAny<Exception>(() => FileConversions.Unzip(FileConversions.Zip("x") + "\n"));
        Assert.ThrowsAny<Exception>(() => FileConversions.Unzip(Convert.ToBase64String("not gzip"u8.ToArray())));
    }

    [Fact]
    public void EncodeIsPlainBase64()
    {
        Assert.Equal("AQID", FileConversions.Encode([1, 2, 3]));
    }

    private sealed record Sample(string Name, int Count);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteJsonReadJsonRoundtrip(bool zip)
    {
        var file = new FakeEnv().Mods.Add("f.json");
        FileConversions.WriteJson(file, new Sample("x", 3), zip);
        var text = Encoding.UTF8.GetString(file.Content);
        Assert.Equal(zip, !text.StartsWith('{'));
        Assert.Equal(new Sample("x", 3), FileConversions.ReadJson<Sample>(file));
    }

    [Fact]
    public void ReadJsonFallsBackToPlainText()
    {
        var file = new FakeEnv().Mods.Add("f.json", "{\"name\": \"p\", \"count\": 2}");
        Assert.Equal(new Sample("p", 2), FileConversions.ReadJson<Sample>(file));
    }

    [Fact]
    public void MapFromSavedStringFallsBackToPlainAndTrimsZipped()
    {
        // Broken JSON fails at the JSON stage; what matters is that neither the plain text
        // nor the padded zipped text fails with the zip stage's FormatException/InvalidDataException.
        Assert.ThrowsAny<Exception>(() => MapSaver.MapFromSavedString("{broken"));
        Assert.IsNotType<FormatException>(Record.Exception(() => MapSaver.MapFromSavedString("  " + FileConversions.Zip("{broken") + "\n")));
        Assert.IsNotType<InvalidDataException>(Record.Exception(() => MapSaver.MapFromSavedString("{broken")));
    }

    [Fact]
    public void GetMapsListsMapsFolder()
    {
        var root = new FakeEnv().Mods;
        root.Dir("maps").Add("b");
        ((FakeFile)root.Child("maps")).Add("a");
        var maps = MapSaver.GetMaps(path => path == "maps" ? root.Child("maps") : throw new InvalidOperationException(path));
        Assert.Equal(["a", "b"], maps.Select(it => it.Name));
        Assert.Equal("maps", MapSaver.MapsFolder);
        Assert.True(MapSaver.SaveZipped);
    }

    [Fact]
    public void LoadMapPreviewReadsParametersFromFile()
    {
        var file = new FakeEnv().Mods.Add("m", "{\"mapParameters\": {\"name\": \"Foo\"}}");
        Assert.NotNull(MapSaver.LoadMapPreview(file));
        Assert.IsType<TileMap.Preview>(MapSaver.LoadMapPreview(file));
    }
}
