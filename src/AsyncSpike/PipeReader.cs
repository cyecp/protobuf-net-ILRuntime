using System;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Threading.Tasks;

namespace ProtoBuf
{
    internal sealed class PipeReader : AsyncProtoReader
    {
        private IPipeReader _reader;
        private readonly bool _closePipe;
        private volatile bool _isReading;
        ReadableBuffer _available, _originalAsReceived;
        internal PipeReader(IPipeReader reader, bool closePipe, long bytes) : base(bytes)
        {
            _reader = reader;
            _closePipe = closePipe;
        }
        protected override Task SkipBytesAsync(int bytes)
        {
            _available = _available.Slice(bytes);
            Advance(bytes);
            return Task.CompletedTask;
        }
        private Task EnsureBufferedAsync(int bytes)
        {
            async Task Awaited(Task<bool> task)
            {
                if (!await task.ConfigureAwait(false)) ThrowEOF<int>();
                while (_available.Length < bytes)
                {
                    if (!await RequestMoreDataAsync().ConfigureAwait(false)) ThrowEOF<int>();
                }
            }
            while (_available.Length < bytes)
            {
                var task = RequestMoreDataAsync();
                if (!task.IsCompleted) return Awaited(task);
                if (!task.Result) return ThrowEOF<Task>();
            }
            return Task.CompletedTask;
        }
        protected override ValueTask<uint> ReadFixedUInt32Async()
        {
            async ValueTask<uint> Awaited(Task task)
            {
                await task.ConfigureAwait(false);
                return Process();
            }
            uint Process()
            {
                var val = _available.ReadLittleEndian<uint>();
                _available = _available.Slice(4);
                Advance(4);
                return val;
            }
            var t = EnsureBufferedAsync(4);
            if (!t.IsCompleted) return Awaited(t);

            t.Wait(); // check for exception
            return AsTask(Process());
        }
        protected override ValueTask<ulong> ReadFixedUInt64Async()
        {
            async ValueTask<ulong> Awaited(Task task)
            {
                await task.ConfigureAwait(false);
                return Process();
            }
            ulong Process()
            {
                var val = _available.ReadLittleEndian<ulong>();
                _available = _available.Slice(8);
                Advance(8);
                return val;
            }
            var t = EnsureBufferedAsync(8);
            if (!t.IsCompleted) return Awaited(t);

            t.Wait(); // check for exception
            return AsTask(Process());
        }
        protected override ValueTask<byte[]> ReadBytesAsync(int bytes)
        {
            async ValueTask<byte[]> Awaited(Task task, int len)
            {
                await task.ConfigureAwait(false);
                return Process(len);
            }
            byte[] Process(int len)
            {
                var arr = _available.Slice(0, len).ToArray();
                _available = _available.Slice(len);
                Advance(len);
                return arr;
            }
            var t = EnsureBufferedAsync(bytes);
            if (!t.IsCompleted) return Awaited(t, bytes);

            t.Wait(); // check for exception
            return AsTask(Process(bytes));
        }
        protected override ValueTask<string> ReadStringAsync(int bytes)
        {
            async ValueTask<string> Awaited(Task task, int len)
            {
                await task.ConfigureAwait(false);
                return Process(len);
            }
            string Process(int len)
            {
                var s = _available.Slice(0, bytes).GetUtf8String();
                Trace($"Read string: {s}");

                _available = _available.Slice(len);
                Advance(len);
                return s;
            }

            var t = EnsureBufferedAsync(bytes);
            if (!t.IsCompleted) return Awaited(t, bytes);

            t.Wait(); // check for exception
            return AsTask(Process(bytes));
        }
        private static (int value, int consumed) TryPeekVarintInt32(ref ReadableBuffer buffer)
        {
            Trace($"Parsing varint from {buffer.Length} bytes...");
            return (buffer.IsSingleSpan || buffer.First.Length >= MaxBytesForVarint)
                ? TryPeekVarintSingleSpan(buffer.First.Span)
                : TryPeekVarintMultiSpan(ref buffer);
        }
        internal static unsafe (int value, int consumed) TryPeekVarintSingleSpan(ReadOnlySpan<byte> span)
        {
            int len = span.Length;
            if (len == 0) return (0, 0);
            // thought: optimize the "I have tons of data" case? (remove the length checks)
            fixed (byte* spanPtr = &span.DangerousGetPinnableReference())
            {
                var ptr = spanPtr;

                // least significant group first
                int val = *ptr & 127;
                if ((*ptr & 128) == 0)
                {
                    Trace($"Parsed {val} from 1 byte");
                    return (val, 1);
                }
                if (len == 1) return (0, 0);

                val |= (*++ptr & 127) << 7;
                if ((*ptr & 128) == 0)
                {
                    Trace($"Parsed {val} from 2 bytes");
                    return (val, 2);
                }
                if (len == 2) return (0, 0);

                val |= (*++ptr & 127) << 14;
                if ((*ptr & 128) == 0)
                {
                    Trace($"Parsed {val} from 3 bytes");
                    return (val, 3);
                }
                if (len == 3) return (0, 0);

                val |= (*++ptr & 127) << 21;
                if ((*ptr & 128) == 0)
                {
                    Trace($"Parsed {val} from 4 bytes");
                    return (val, 4);
                }
                if (len == 4) return (0, 0);

                val |= (*++ptr & 127) << 28;
                if ((*ptr & 128) == 0)
                {
                    Trace($"Parsed {val} from 5 bytes");
                    return (val, 5);
                }
                if (len == 5) return (0, 0);

                // TODO switch to long and check up to 10 bytes (for -1)
                throw new NotImplementedException("need moar pointer math");
            }
        }
        private static unsafe (int value, int consumed) TryPeekVarintMultiSpan(ref ReadableBuffer buffer)
        {
            int value = 0;
            int consumed = 0, shift = 0;
            foreach (var segment in buffer)
            {
                var span = segment.Span;
                if (span.Length != 0)
                {
                    fixed (byte* ptr = &span.DangerousGetPinnableReference())
                    {
                        byte* head = ptr;
                        while (consumed++ < MaxBytesForVarint)
                        {
                            int val = *head++;
                            value |= (val & 127) << shift;
                            shift += 7;
                            if ((val & 128) == 0)
                            {
                                Trace($"Parsed {value} from {consumed} bytes (multiple spans)");
                                return (value, consumed);
                            }
                        }
                    }
                }
            }
            return (0, 0);
        }

