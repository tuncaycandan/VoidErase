VoidErase NIST SP 800-88 Rev. 2 kayıt altyapısı

Eklenen dosya:
- NistSanitizationRecord.cs

İşlem tamamlandığında veya iptal edildiğinde uygulama şu klasöre XML kayıt yazar:
%LOCALAPPDATA%\VoidErase\NistRecords

Kayıtta şu bilgiler bulunur:
- `Compatibility`: NIST uyumluluk durumu (`Candidate`, `NotEstablished`, `Blocked`)
- `ValidationRequired`: validasyon gerekip gerekmediği
- `DecisionReason`: medya türüne göre kararın nedeni; Türkçe modda Türkçe oluşturulur
- benzersiz kayıt kimliği ve UTC zaman damgaları
- sonuç: Succeeded, Failed, Blocked veya Cancelled
- teknik ve yöntem açıklaması
- toplam dosya, boyut, başarılı/başarısız/atlanan sayıları
- doğrulama sonucu, yöntemi ve kanıt açıklaması
- hedef yol ve medya kimliği alanları
- NIST iddiasının izinli olup olmadığı ve sınırlaması

Mevcut uygulama düzeyi AES-256-GCM + doğrulanmış dosya silme akışı için ClaimAllowed false bırakılır. Bunun nedeni bu işlemin fiziksel medya Purge veya Destroy kanıtı sunmamasıdır.

Kayıt yazma hatası silme sonucunu değiştirmez; hata VoidErase günlük dosyasına yazılır.

Test:
1. Küçük bir test dosyasını uygulama ile işleyin.
2. İşlem özetini kapatın.
3. %LOCALAPPDATA%\VoidErase\NistRecords klasöründe NIST-<id>.xml dosyasını kontrol edin.
4. Dosyada Outcome=Succeeded ve Verification/Outcome=Passed alanlarını doğrulayın.
5. `Compatibility`, `ValidationRequired` ve `DecisionReason` alanlarını kontrol edin.
6. Türkçe modda `DecisionReason` değerinin Türkçe olduğunu doğrulayın.

Sistem veya boot disk üzerinde gerçek işlem başlatmayın. Bu altyapı fiziksel aygıta yazma veya cihaz sanitizasyonu gerçekleştirmez.
