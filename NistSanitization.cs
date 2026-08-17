using System;
using System.Collections.Generic;

namespace VoidErase;

/// <summary>
/// NIST SP 800-88 Rev. 2 aligned terminology and decision model.
/// This layer deliberately separates application-level file deletion from
/// media sanitization so VoidErase never claims NIST sanitization when it
/// cannot establish the required media-level assurance.
/// </summary>
internal enum SanitizationMethod
{
    None,
    Clear,
    Purge,
    CryptographicErase,
    Destroy
}

internal enum SanitizationAssurance
{
    NotEstablished,
    ApplicationLevelOnly,
    MediaLevelCandidate,
    MediaLevelVerified
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

internal sealed class SanitizationDecision
{
    public MediaKind Media { get; init; }
    public SanitizationMethod Method { get; init; }
    public SanitizationAssurance Assurance { get; init; }
    public bool VerificationRequired { get; init; }
    public bool CanProceed { get; init; }
    public string Reason { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
}

internal static class NistSanitizationPlanner
{
    public static SanitizationDecision ForFileOperation(MediaKind media)
    {
        // The current VoidErase engine operates on files. AES-256-GCM
        // authenticates a temporary processing container; it is not itself
        // a media sanitization method. Do not label it Clear/Purge.
        return new SanitizationDecision
        {
            Media = media,
            Method = SanitizationMethod.None,
            Assurance = SanitizationAssurance.ApplicationLevelOnly,
            VerificationRequired = true,
            CanProceed = true,
            Reason =
                "The operation is application-level file processing. " +
                "NIST SP 800-88 Rev. 2 media sanitization has not been established.",
            RecommendedAction = media switch
            {
                MediaKind.SolidState =>
                    "For media reuse/disposal, use a trusted device/vendor purge or cryptographic-erase mechanism.",
                MediaKind.Magnetic =>
                    "For media reuse/disposal, use an approved media-level sanitization method rather than relying on file deletion.",
                MediaKind.Removable =>
                    "Use a media-specific sanitization method appropriate to the device and controller.",
                MediaKind.Virtual =>
                    "Sanitize the underlying storage and snapshots through the platform/provider controls.",
                _ =>
                    "Establish the media type and use an approved media-level sanitization method."
            }
        };
    }

    public static SanitizationDecision CryptographicErase(MediaKind media, bool keyDestructionVerified)
    {
        return new SanitizationDecision
        {
            Media = media,
            Method = SanitizationMethod.CryptographicErase,
            Assurance = keyDestructionVerified
                ? SanitizationAssurance.MediaLevelVerified
                : SanitizationAssurance.MediaLevelCandidate,
            VerificationRequired = true,
            CanProceed = keyDestructionVerified,
            Reason = keyDestructionVerified
                ? "Cryptographic-erase key destruction was verified."
                : "Cryptographic erase cannot be asserted until the relevant media encryption key destruction is verified.",
            RecommendedAction = keyDestructionVerified
                ? "Retain the sanitization record and verification evidence."
                : "Do not claim sanitization; complete and verify key destruction first."
        };
    }

    public static IReadOnlyList<SanitizationMethod> SupportedMethods =>
        new[]
        {
            SanitizationMethod.CryptographicErase,
            SanitizationMethod.Clear,
            SanitizationMethod.Purge,
            SanitizationMethod.Destroy
        };
}
