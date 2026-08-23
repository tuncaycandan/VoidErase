using System;

internal enum SanitizationMethod
{
    None,
    Clear,
    Purge,
    CryptographicErase,
    Destroy
}

internal enum MediaKind
{
    Unknown,
    Magnetic,
    SolidState,
    Removable,
    Virtual,
    Optical
}

internal enum SanitizationAssurance
{
    None,
    Basic,
    Enhanced,
    High
}

internal enum NistCompatibilityState
{
    NotEstablished,
    Candidate,
    Verified,
    Blocked
}

internal sealed class SanitizationDecision
{
    public MediaKind Media { get; set; }
    public SanitizationMethod Method { get; set; }
    public SanitizationAssurance Assurance { get; set; }
    public NistCompatibilityState Compatibility { get; set; }

    public bool RequiresDeviceCommand { get; set; }
    public bool VerificationRequired { get; set; }
    public bool ValidationRequired { get; set; }
    public bool NistAlignedClaimAllowed { get; set; }

    public string Technique { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string ClaimLimitation { get; set; } = "";
}

internal static class NistSanitization
{
    // This is a decision/reporting layer only. It never executes a device
    // command and never authorizes a destructive operation.
    public static SanitizationDecision Decide(MediaInfo media)
    {
        if (media == null)
            return Blocked("Media information is unavailable.", "Medya bilgisi alınamadı.");

        if (media.IsSystemDrive)
            return Blocked("The Windows system drive is always blocked.", "Windows sistem sürücüsü daima engellenir.");

        switch (media.Kind)
        {
            case MediaKind.Magnetic:
                return new SanitizationDecision
                {
                    Media = media.Kind,
                    Method = SanitizationMethod.Clear,
                    Assurance = SanitizationAssurance.Basic,
                    Compatibility = NistCompatibilityState.Candidate,
                    RequiresDeviceCommand = false,
                    VerificationRequired = true,
                    ValidationRequired = true,
                    NistAlignedClaimAllowed = false,
                    Technique = "Logical overwrite / application-level processing",
                    MethodName = "Clear (application scope only)",
                    Reason = "Magnetic media may be eligible for Clear when the selected scope and threat model permit it.",
                    Recommendation = "Use a documented Clear procedure and record verification and validation evidence.",
                    ClaimLimitation = "Application-level processing alone does not establish physical-media Purge."
                };

            case MediaKind.SolidState:
                return new SanitizationDecision
                {
                    Media = media.Kind,
                    Method = SanitizationMethod.Purge,
                    Assurance = SanitizationAssurance.Enhanced,
                    Compatibility = NistCompatibilityState.NotEstablished,
                    RequiresDeviceCommand = true,
                    VerificationRequired = true,
                    ValidationRequired = true,
                    NistAlignedClaimAllowed = false,
                    Technique = "Device-supported sanitization or cryptographic erase",
                    MethodName = "Purge (device capability and evidence required)",
                    Reason = "SSD remapping, spare area and over-provisioning can prevent logical overwrite from covering all media.",
                    Recommendation = "Use a manufacturer-supported sanitization method outside this application and retain its evidence.",
                    ClaimLimitation = "VoidErase does not claim Purge from file deletion or ordinary overwriting."
                };

            case MediaKind.Removable:
                return new SanitizationDecision
                {
                    Media = media.Kind,
                    Method = SanitizationMethod.Purge,
                    Assurance = SanitizationAssurance.Enhanced,
                    Compatibility = NistCompatibilityState.NotEstablished,
                    RequiresDeviceCommand = true,
                    VerificationRequired = true,
                    ValidationRequired = true,
                    NistAlignedClaimAllowed = false,
                    Technique = "Device-supported sanitization; capability must be established",
                    MethodName = "Purge (not established)",
                    Reason = "USB flash controllers may retain data in areas not exposed through the file system.",
                    Recommendation = "Use an approved device or organizational sanitization procedure; keep the device evidence.",
                    ClaimLimitation = "A USB file-level operation is not reported as physical-media Purge."
                };

            case MediaKind.Virtual:
                return new SanitizationDecision
                {
                    Media = media.Kind,
                    Method = SanitizationMethod.CryptographicErase,
                    Assurance = SanitizationAssurance.Enhanced,
                    Compatibility = NistCompatibilityState.NotEstablished,
                    RequiresDeviceCommand = true,
                    VerificationRequired = true,
                    ValidationRequired = true,
                    NistAlignedClaimAllowed = false,
                    Technique = "Provider-level cryptographic key destruction",
                    MethodName = "Purge / cryptographic erase (provider evidence required)",
                    Reason = "Virtual storage depends on the underlying provider and its retention mechanisms.",
                    Recommendation = "Use provider-level sanitization and retain provider verification evidence.",
                    ClaimLimitation = "VoidErase cannot establish sanitization of the underlying provider from a file-level operation."
                };

            case MediaKind.Optical:
                return new SanitizationDecision
                {
                    Media = media.Kind,
                    Method = SanitizationMethod.Destroy,
                    Assurance = SanitizationAssurance.High,
                    Compatibility = NistCompatibilityState.NotEstablished,
                    RequiresDeviceCommand = false,
                    VerificationRequired = true,
                    ValidationRequired = true,
                    NistAlignedClaimAllowed = false,
                    Technique = "Physical destruction",
                    MethodName = "Destroy (physical process required)",
                    Reason = "Logical processing may not reliably sanitize optical media.",
                    Recommendation = "Use an approved physical destruction process and record chain-of-custody evidence.",
                    ClaimLimitation = "VoidErase does not perform or certify physical destruction."
                };

            default:
                return Blocked("The media type could not be determined safely.", "Medya türü güvenli biçimde belirlenemedi.");
        }
    }

    private static SanitizationDecision Blocked(string english, string turkish)
    {
        return new SanitizationDecision
        {
            Media = MediaKind.Unknown,
            Method = SanitizationMethod.None,
            Assurance = SanitizationAssurance.None,
            Compatibility = NistCompatibilityState.Blocked,
            RequiresDeviceCommand = false,
            VerificationRequired = true,
            ValidationRequired = true,
            NistAlignedClaimAllowed = false,
            Technique = "None",
            MethodName = "Not established",
            Reason = english,
            Recommendation = turkish,
            ClaimLimitation = "No NIST sanitization claim is permitted."
        };
    }
}
