using Unciv.Harness;

namespace Unciv.Core.Tests.Harness;

public class BenchEnvFileTests
{
    [Fact]
    public void ParseHandlesCommentsExportQuotesAndOverrides()
    {
        var map = BenchEnvFile.Parse("# c\r\n\r\nUNCIV_BENCH_OUT=/sdcard/out\r\nexport UNCIV_BENCH_RES = \"1080x2400\"\n  UNCIV_BENCH_SIZE='Medium'\nnoequals\n=novalue\nUNCIV_BENCH_FRAMES=100\nUNCIV_BENCH_FRAMES=200\nQ=\"\n");
        Assert.Equal("/sdcard/out", map["UNCIV_BENCH_OUT"]);
        Assert.Equal("1080x2400", map["UNCIV_BENCH_RES"]);
        Assert.Equal("Medium", map["UNCIV_BENCH_SIZE"]);
        Assert.Equal("200", map["UNCIV_BENCH_FRAMES"]);
        Assert.Equal("\"", map["Q"]);
        Assert.Equal(5, map.Count);
    }

    [Fact]
    public void LookupPrefersEnvironment()
    {
        var file = BenchEnvFile.Parse("A=file\nB=file");
        Assert.Equal("env", BenchEnvFile.Lookup("A", file, _ => "env"));
        Assert.Equal("file", BenchEnvFile.Lookup("B", file, _ => null));
        Assert.Null(BenchEnvFile.Lookup("C", file, _ => null));
    }
}
