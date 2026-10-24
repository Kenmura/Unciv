using Unciv.Logic.Map.MapGenerator;
using Xunit;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator;

public class PerlinTests
{
    private const double Precision = 1e-12;

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 2.0, 3.0, 0.0)]
    [InlineData(-1.0, -2.0, -3.0, 0.0)]
    [InlineData(0.5, 0.5, 0.5, -0.125)]
    [InlineData(1.25, -2.75, 3.5, 0.15489721298217773)]
    [InlineData(255.5, 256.5, 512.5, -0.25)]
    [InlineData(-0.1, -0.9, -15.3, 0.03514901799521253)]
    [InlineData(1000.123, -2000.456, 3000.789, -0.19258210767959388)]
    public void Noise_MatchesGoldenValues(double x, double y, double z, double expected)
    {
        double actual = Perlin.Noise(x, y, z);
        Assert.Equal(expected, actual, Precision);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 2.0, 3.0, 0.33584055050339473)]
    [InlineData(-1.0, -2.0, -3.0, -0.40486591947524403)]
    [InlineData(0.5, 0.5, 0.5, 0.15544830435136697)]
    [InlineData(1.25, -2.75, 3.5, 0.26751649465795857)]
    [InlineData(255.5, 256.5, 512.5, -0.45720256858889574)]
    [InlineData(-0.1, -0.9, -15.3, 0.12490532898243976)]
    [InlineData(1000.123, -2000.456, 3000.789, -0.15601039977634587)]
    public void Noise3d_DefaultParameters_MatchesGoldenValues(double x, double y, double z, double expected)
    {
        double actual = Perlin.Noise3d(x, y, z);
        Assert.Equal(expected, actual, Precision);
    }

    [Fact]
    public void Noise3d_CustomParameters_MatchesGoldenValues()
    {
        double result1 = Perlin.Noise3d(10.5, 20.25, -5.75, nOctaves: 5, persistence: 0.6, lacunarity: 2.5, scale: 15.0);
        Assert.Equal(-0.007970607666791198, result1, Precision);

        double result2 = Perlin.Noise3d(-100.0, 50.0, 25.0, nOctaves: 1, persistence: 0.5, lacunarity: 2.0, scale: 1.0);
        Assert.Equal(0.0, result2, Precision);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 2.0, 3.0, 0.33584055050339473)]
    [InlineData(-1.0, -2.0, -3.0, 0.40486591947524403)]
    [InlineData(0.5, 0.5, 0.5, 0.15544830435136697)]
    [InlineData(1.25, -2.75, 3.5, 0.26751649465795857)]
    [InlineData(255.5, 256.5, 512.5, 0.45720256858889574)]
    [InlineData(-0.1, -0.9, -15.3, 0.1301434575711627)]
    [InlineData(1000.123, -2000.456, 3000.789, 0.15601039977634587)]
    public void RidgedNoise3d_DefaultParameters_MatchesGoldenValues(double x, double y, double z, double expected)
    {
        double actual = Perlin.RidgedNoise3d(x, y, z);
        Assert.Equal(expected, actual, Precision);
    }

    [Fact]
    public void RidgedNoise3d_CustomParameters_MatchesGoldenValues()
    {
        double result1 = Perlin.RidgedNoise3d(10.5, 20.25, -5.75, nOctaves: 4, persistence: 0.7, lacunarity: 1.8, scale: 12.0);
        Assert.Equal(0.2171003925168333, result1, Precision);

        double result2 = Perlin.RidgedNoise3d(-100.0, 50.0, 25.0, nOctaves: 2, persistence: 0.4, lacunarity: 3.0, scale: 5.0);
        Assert.Equal(0.0, result2, Precision);
    }

    [Fact]
    public void RidgedNoise3d_AlwaysNonNegative()
    {
        for (double x = -10.0; x <= 10.0; x += 2.5)
        {
            for (double y = -10.0; y <= 10.0; y += 2.5)
            {
                double val = Perlin.RidgedNoise3d(x, y, 1.0);
                Assert.True(val >= 0.0, $"Expected non-negative at ({x}, {y}, 1.0), got {val}");
            }
        }
    }
}
