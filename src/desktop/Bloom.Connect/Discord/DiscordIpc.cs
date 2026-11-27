using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core.Connect;

namespace Bloom.Connect.Discord;

/// <summary>Discord local RPC opcodes (IPC transport).</summary>
public enum DiscordOpcode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4,
}

public sealed record DiscordFrame(DiscordOpcode Opcode, JsonObject Payload);

public sealed class DiscordIpcException(string message) : Exception(message);

/// <summary>
/// One connection to the Discord desktop client over its local named pipe
/// (\\.\pipe\discord-ipc-0..9). Frame = int32 opcode, int32 length (little endian), UTF-8 JSON.
/// Own small implementation instead of a library: only handshake and SET_ACTIVITY are needed.
/// </summary>
public sealed class DiscordIpcConnection : IAsyncDisposable
{
    private const int MaxFrame = 64 * 1024;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public DiscordIpcConnection(Stream stream) => _stream = stream;

    /// <summary>Tries the ten pipe names Discord (stable, PTB, Canary) may use.</summary>
    public static async Task<DiscordIpcConnection?> ConnectAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(250, ct);
                return new DiscordIpcConnection(pipe);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync();
            }
        }
        return null;
    }

    /// <summary>Sends the handshake and waits for READY. Returns the signed-in Discord user.</summary>
    public async Task<DiscordUser?> HandshakeAsync(string applicationId, CancellationToken ct)
    {
        await WriteAsync(DiscordOpcode.Handshake, new JsonObject { ["v"] = 1, ["client_id"] = applicationId }, ct);
        var frame = await ReadAsync(ct);
        if (frame.Opcode == DiscordOpcode.Close)
            throw new DiscordIpcException(frame.Payload["message"]?.GetValue<string>() ?? "Discord closed the connection");
        if (frame.Payload["evt"]?.GetValue<string>() != "READY")
            throw new DiscordIpcException("Unexpected handshake response");
        return ParseUser(frame.Payload["data"]?["user"]);
    }

    public Task SetActivityAsync(JsonObject? activity, int processId, CancellationToken ct) =>
        WriteAsync(DiscordOpcode.Frame, new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["args"] = new JsonObject { ["pid"] = processId, ["activity"] = activity },
            ["nonce"] = Guid.NewGuid().ToString(),
        }, ct);

    public async Task WriteAsync(DiscordOpcode opcode, JsonObject payload, CancellationToken ct)
    {
        var frame = Encode(opcode, payload);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DiscordFrame> ReadAsync(CancellationToken ct)
    {
        var header = new byte[8];
        await _stream.ReadExactlyAsync(header, ct);
        var opcode = (DiscordOpcode)BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length < 0 || length > MaxFrame) throw new DiscordIpcException($"Invalid frame length {length}");
        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, ct);
        var payload = length == 0 ? new JsonObject() : JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        return new DiscordFrame(opcode, payload);
    }

    internal static byte[] Encode(DiscordOpcode opcode, JsonObject payload)
    {
        var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, (int)opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        return frame;
    }

    internal static DiscordUser? ParseUser(JsonNode? user)
    {
        if (user is not JsonObject o || o["id"]?.GetValue<string>() is not { Length: > 0 } id) return null;
        return new DiscordUser(id, o["username"]?.GetValue<string>() ?? "", o["global_name"]?.GetValue<string?>(), o["avatar"]?.GetValue<string?>());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await WriteAsync(DiscordOpcode.Close, new JsonObject(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Pipe already gone.
        }
        await _stream.DisposeAsync();
        _writeLock.Dispose();
    }
}