        const int MaxBytesForVarint = 10;


        private Task<bool> RequestMoreDataAsync()
        {
            // ask the underlying pipe for more data
            ReadableBufferAwaitable BeginReadAsync()
            {
                _reader.Advance(_available.Start, _available.End);
                _isReading = true;
                _available = default(ReadableBuffer);
                return _reader.ReadAsync();
            }
            // accept data from the pipe, and see whether we should ask again
            bool EndReadCheckAskAgain(ReadResult read, int oldLen)
            {
                _originalAsReceived = _available = read.Buffer;
                _isReading = false;

                if (read.IsCancelled)
                {
                    throw new ObjectDisposedException(GetType().Name);
                }
                return read.Buffer.Length <= oldLen && !read.IsCompleted;
            }
            // convert from a synchronous request to an async continuation
            async Task<bool> Awaited(ReadableBufferAwaitable t, int oldLen)
            {
                ReadResult read = await t; // note: not a Task/ValueTask<T> - ConfigureAwait does not apply
                while (EndReadCheckAskAgain(read, oldLen))
                {
                    t = BeginReadAsync();
                    read = t.IsCompleted ? t.GetResult() : await t;
                }
                return PostProcess(oldLen);
            }
            // finalize state and see how well we did
            bool PostProcess(int oldLen)
            {
                if (End != long.MaxValue)
                {
                    ApplyDataConstraint();
                }
                return _available.Length > oldLen; // did we make progress?
            }

            {
                if (Position >= End)
                {
                    Trace("Refusing more data to sub-object");
                    return False; // nope!
                }

                int oldLen = _available.Length;
                ReadResult read;
                do
                {
                    var t = BeginReadAsync();
                    if (!t.IsCompleted) return Awaited(t, oldLen);
                    read = t.GetResult();
                }
                while (EndReadCheckAskAgain(read, oldLen));

                return PostProcess(oldLen) ? True : False;
            }
        }
        protected override void RemoveDataConstraint()
        {
            if (_available.End != _originalAsReceived.End)
            {
                int wasForConsoleMessage = _available.Length;
                // change back to the original right hand boundary
                _available = _originalAsReceived.Slice(_available.Start);
                Trace($"Data constraint removed; {_available.Length} bytes available (was {wasForConsoleMessage})");
            }
        }
        protected override void ApplyDataConstraint()
        {
            if (End != long.MaxValue && checked(Position + _available.Length) > End)
            {
                int allow = checked((int)(End - Position));
                int wasForConsoleMessage = _available.Length;
                _available = _available.Slice(0, allow);
                Trace($"Data constraint imposed; {_available.Length} bytes available (was {wasForConsoleMessage})");
            }
        }
        protected override ValueTask<int?> TryReadVarintInt32Async(bool consume)
        {
            async ValueTask<int?> Awaited(Task<bool> task, bool consumeData)
            {
                while(await task.ConfigureAwait(false))
                {
                    var read = TryPeekVarintInt32(ref _available);
                    if (read.consumed != 0)
                    {
                        if (consumeData)
                        {
                            Advance(read.consumed);
                            _available = _available.Slice(read.consumed);
                        }
                        return read.value;
                    }

                    task = RequestMoreDataAsync();
                }
                if (_available.Length == 0) return null;
                return ThrowEOF<int?>();
            }

            Task<bool> more;
            do
            {
                var read = TryPeekVarintInt32(ref _available);
                if (read.consumed != 0)
                {
                    if (consume)
                    {
                        Advance(read.consumed);
                        _available = _available.Slice(read.consumed);
                    }
                    return AsTask<int?>(read.value);
                }

                more = RequestMoreDataAsync();
                if (!more.IsCompleted) return Awaited(more, consume);
            }
            while (more.Result);

            if (_available.Length == 0) return AsTask<int?>(null);
            return ThrowEOF<ValueTask<int?>>();
        }

        public override void Dispose()
        {
            var reader = _reader;
            var available = _available;
            _reader = null;
            _available = default(ReadableBuffer);
            if (reader != null)
            {
                if (_isReading)
                {
                    reader.CancelPendingRead();
                }
                else
                {
                    reader.Advance(available.Start);
                }

                if (_closePipe)
                {
                    reader.Complete();
                }
            }
        }
    }
}
