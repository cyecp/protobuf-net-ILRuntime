using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ProtoBuf
{
    public abstract class AsyncProtoReader : IDisposable
    {
        public static readonly AsyncProtoReader Null = new NullReader();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static ValueTask<T> AsTask<T>(T result) => new ValueTask<T>(result);
        protected static readonly Task<bool> True = Task.FromResult(true), False = Task.FromResult(false);
        
        protected static readonly TextEncoder Encoder = TextEncoder.Utf8;
        protected abstract void ApplyDataConstraint();
        protected abstract void RemoveDataConstraint();
        public virtual void Dispose() { }


        public ValueTask<float> ReadSingleAsync()
        {
            async ValueTask<float> Awaited32(ValueTask<uint> t) => ToSingle(await t);
            async ValueTask<float> Awaited64(ValueTask<ulong> t) => (float)ToDouble(await t);
            switch (WireType)
            {
                case WireType.Fixed32:
                    var u32 = ReadFixedUInt32Async();
                    return u32.IsCompleted ? AsTask(ToSingle(u32.Result)) : Awaited32(u32);
                case WireType.Fixed64:
                    var u64 = ReadFixedUInt64Async();
                    return u64.IsCompleted ? AsTask((float)ToDouble(u64.Result)) : Awaited64(u64);
                default:
                    throw new InvalidOperationException();
            }
        }
        public ValueTask<double> ReadDoubleAsync()
        {
            async ValueTask<double> Awaited32(ValueTask<uint> t) => (double)ToSingle(await t);
            async ValueTask<double> Awaited64(ValueTask<ulong> t) => ToDouble(await t);
            switch (WireType)
            {
                case WireType.Fixed32:
                    var u32 = ReadFixedUInt32Async();
                    return u32.IsCompleted ? AsTask((double)ToSingle(u32.Result)) : Awaited32(u32);
                case WireType.Fixed64:
                    var u64 = ReadFixedUInt64Async();
                    return u64.IsCompleted ? AsTask(ToDouble(u64.Result)) : Awaited64(u64);
                default:
                    throw new InvalidOperationException();
            }
        }

        private static unsafe float ToSingle(uint value) => *(float*)(&value);
        private static unsafe double ToDouble(ulong value) => *(double*)(&value);

        protected abstract ValueTask<uint> ReadFixedUInt32Async();
        protected abstract ValueTask<ulong> ReadFixedUInt64Async();

        public static AsyncProtoReader Create(Buffer<byte> buffer) => new BufferReader(buffer);
        public static AsyncProtoReader Create(IPipeReader pipe, bool closePipe = true, long bytes = long.MaxValue) => new PipeReader(pipe, closePipe, bytes);

        protected abstract Task SkipBytesAsync(int bytes);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ValueOrEOF(int? varint) => varint == null ? ThrowEOF<int>() : varint.GetValueOrDefault();
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static T ThrowEOF<T>() => throw new EndOfStreamException();
        public virtual Task SkipFieldAsync()
        {
            async Task AwaitedCheckVarint(ValueTask<int?> prefix)
                => ValueOrEOF(await prefix);

            async Task AwaitedSkipByPrefixLength(ValueTask<int?> task)
                => await SkipBytesAsync(ValueOrEOF(await task));

            ValueTask<int?> varint;
            switch (WireType)
            {
                case WireType.Varint:
                    varint = TryReadVarintInt32Async();
                    if (varint.IsCompleted)
                    {
                        ValueOrEOF(varint.Result); return Task.CompletedTask;
                    }
                    else
                    {
                        return AwaitedCheckVarint(varint);
                    }
                case WireType.Fixed32: return SkipBytesAsync(4);
                case WireType.Fixed64: return SkipBytesAsync(8);
                case WireType.String:
                    varint = TryReadVarintInt32Async();
                    return varint.IsCompleted
                        ? SkipBytesAsync(ValueOrEOF(varint.Result))
                        : AwaitedSkipByPrefixLength(varint);
                default:
                    throw new NotImplementedException();
            }
        }

        int _fieldHeader;
        public int FieldNumber => _fieldHeader >> 3;

        protected AsyncProtoReader(long length = long.MaxValue) { _end = length; }
        public WireType WireType => (WireType)(_fieldHeader & 7);
        public Task<bool> ReadNextFieldAsync()
        {
            async Task<bool> Awaited(ValueTask<int?> task)
            {
                var next = await task;
                _fieldHeader = next.GetValueOrDefault();
                return next.HasValue;
            }
            var nextTask = TryReadVarintInt32Async();
            if (nextTask.IsCompleted)
            {
                var next = nextTask.Result;
                _fieldHeader = next.GetValueOrDefault();
                return next.HasValue ? True : False;
            }
            else return Awaited(nextTask);

        }
        protected void Advance(int count) => _position += count;
        public long Position => _position;
        long _position, _end;
        protected long End => _end;
        public ValueTask<SubObjectToken> BeginSubObjectAsync()
        {
            async ValueTask<SubObjectToken> Awaited(ValueTask<int?> task)
            {
                int len = ValueOrEOF(await task);
                var result = new SubObjectToken(_end, _end = _position + len);
                ApplyDataConstraint();
                return result;
            }
            switch (WireType)
            {
                case WireType.String:
                    var task = TryReadVarintInt32Async();
                    if (task.IsCompleted)
                    {
                        int len = ValueOrEOF(task.Result);
                        var result = new SubObjectToken(_end, _end = _position + len);
                        ApplyDataConstraint();
                        return AsTask(result);
                    }
                    else return Awaited(task);
                default:
                    throw new InvalidOperationException();
            }
        }
        public void EndSubObject(ref SubObjectToken token)
        {
            if (token.End != _end) throw new InvalidOperationException("Sub-object ended in wrong order");
            if (token.End != _position) throw new InvalidOperationException("Sub-object not fully consumed");
            RemoveDataConstraint();
            _end = token.OldEnd;
            if (_end != long.MaxValue)
            {
                ApplyDataConstraint();
            }
            token = default(SubObjectToken);
        }
        public ValueTask<int> ReadInt32Async()
        {
            async ValueTask<int> AwaitedVarint(ValueTask<int?> task) => ValueOrEOF(await task);
            async ValueTask<int> AwaitedFixed32(ValueTask<uint> task) => checked((int)await task);
            async ValueTask<int> AwaitedFixed64(ValueTask<ulong> task) => checked((int)await task);
            switch (WireType)
            {
                case WireType.Varint:
                    var v32 = TryReadVarintInt32Async();
                    return v32.IsCompleted ? AsTask(ValueOrEOF(v32.Result)) : AwaitedVarint(v32);
                case WireType.Fixed32:
                    var f32 = ReadFixedUInt32Async();
                    return f32.IsCompleted ? AsTask(checked((int)f32.Result)) : AwaitedFixed32(f32);
                case WireType.Fixed64:
                    var f64 = ReadFixedUInt64Async();
                    return f64.IsCompleted ? AsTask(checked((int)f64.Result)) : AwaitedFixed64(f64);
                default:
                    throw new InvalidOperationException();
            }
        }
        [Conditional("VERBOSE")]
        protected static void Trace(string message) => SimpleUsage.Trace(message);


        static readonly byte[] EmptyBytes = new byte[0];
        public Task<bool> ReadBooleanAsync()
        {
            async Task<bool> Awaited(ValueTask<int> task) => (await task) != 0;
            var val = ReadInt32Async();
            return val.IsCompleted ? (val.Result != 0 ? True : False) : Awaited(val);
        }
        protected abstract ValueTask<byte[]> ReadBytesAsync(int bytes);
        protected abstract ValueTask<int?> TryReadVarintInt32Async();

        public ValueTask<string> ReadStringAsync()
        {
            async ValueTask<string> Awaited(ValueTask<int?> task)
            {
                int llen = ValueOrEOF(await task);
                Trace($"String length: {llen}");

                return llen == 0 ? "" : await ReadStringAsync(llen);
            }

            if (WireType != WireType.String) throw new InvalidOperationException();

            var tLen = TryReadVarintInt32Async();
            if (!tLen.IsCompleted) return Awaited(tLen);

            var len = ValueOrEOF(tLen.Result);
            Trace($"String length: {len}");
            return len == 0 ? AsTask("") : ReadStringAsync(len);
        }
        public ValueTask<byte[]> ReadBytesAsync()
        {
            async ValueTask<byte[]> Awaited(ValueTask<int?> task)
            {
                int llen = ValueOrEOF(await task);
                Trace($"BLOB length: {llen}");

                return llen == 0 ? EmptyBytes : await ReadBytesAsync(llen);
            }

            if (WireType != WireType.String) throw new InvalidOperationException();

            var tLen = TryReadVarintInt32Async();
            if (!tLen.IsCompleted) return Awaited(tLen);

            var len = ValueOrEOF(tLen.Result);
            Trace($"BLOB length: {len}");
            return len == 0 ? AsTask(EmptyBytes) : ReadBytesAsync(len);
        }
        protected abstract ValueTask<string> ReadStringAsync(int bytes);
    }
}
