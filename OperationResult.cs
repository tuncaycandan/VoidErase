using System;
using System.Collections.Generic;
using System.IO;

namespace VoidErase;

internal sealed class OperationResult
{
    private string targetPath = "";

    public string TargetPath
    {
        get => targetPath;
        set
        {
            targetPath = value ?? "";

            if (FileSystemPathCanBeClassified(targetPath))
            {
                MediaKind = MediaDetection.Detect(targetPath);
                SanitizationDecision decision =
                    NistSanitizationPlanner.ForFileOperation(MediaKind);

                SanitizationMethod = decision.Method;
                SanitizationAssurance = decision.Assurance;
                SanitizationVerificationRequired = decision.VerificationRequired;
                SanitizationReason = decision.Reason;
                SanitizationRecommendation = decision.RecommendedAction;
            }
        }
    }

    public DateTime StartedAt { get; set; }
    public TimeSpan Elapsed { get; set; }

    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }

    public int Successful { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Verified { get; set; }

    public bool Cancelled { get; set; }

    // NIST SP 800-88 Rev. 2 evidence model.
    // A file-level authenticated transform must not be represented as
    // media sanitization unless media-level assurance was actually established.
    public SanitizationMethod SanitizationMethod { get; set; } = SanitizationMethod.None;
    public SanitizationAssurance SanitizationAssurance { get; set; } = SanitizationAssurance.NotEstablished;
    public MediaKind MediaKind { get; set; } = MediaKind.Unknown;
    public bool SanitizationVerificationRequired { get; set; }
    public string SanitizationReason { get; set; } = "";
    public string SanitizationRecommendation { get; set; } = "";

    public List<string> SuccessfulFiles { get; } = new();
    public List<string> FailedFiles { get; } = new();
    public List<string> SkippedFiles { get; } = new();

    private static bool FileSystemPathCanBeClassified(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return Path.IsPathRooted(path) &&
                   (File.Exists(path) || Directory.Exists(path));
        }
        catch
        {
            return false;
        }
    }
}