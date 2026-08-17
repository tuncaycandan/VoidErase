# VoidErase v1.2 — NIST SP 800-88 Rev. 2 Aligned Architecture

## Scope

VoidErase v1.2 separates **application-level file processing** from **media sanitization**.

The current AES-256-GCM workflow authenticates a temporary processing container and verifies it before deleting the source file. That is useful for safe application behavior, but it is **not by itself a NIST SP 800-88 Rev. 2 media sanitization method**.

VoidErase must never report a successful file deletion as a verified media sanitization event unless media-level assurance has actually been established.

## Sanitization decision model

Every future sanitization operation is represented by:

- media type: magnetic, solid-state, removable, virtual, optical, or unknown;
- method: Clear, Purge, Cryptographic Erase, Destroy, or None;
- assurance state;
- whether verification is required;
- reason for the selected method;
- recommended follow-up action.

The implementation is intentionally conservative when the media type or device capability is unknown.

## v1.2 execution layers

### Layer 1 — Safety and scope

Before any operation:

1. Normalize and validate the target path.
2. Reject protected Windows/system locations.
3. Do not traverse junctions, symbolic links, or other reparse points by default.
4. Preserve the existing cancellation and confirmation safeguards.
5. Record every skipped or rejected item.

### Layer 2 — Media identification

The application should identify the physical or virtual storage context where practical.

Unknown media must remain `Unknown`; the application must not guess SSD/HDD from a drive letter alone.

For removable, virtual, RAID, storage spaces, and controller-backed devices, the decision must prefer the device/vendor/platform sanitization capability over generic file operations.

### Layer 3 — Sanitization method selection

The planner selects only a method appropriate to the established media capability.

- **Cryptographic Erase:** only when the relevant media encryption key is actually destroyed and that destruction can be verified.
- **Clear:** only when an approved media-specific clear technique is applicable.
- **Purge:** preferred for media reuse/disposal when the device supports an approved purge mechanism.
- **Destroy:** physical destruction when other approved methods cannot provide the required assurance.
- **None:** application-level file deletion where media sanitization cannot be established.

VoidErase should not invent a generic multi-pass overwrite claim for SSD/flash media.

### Layer 4 — Verification / validation

Verification answers whether the sanitization operation completed as intended.

Validation answers whether the resulting state provides the required confidentiality assurance for the media and sensitivity level.

The UI and operation record should keep these concepts separate.

A successful AES-GCM decrypt/rehash of the temporary container is an **application integrity verification**, not proof that old physical media sectors are unrecoverable.

### Layer 5 — Evidence

Each operation should be able to produce a sanitization record containing:

- timestamp;
- target path or media identifier as appropriate;
- media type and identification confidence;
- selected sanitization method;
- tool/application version;
- operation result;
- verification result;
- validation result;
- errors and skipped items;
- operator confirmation where applicable.

Sensitive file contents, encryption keys, and plaintext data must never be written to the evidence log.

## Cryptographic Erase boundary

Cryptographic Erase is fundamentally different from VoidErase's current per-file AES-256-GCM temporary-container process.

For v1.2, CE should be implemented as a device/volume capability adapter rather than by generating a temporary AES key for each file. The adapter must establish that the key protecting the relevant media data has been destroyed and must retain evidence of the operation without retaining the key.

If VoidErase cannot establish that boundary, it must report **Application-level only** rather than **NIST-sanitized**.

## Device capability adapters

Future adapters should be isolated behind an interface similar to:

```text
IMediaSanitizer
  IdentifyMedia()
  GetCapabilities()
  Execute(method)
  Verify()
  Validate()
  GetEvidence()
```

Possible implementations include:

- Windows/device-specific sanitize commands;
- trusted SSD/NVMe sanitize or cryptographic-erase mechanisms;
- approved vendor utilities with verifiable exit/status information;
- organization-approved external sanitization tools;
- physical destruction workflow recording.

Vendor/controller trust is part of the assurance decision. A command returning success is not automatically equivalent to validated sanitization.

## Safety rules

1. Never silently downgrade a requested media sanitization operation to ordinary file deletion.
2. Never follow a junction/symlink during recursive traversal.
3. Never claim NIST compliance solely because AES-256-GCM was used.
4. Never claim that overwrite passes guarantee sanitization on SSD/flash media.
5. Never retain sanitization keys in logs.
6. Preserve the original file when verification fails in the existing safe file-processing path.
7. Make the final report explicit about what was verified and what was not.

## Product wording

Recommended product wording:

> **NIST SP 800-88 Rev. 2 aligned architecture**
>
> VoidErase distinguishes application-level secure deletion from media sanitization. Media-level sanitization is reported only when the selected device-specific method and verification provide sufficient evidence.

Avoid wording such as:

> "AES-256-GCM is NIST SP 800-88 sanitization."

That statement would be technically misleading.

## Reference

NIST SP 800-88 Rev. 2 was published September 26, 2025 and supersedes Rev. 1. The revision emphasizes media sanitization programs, validation, media-specific techniques, and stronger treatment of cryptographic erase and key sanitization.
