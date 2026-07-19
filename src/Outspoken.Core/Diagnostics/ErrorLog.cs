using System.IO;

namespace Outspoken.Core.Diagnostics;

/// <summary>
/// Errors-only local log for debugging (spec §5). Appends timestamped error lines to a file under
/// %LOCALAPPDATA%\Outspoken\logs, size-rotated to one previous file. It NEVER receives dictation
/// content - the privacy invariant (ADR-001) means callers pass only operator-facing error strings
/// (stage failures, init problems), never the transcript. Logging never throws: a file error is
/// swallowed rather than allowed to break the app.
/// </summary>
public sealed class ErrorLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public ErrorLog(string? path = null, long maxBytes = 512 * 1024)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Outspoken", "logs", "outspoken-errors.log");
        _maxBytes = maxBytes;
    }

    public string Path => _path;

    /// <summary>Appends a timestamped error line. Message must be operator-facing - never dictation content.</summary>
    public void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                RotateIfNeeded();
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    /// <summary>When the log passes the size cap, keep one previous file (.1) and start fresh.</summary>
    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes)
            return;

        var previous = _path + ".1";
        try
        {
            if (File.Exists(previous))
                File.Delete(previous);
            File.Move(_path, previous);
        }
        catch
        {
            // If rotation fails, keep appending to the current file rather than losing the entry.
        }
    }
}
