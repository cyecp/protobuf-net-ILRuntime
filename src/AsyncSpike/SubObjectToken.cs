namespace ProtoBuf
{
    public struct SubObjectToken
    {
        internal SubObjectToken(long oldEnd, long end)
        {
            OldEnd = oldEnd;
            End = end;
        }
        internal readonly long OldEnd, End;
    }
}
