# VoidErase v1.4.0 — Kanıt, Kimlik ve Arayüz Güncellemesi

VoidErase v1.4.0; kanıt yönetimi, medya kimliği görünürlüğü, raporlama, ayarlar kullanılabilirliği ve dry-run güvenliğini geliştirir. Doğrudan geri döndürülemez fiziksel disk yazma komutları eklenmemiştir.

## Medya Kimliği Doğrulaması

Uygulama, mümkün olduğunda işlem öncesi medya kimliğini kaydeder ve NIST kaydı oluşturulmadan önce işlem sonrası kimlik karşılaştırması yapar. Karşılaştırmada fiziksel disk yolu, disk numarası, model, seri numarası, medya türü, bağlantı türü ve medya boyutu dikkate alınır. Kimlik uyuşmazlığı olası aygıt değişimi olarak raporlanır ve fiziksel medya sanitizasyon iddiasına izin vermez.

## NIST Raporlama

NIST XML ve HTML raporları artık kimlik doğrulama durumunu, kimlik eşleşmesini, işlem öncesi kimliği, işlem sonrası kimliği ve sağlayıcı sürümünü gösterir. İşlem özeti de sanitizasyon kararı ve validasyon gerekliliğinin yanında kimlik doğrulama sonucunu gösterir.

Uygulama düzeyi işleme ve doğrulamanın fiziksel medya Purge veya Destroy kanıtı olmadığı sınırlaması korunmuştur.

## Güvenli Sağlayıcı Sınırı

Sanitizasyon sağlayıcısı sözleşmesine sağlayıcı sürümü ve fiziksel yazma yetkisi durumu eklendi. Yerleşik sağlayıcı dry-run modunda kalır ve fiziksel aygıt yazma yetkisini daima reddeder.

## Kullanıcı Arayüzü ve Ayarlar

Ayarlar penceresindeki gereksiz boşluklar azaltıldı. Gizli dosyaları sil seçeneği görünür ve doğru şekilde kaydedilir. Dil seçim kutusu daraltıldı ve Dil/Language etiketinin yanına çakışma olmadan yerleştirildi. Sağ tık menüsü etkinse yeşil onay, etkin değilse kırmızı çarpı gösterilir.

Ana pencerede iki dilli ToolTip’ler, NIST Kayıtları düğmesi, tıklanabilir sürüm etiketiyle güncelleme kontrolü, ortalanmış footer markası ve eşit genişlikte eylem düğmeleri korunur.

## Testler ve Güvenlik

Dry-run testleri sistem ve boot disklerini engelleme, kimlik uyuşmazlığı tespiti ve sağlayıcının fiziksel yazma yetkisini reddetmesi senaryolarını kapsar. Zero-fill, secure-erase, TRIM, firmware, `clean` veya başka geri döndürülemez fiziksel aygıt komutu bulunmaz.

## Yükseltme Notu

Framework 4.8 uygulamasını Windows 10 veya daha yeni bir Windows sürümünde, sağlanan PowerShell build script’iyle derleyin. Mevcut NIST XML kayıtları değiştirilmez; yeni kayıtlar v1.4.0 assembly sürüm bilgisini kullanır.
