namespace VoidErase
{
    internal sealed class OperationResult
    {
        public int TotalFiles { get; set; }
        public long TotalBytes { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Verified { get; set; }
        public bool Cancelled { get; set; }
    }
}
