using System.IO.Compression;
using System.Text;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers ServerService.DetectRequiredJavaFromJar, which answers "which Java major
/// does this jar need?" by reading the class file version of its Main-Class.
///
/// This exists because proxies were pinned to Java 21 on the reasoning that Velocity
/// required 21+. Velocity's bytecode target then moved: 3.4.0 targets Java 17, 3.5.1
/// targets 21, and 4.x targets 25. A 4.x proxy launched on the pinned Java 21 dies
/// before main() with UnsupportedClassVersionError (class file 69 vs 65), which is
/// what happened once Velocity 4.x reached the top of the profile list.
/// </summary>
public class JavaBinaryDetectionTests
{
    // class file major = Java major + 44: 61 = Java 17, 65 = 21, 69 = 25.
    [Theory]
    [InlineData(61, 17)]
    [InlineData(65, 21)]
    [InlineData(69, 25)]
    [InlineData(52, 8)]
    public void ReadsJavaMajorFromMainClassBytecode(int classFileVersion, int expectedJava)
    {
        var jar = WriteJar("com.example.Main", classFileVersion);
        try
        {
            Assert.Equal(expectedJava, ServerService.DetectRequiredJavaFromJar(jar));
        }
        finally
        {
            File.Delete(jar);
        }
    }

    [Fact]
    public void UnfoldsManifestContinuationLines()
    {
        // Manifest values wrap at 72 bytes onto a line starting with one space. Without
        // unfolding, Main-Class comes back truncated and the class entry is never found.
        var main = "com.velocitypowered.proxy.bootstrap.SomeVeryLongEntryPointClassName";
        var jar = WriteJar(main, 69, foldManifestAt: 40);
        try
        {
            Assert.Equal(25, ServerService.DetectRequiredJavaFromJar(jar));
        }
        finally
        {
            File.Delete(jar);
        }
    }

    [Fact]
    public void ReturnsNullWhenJarIsMissing()
    {
        Assert.Null(ServerService.DetectRequiredJavaFromJar("/nonexistent/velocity.jar"));
    }

    [Fact]
    public void ReturnsNullWhenFileIsNotAZip()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jar");
        File.WriteAllText(path, "not a jar");
        try
        {
            Assert.Null(ServerService.DetectRequiredJavaFromJar(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullWhenManifestHasNoMainClass()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jar");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open()))
            writer.Write("Manifest-Version: 1.0\r\n\r\n");
        try
        {
            Assert.Null(ServerService.DetectRequiredJavaFromJar(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullWhenMainClassIsNotAClassFile()
    {
        // No 0xCAFEBABE magic: refuse to guess rather than return a bogus major.
        var jar = WriteJar("com.example.Main", classFileVersion: 69, corruptMagic: true);
        try
        {
            Assert.Null(ServerService.DetectRequiredJavaFromJar(jar));
        }
        finally
        {
            File.Delete(jar);
        }
    }

    /// <summary>Builds a minimal jar with a Main-Class stub at the given class file version.</summary>
    private static string WriteJar(
        string mainClass,
        int classFileVersion,
        int? foldManifestAt = null,
        bool corruptMagic = false)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jar");

        var value = $"Main-Class: {mainClass}";
        var manifest = new StringBuilder("Manifest-Version: 1.0\r\n");
        if (foldManifestAt is { } width && value.Length > width)
        {
            manifest.Append(value[..width]).Append("\r\n");
            for (var i = width; i < value.Length; i += width - 1)
                manifest.Append(' ').Append(value.Substring(i, Math.Min(width - 1, value.Length - i))).Append("\r\n");
        }
        else
        {
            manifest.Append(value).Append("\r\n");
        }
        manifest.Append("\r\n");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        using (var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open()))
            writer.Write(manifest.ToString());

        // u4 magic, u2 minor_version, u2 major_version - all the detector reads.
        var header = new byte[]
        {
            corruptMagic ? (byte)0x00 : (byte)0xCA, 0xFE, 0xBA, 0xBE,
            0x00, 0x00,
            (byte)(classFileVersion >> 8), (byte)(classFileVersion & 0xFF)
        };
        using (var stream = archive.CreateEntry(mainClass.Replace('.', '/') + ".class").Open())
            stream.Write(header, 0, header.Length);

        return path;
    }
}
