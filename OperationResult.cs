using System;
using System.Collections.Generic;

namespace VoidErase;

internal sealed class OperationResult
{
    public string TargetPath { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public TimeSpan Elapsed { get; set; }

    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }

    public int Successful { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Verified { get; set; }

    public bool Cancelled { get; set; }

    // NIST SP 800-88 Rev. 2 / sanitization metadata
    public string SanitizationMethod { get; set; } =
    "Cryptographic transformation + verified deletion";
	public string SanitizationStandard { get; set; } =
    "NIST SP 800-88 Rev. 2 aligned reporting";
    public string VerificationMethod { get; set; } = "AES-256-GCM + SHA-256";
    public bool KeyDestructionCompleted { get; set; }

    // Ek işlem bilgileri
    public string ErasureMethod { get; set; } =
    "Cryptographic transformation + verified deletion";
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public string VerificationAlgorithm { get; set; } = "SHA-256";
    public bool VerificationCompleted { get; set; }

    public string NistRecordPath { get; set; } = "";
    public string NistCompatibility { get; set; } = "NotEstablished";
    public bool NistValidationRequired { get; set; }
    public string NistDecisionReason { get; set; } = "";
    public string NistMediaSummary { get; set; } = "";
    public SanitizationIdentitySnapshot PreOperationIdentity { get; set; }
    public SanitizationIdentitySnapshot PostOperationIdentity { get; set; }
    public bool IdentityMatch { get; set; }
    public string IdentityValidation { get; set; } = "";
    public string FailureReason { get; set; } = "";

    public List<string> SuccessfulFiles { get; } = new();
    public List<string> FailedFiles { get; } = new();
    public List<string> SkippedFiles { get; } = new();
}
