using System.Text;
using System.Text.Json;
using Agent.Core.Logging;

namespace Agent.Logging;

public class JsonlSessionLogger : ISessionLogger
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public string FilePath { get; }

    public JsonlSessionLogger(string filePath)
    {
        FilePath = filePath;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer = new StreamWriter(filePath, append: true, Encoding.UTF8)
        {
            AutoFlush = false
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task LogAsync<T>(T entry)
    {
        var logEntry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            data = entry
        };

        string line;
        try
        {
            line = JsonSerializer.Serialize(logEntry, _jsonOptions);
        }
        catch (Exception ex)
        {
            line = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow,
                error = "Serialization failed",
                message = ex.Message,
                dataType = typeof(T).Name
            }, _jsonOptions);
        }

        await _lock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(line);
            await _writer.FlushAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await _writer.FlushAsync();
            await _writer.DisposeAsync();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
