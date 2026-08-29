# v1.4.0 Build Fix

`IdentityValidation.cs` içinde `SanitizationPlan.IsBootDisk` kullanımı kaldırıldı. Boot disk bilgisi, mevcut ve derlenebilir `UnifiedStoragePreflight.AnalyzePath` sonucundaki `IsBootDisk` alanından okunuyor.

Bu nedenle şu hata düzeltilmiştir:

```text
'SanitizationPlan' bir 'IsBootDisk' tanımı içermiyor
```

Windows PowerShell’de:

```powershell
cd D:\ahk\VoidErase-v1.4.0-SOURCE-FIXED
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Framework48.ps1
.\bin\Release\VoidErase.exe
```

Bu düzeltme fiziksel aygıta yazma komutu eklemez; yalnızca güvenli kimlik doğrulama kodunun mevcut preflight modeliyle derlenebilir olmasını sağlar.
