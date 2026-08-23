# Güvenli Sanitizasyon Sağlayıcısı ve Kanıt Katmanı

Bu değişiklik paketi, fiziksel aygıta doğrudan yazma yapmayan güvenli sağlayıcı sözleşmesini içerir. `DryRunSanitizationProvider` yalnızca plan üretir ve fiziksel yazma yetkisi vermez.

## Kimlik doğrulama

`SanitizationIdentitySnapshot` işlem öncesi ve sonrası model, seri numarası, fiziksel disk, disk numarası, medya türü, bağlantı türü, kapasite ve sistem/boot bilgilerini taşır. `Matches` metodu kimlik değişikliğinde veya sistem/boot diskte `false` döner.

## Kanıt içe aktarma

`SanitizationEvidenceImporter` yalnızca XML kanıt dosyasını okur. Kanıt; sağlayıcı adı, hedef fiziksel aygıt, seri numarası, kapasite, doğrulanmış sağlayıcı durumu ve işlem sonucunu içermelidir. Kimlik eşleşmezse kanıt reddedilir ve NIST Purge iddiasına izin verilmemelidir.

## Raporlar

`NistReportExporter` mevcut NIST XML kaydından salt-okunur HTML raporu oluşturur. Microsoft Edge veya Google Chrome kuruluysa HTML’den PDF üretmeyi dener. Rapor üretimi fiziksel aygıt komutu çalıştırmaz.

## Güvenlik sınırı

Bu paket zero-fill, DoD, `clean`, TRIM, sanitize veya başka bir fiziksel disk yazma komutu içermez. Gerçek cihaz sanitizasyonu için kurum tarafından onaylanmış araç ve prosedür kullanılmalı; VoidErase yalnızca hedef kimliğini, kanıtı ve raporu yönetmelidir.
