using System.IO;
using Outspoken.Core.Diagnostics;

namespace Outspoken.Core.Tests;

public class ErrorLogTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"outspoken-errlog-{Guid.NewGuid():N}", "errors.log");

    [Fact]
    public void Write_AppendsTimestampedLine()
    {
        var path = TempPath();
        try
        {
            var log = new ErrorLog(path);
            log.Write("mic start failed: no capture device");
            log.Write("cleanup failed: 401 unauthorized");

            var contents = File.ReadAllText(path);
            Assert.Contains("mic start failed: no capture device", contents);
            Assert.Contains("cleanup failed: 401 unauthorized", contents);
            Assert.Equal(2, File.ReadAllLines(path).Length);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Write_RotatesToOnePreviousFile_WhenOverSizeCap()
    {
        var path = TempPath();
        try
        {
            var log = new ErrorLog(path, maxBytes: 200); // tiny cap to force rotation

            for (var i = 0; i < 20; i++)
                log.Write($"error entry number {i} with enough text to grow the file past the cap");

            Assert.True(File.Exists(path));           // current log exists
            Assert.True(File.Exists(path + ".1"));    // one rotated file kept
            // Current log stays small (reset on rotation), not the full 20-line history.
            Assert.True(new FileInfo(path).Length < 1000);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Write_NeverThrows_OnBadPath()
    {
        // An unwritable path must not crash the app - logging is best-effort.
        var log = new ErrorLog("Z:\\nonexistent-drive\\outspoken\\errors.log");
        var ex = Record.Exception(() => log.Write("should be swallowed"));
        Assert.Null(ex);
    }

    [Fact]
    public void DefaultPath_IsUnderLocalAppDataLogs()
    {
        var log = new ErrorLog();
        Assert.Contains(Path.Combine("Outspoken", "logs"), log.Path);
    }
}
