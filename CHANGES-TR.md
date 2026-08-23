# VoidErase Güncel Değişiklik Paketi

Bu paket; NIST XML kaydı, Türkçe/İngilizce kayıt açıklamaları, medya kimliği, Clear/Purge/Destroy karar modeli, sistem/boot disk güvenlik geçidi, sağ tık entegrasyonu, dry-run medya kontrolleri ve NIST rapor ekranını içerir.

## HTML/PDF raporları

İşlem özeti ekranında NIST XML kaydı varsa `HTML Raporu` ve `PDF Raporu` düğmeleri etkinleşir. HTML raporu XML kaydından salt-okunur biçimde oluşturulur. PDF çıktısı Windows üzerindeki Microsoft Edge veya Google Chrome ile HTML’den üretilir.

## Dil senkronizasyonu

Rapor, işlem sırasında seçili arayüz dilini kullanır. Türkçe modda başlıklar, karar, uyumluluk, validasyon ve sınırlamalar Türkçe; İngilizce modda İngilizce üretilir. Makine durum kodları korunur.

## Güvenlik sınırı

Bu paket fiziksel USB/HDD/SSD üzerine doğrudan zero-fill, DoD, TRIM, sanitize veya başka bir geri döndürülemez cihaz yazma komutu içermez. Sağlayıcı katmanı yalnızca dry-run planı sunar. Sistem ve boot diskleri değiştirilemez biçimde engellenir.

## Windows 10 derleme

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Framework48.ps1
```

Derleme sonrası `bin\\Release\\VoidErase.exe` çalıştırılır. Önce küçük bir test dosyasıyla XML, HTML ve PDF raporlarını deneyin. Sistem klasörlerini hedeflemeyin.
