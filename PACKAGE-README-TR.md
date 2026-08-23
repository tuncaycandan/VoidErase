# VoidErase — Tüm Güvenli Süreçler Kaynak Paketi

Bu paket, VoidErase projesinin güncel güvenli süreçlerini tek kaynak arşivinde toplar.

## Dahil edilen süreçler

- .NET Framework 4.8 Windows Forms uygulaması ve Windows 10 derleme betiği
- AES-256-GCM dosya düzeyi işlem ve SHA-256 doğrulama
- Türkçe/İngilizce arayüz ve NIST kayıt yerelleştirmesi
- NIST SP 800-88 Rev. 2 karar ve raporlama modeli
- Clear, Purge, Destroy ve Blocked kararları
- USB, HDD, SSD, NVMe ve sanal medya sınıflandırması
- Model, seri numarası, fiziksel disk numarası, aygıt yolu, bus türü ve boyut kaydı
- XML 1.0 geçersiz karakter temizliği
- NIST XML doğrulama kayıtları ve rapor ekranı
- İşlem öncesi karar kaydı ve sağ tık işlemlerinde XML üretimi
- NIST XML dosyasını açan rapor düğmesi
- Windows Explorer sağ tık menüsü
- Sistem/boot diski, Windows, Program Files ve uygulamanın kendi EXE dosyası için değiştirilemez koruma
- Korunan hedeflerde gri/pasif kalıcı silme düğmesi
- USB/HDD/NVMe dry-run ve preflight kontrolleri
- Fiziksel aygıt komutu çalıştırmadan yetkili araç kullanımını planlamaya uygun raporlama altyapısı

## Windows 10 derleme

ZIP’i açın ve PowerShell’de proje klasöründe çalıştırın:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Framework48.ps1
```

Çıktı:

```text
bin\Release\VoidErase.exe
```

## Sağ tık menüsü

Güncel EXE’yi çalıştırın. Eski kayıtları yenilemek için önce **Sağ Tık Menüsünü Kaldır**, sonra tekrar etkinleştirin.

## NIST kayıtları

XML kayıtları şu klasöre yazılır:

```text
%LOCALAPPDATA%\VoidErase\NistRecords
```

Türkçe arayüzde kullanıcıya görünen sonuç, güvence, uyumluluk, doğrulama ve karar açıklamaları Türkçe oluşturulur. Eski XML kayıtları otomatik güncellenmez; yeni davranışı görmek için yeni işlem yapın.

## Güvenlik sınırı

Bu paket fiziksel USB/HDD/SSD aygıtlarına doğrudan yazan, cihazı kullanılmaz hâle getiren veya fiziksel Purge/Destroy işlemi yapan kod içermez. USB/HDD/NVMe kontrolleri dry-run/preflight ve raporlama amaçlıdır. Sistem ve boot diskleri daima engellenir.

Testleri yalnızca silinmesinde sakınca olmayan küçük dosyalarla yapın. `C:\Windows`, sistem sürücüsünün kökü, `Program Files` veya kişisel klasörlerin tamamında kalıcı silme başlatmayın.
