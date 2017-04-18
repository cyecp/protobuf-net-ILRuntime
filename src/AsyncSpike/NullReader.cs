using System.Threading.Tasks;

namespace ProtoBuf
{
    internal sealed class NullReader : AsyncProtoReader
    {
        protected override ValueTask<int?> TryReadVarintInt32Async() => AsTask((int?)null);

        protected override ValueTask<string> ReadStringAsync(int bytes) => ThrowEOF<ValueTask<string>>();

        protected override ValueTask<byte[]> ReadBytesAsync(int bytes) => ThrowEOF<ValueTask<byte[]>>();
        protected override ValueTask<uint> ReadFixedUInt32Async() => ThrowEOF<ValueTask<uint>>();
        protected override ValueTask<ulong> ReadFixedUInt64Async() => ThrowEOF<ValueTask<ulong>>();
        protected override void ApplyDataConstraint() { }
        protected override void RemoveDataConstraint() { }
        protected override Task SkipBytesAsync(int bytes) => ThrowEOF<Task>();
    }
}
