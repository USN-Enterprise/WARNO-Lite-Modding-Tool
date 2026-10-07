using System.Security.Cryptography;
using System.Text.Json;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

// Content fingerprints bound memory during draft and project revision checks.
// Originals and candidates for modified files remain available for recovery.
public static class SourceFingerprint
{
    public static byte[]? File(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        return SHA256.HashData(stream);
    }
    public static byte[]? Bytes(byte[]? data) => data is null ? null : SHA256.HashData(data);
    public static string Json<T>(T value)
    {
        using var stream = new HashStream(); JsonSerializer.Serialize(stream, value); return stream.Finish();
    }
    private sealed class HashStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public string Finish() => Convert.ToHexString(_hash.GetHashAndReset());
        public override void Write(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _hash.AppendData(buffer);
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
    }
}
