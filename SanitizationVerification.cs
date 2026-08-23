using System;
using System.IO;

internal enum VerificationStatus
{
    Verified,
    Failed,
    NotApplicable
}

internal sealed class VerificationResult
{
    public VerificationStatus Status { get; set; }

    public bool PathAbsent { get; set; }

    public string Message { get; set; } = "";

    public string Limitation { get; set; } = "";
}

internal static class SanitizationVerification
{
    public static VerificationResult VerifyPathAbsent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new VerificationResult
            {
                Status = VerificationStatus.Failed,
                PathAbsent = false,
                Message = "No path was provided.",
                Limitation =
                    "The sanitization result cannot be verified without a target path."
            };
        }

        try
        {
            bool fileExists = File.Exists(path);
            bool directoryExists = Directory.Exists(path);

            if (fileExists || directoryExists)
            {
                return new VerificationResult
                {
                    Status = VerificationStatus.Failed,
                    PathAbsent = false,
                    Message = "The target still exists after the operation.",
                    Limitation =
                        "The target path was not removed successfully."
                };
            }

            return new VerificationResult
            {
                Status = VerificationStatus.Verified,
                PathAbsent = true,
                Message = "The target path is no longer present.",
                Limitation =
                    "Path absence verifies logical removal only. It does not by itself prove that all physical media copies, remapped blocks, spare areas, snapshots, or backups were sanitized."
            };
        }
        catch (Exception ex)
        {
            return new VerificationResult
            {
                Status = VerificationStatus.Failed,
                PathAbsent = false,
                Message = "Verification failed: " + ex.Message,
                Limitation =
                    "The operating system could not reliably determine whether the target still exists."
            };
        }
    }

    public static VerificationResult NotApplicable(
        string reason)
    {
        return new VerificationResult
        {
            Status = VerificationStatus.NotApplicable,
            PathAbsent = false,
            Message = reason ?? "Verification is not applicable.",
            Limitation =
                "No physical-media sanitization claim can be made from this verification."
        };
    }
}