using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

namespace VoidErase;

internal static class PerformanceBenchmark
{
    public static string Run(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("Benchmark directory was not found.");

        string[] files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".destroying", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        StringBuilder report = new StringBuilder();
        report.AppendLine("build,case,file,bytes,seconds,mbps,private_bytes_before,private_bytes_after,private_bytes_delta,verified");

        foreach (string file in files)
        {
            FileInfo info = new FileInfo(file);
            string temp = Path.Combine(info.DirectoryName!, ".benchmark-" + Guid.NewGuid().ToString("N") + ".destroying");
            byte[] key = CryptoCompat.RandomBytes(32);
            byte[] headerNonce = CryptoCompat.RandomBytes(12);
            long privateBytesBefore = Process.GetCurrentProcess().PrivateMemorySize64;
            Stopwatch timer = Stopwatch.StartNew();
            bool verified = false;
            try
            {
                Program.EncryptChunks(file, temp, key, headerNonce, NoOpProgressReporter.Instance);
                Program.ValidateContainer(temp, key, headerNonce, NoOpProgressReporter.Instance);
                verified = true;
            }
            finally
            {
                timer.Stop();
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                CryptoCompat.ZeroMemory(key);
                CryptoCompat.ZeroMemory(headerNonce);
            }

            long privateBytesAfter = Process.GetCurrentProcess().PrivateMemorySize64;
            double seconds = Math.Max(timer.Elapsed.TotalSeconds, 0.000001);
            double mbps = (info.Length / 1024d / 1024d) / seconds;
            report.AppendLine(string.Join(",",
                "v1.4",
                "crypto-roundtrip",
                Csv(Path.GetFileName(file)),
                info.Length,
                seconds.ToString("0.000", CultureInfo.InvariantCulture),
                mbps.ToString("0.00", CultureInfo.InvariantCulture),
                privateBytesBefore,
                privateBytesAfter,
                privateBytesAfter - privateBytesBefore,
                verified ? "true" : "false"));
        }

        string output = Path.Combine(directory, "voiderase-benchmark-results.csv");
        File.WriteAllText(output, report.ToString(), new UTF8Encoding(false));
        return output;
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private sealed class NoOpProgressReporter : IProgressReporter
    {
        public static readonly NoOpProgressReporter Instance = new NoOpProgressReporter();
        public void ReportProgress(long processed, long total, TimeSpan elapsed) { }
        public void ReportValidation(long current, long total, TimeSpan elapsed) { }
        public void ReportFinalizing() { }
        public void ThrowIfCancellationRequested() { }
    }
}
