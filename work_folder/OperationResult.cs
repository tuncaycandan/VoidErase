namespace VoidErase
{
    internal sealed class OperationResult
    {
        public int TotalFiles { get; init; }
        public long TotalBytes { get; init; }
        public int Successful { get; init; }
        public int Failed { get; init; }
        public int Verified { get; init; }
        public bool Cancelled { get; init; }
    }
}
