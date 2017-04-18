using System.Binary;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ProtoBuf
{
    internal sealed class BufferReader : AsyncProtoReader
    {
        private Buffer<byte> _active, _original;
        internal BufferReader(Buffer<byte> buffer) : base(buffer.Length)
        {
            _active = _original = buffer;
        }
        protected override Task SkipBytesAsync(int bytes)
        {
            _active = _active.Slice(bytes);
            Advance(bytes);
            return Task.CompletedTask;
        }
        protected override ValueTask<uint> ReadFixedUInt32Async()
        {
            var val = _active.Span.ReadLittleEndian<uint>();
            _active = _active.Slice(4);
            Advance(4);
            return AsTask(val);
        }
        protected override ValueTask<ulong> ReadFixedUInt64Async()
        {
            var val = _active.Span.ReadLittleEndian<ulong>();
            _active = _active.Slice(8);
            Advance(8);
            return AsTask(val);
        }
        protected override ValueTask<byte[]> ReadBytesAsync(int bytes)
        {
            var arr = _active.Slice(0, bytes).ToArray();
            _active = _active.Slice(bytes);
            return AsTask(arr);
        }
        protected override ValueTask<string> ReadStringAsync(int bytes)
        {
            bool result = Encoder.TryDecode(_active.Slice(0, bytes).Span, out string text, out int consumed);
            Debug.Assert(result, "TryDecode failed");
            Debug.Assert(consumed == bytes, "TryDecode used wrong count");
            _active = _active.Slice(bytes);
            Advance(bytes);
            return AsTask(text);
        }
        protected override ValueTask<int?> TryReadVarintInt32Async()
        {
            var result = PipeReader.TryPeekVarintSingleSpan(_active.Span);
            if (result.consumed == 0)
            {
                return new ValueTask<int?>((int?)null);
            }
            _active = _active.Slice(result.consumed);
            Advance(result.consumed);
            return AsTask<int?>(result.value);
        }
        protected override void ApplyDataConstraint()
        {
            if (End != long.MaxValue)
            {
                _active = _original.Slice((int)Position, (int)(End - Position));
            }
        }
        protected override void RemoveDataConstraint()
        {
            _active = _original.Slice((int)Position);
        }
    }
}
