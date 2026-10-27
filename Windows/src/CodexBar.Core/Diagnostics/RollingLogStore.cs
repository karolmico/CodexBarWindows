using System.Globalization;
using System.Text;

namespace CodexBar.Core.Diagnostics;

/// <summary>
/// Append-only local log with a byte cap. When the active file would exceed the cap it is rotated to
/// <c>.1</c>, older files shift up, and anything past <c>maxFiles</c> is deleted. Every line is redacted
/// before it is written, and IO failures are swallowed: logging must never take the app down.
/// </summary>
public sealed class RollingLogStore
{
    public const long DefaultMaxBytes = 512 * 1024;
    public const int DefaultMaxFiles = 3;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object gate = new();
    private readonly string directory;
    private readonly string fileName;
    private readonly long maxBytes;
    private readonly int maxFiles;
    private readonly Func<DateTimeOffset> clock;

    public RollingLogStore(
        string directory,
        string fileName = "codexbar.log",
        long maxBytes = DefaultMaxBytes,
        int maxFiles = DefaultMaxFiles,
        Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A log directory is required.", nameof(directory));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A log file name is required.", nameof(fileName));
        }

        this.directory = directory;
        this.fileName = fileName;
        this.maxBytes = Math.Max(256, maxBytes);
        this.maxFiles = Math.Max(1, maxFiles);
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string CurrentPath => Path.Combine(this.directory, this.fileName);

    public void Info(string message) => this.Write("INFO", message, error: null);

    public void Warn(string message, Exception? error = null) => this.Write("WARN", message, error);

    public void Error(string message, Exception? error = null) => this.Write("ERROR", message, error);

    public void Write(string level, string message, Exception? error = null)
    {
        var timestamp = this.clock().UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
        var detail = error is null ? string.Empty : $" | {error.GetType().Name}: {error.Message}";
        var line = LogRedactor.Redact($"{timestamp} [{level}] {message}{detail}")
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        lock (this.gate)
        {
            try
            {
                Directory.CreateDirectory(this.directory);
                var path = this.CurrentPath;
                var bytes = Utf8.GetByteCount(line) + Environment.NewLine.Length;
                if (File.Exists(path) && new FileInfo(path).Length + bytes > this.maxBytes)
                {
                    this.Rotate(path);
                }

                File.AppendAllText(path, line + Environment.NewLine, Utf8);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                // Diagnostics are best-effort; a read-only profile must not break fetching.
            }
        }
    }

    private void Rotate(string activePath)
    {
        var oldest = $"{activePath}.{this.maxFiles}";
        TryDelete(oldest);

        for (var index = this.maxFiles - 1; index >= 1; index--)
        {
            var source = $"{activePath}.{index}";
            var destination = $"{activePath}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        if (File.Exists(activePath))
        {
            File.Move(activePath, $"{activePath}.1", overwrite: true);
        }

        // A smaller cap or a manual edit can leave files beyond the configured depth.
        for (var index = this.maxFiles + 1; index <= this.maxFiles + 5; index++)
        {
            TryDelete($"{activePath}.{index}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
