# VoidErase Proje İnceleme Raporu

## Genel değerlendirme

`VoidErase.zip` arşivi incelendi. Proje, **.NET Framework 4.8 ve Windows Forms** tabanlı bir C# masaüstü uygulaması olarak yapılandırılmıştır. Uygulamanın ana amacı dosya/klasör silme akışı ile depolama ortamı tespiti, ön değerlendirme ve güvenli silme kararlarını bir arada yürütmektir.

Kod tabanında güvenlik açısından önemli bir yaklaşım görülüyor: depolama karar/probe/preflight katmanları, gerçek cihaz seviyesinde yıkıcı işlem yapan bir yürütücüden ayrılmaya çalışılmış. README ve patch notlarına göre NVMe yetenek sorgusu ile HDD/USB ön kontrolleri **salt okunur veya dry-run** olarak tasarlanmış. Bu nedenle inceleme sırasında gerçek disk silme, sanitize, overwrite, format veya fiziksel cihaza yazma işlemi çalıştırılmadı.

## Proje yapısı

| Alan | Bulgular |
|---|---|
| Platform | .NET Framework 4.8 |
| Kullanıcı arayüzü | Windows Forms |
| Ana giriş noktası | `Program.cs` |
| Dosya silme motoru | `Program.cs`, `DeletionEngineV2.cs` |
| Depolama analizi | `StorageSanitizationProtocol.cs`, `MediaDetection.cs`, `UnifiedStoragePreflight.cs` |
| HDD akışı | `HddLogicalClearPreflight.cs`, `HddLogicalClearExecutionGate.cs`, `HddLogicalClearExecution.cs` |
| NVMe akışı | `NvmeCapabilityProbe.cs` |
| USB akışı | `UsbTargetPreflight.cs` |
| Doğrulama ve kayıt | `SanitizationVerification.cs`, `VoidEraseLogger.cs` |
| Ayarlar ve formlar | `VoidEraseSettings.cs`, `VoidEraseSettingsForm.cs`, `OperationSummaryForm.cs` |

Proje dosyası 22 C# kaynağını derlemeye dahil ediyor. Arşivde ayrıca eski sürümler, yedekler ve henüz projeye dahil edilmeyen deneysel dosyalar bulunuyor. Özellikle `StorageSanitization.cs`, `UsbTargetExecution.cs` ve `UsbTargetExecutionGate.cs` mevcut arşivde bulunmasına rağmen `.csproj` içine dahil edilmemiş. Bu durumun bilinçli bir sürümleme tercihi mi yoksa eksik entegrasyon mu olduğu netleştirilmeli.

## Tespit edilen güçlü yönler

Kodda reparse point ve sembolik bağlantıların takip edilmesini engelleyen açık kontroller bulunuyor. Sistem sürücüsü, korumalı yollar, gizli dosyalar ve hedef yolun sonradan değişmesi gibi riskler için çeşitli güvenlik kontrolleri eklenmiş. Fiziksel disk analizinde model, seri numarası, disk numarası, medya türü ve sistem diski durumu gibi bilgiler birlikte değerlendiriliyor.

`StorageSanitizationProtocol` karar motoru ile gerçek yürütme katmanlarının ayrılması doğru bir mimari yönde ilerliyor. `PrepareDeviceSanitizePreflight` gibi metotların yalnızca ön değerlendirme üretmesi ve bağımsız onay/güvenlik kapısı gerektirmesi, yanlış hedef üzerinde işlem yapılması riskini azaltan bir tasarım.

Ayrıca projede değişiklik geçmişi, patch notları ve önceki kaynak kopyaları tutulmuş. Bu, geri dönüş ve karşılaştırma açısından yararlı olsa da uzun vadede kaynak ağacının sadeleştirilmesi gerekiyor.

## Öncelikli sorunlar ve riskler

### 1. Kaynak ağacında sürüm karmaşası

Kök dizinde `Program.cs`, `Program_CURRENT.txt`, tarihli `Program(...).cs`, `.backup` ve `.before-*` dosyaları bir arada tutuluyor. Aynı durum NVMe ve depolama protokolü dosyalarında da var. Bu dosyalar derlemeye dahil edilmese bile geliştirici açısından yanlış dosyanın düzenlenmesi veya yanlış sürümün dağıtılması riskini artırıyor.

**Öneri:** Tek bir etkin kaynak sürümü bırakılmalı; eski dosyalar `archive/` veya Git branch/tag yapısına taşınmalı. `.csproj` dosyası açık ve kontrollü bir kaynak listesine sahip olmaya devam etmeli.

### 2. Derleme doğrulaması bu ortamda yapılamadı

Arşiv .NET Framework 4.8 ve Windows'a özgü `System.Management`/Windows Forms bağımlılıkları içeriyor. İnceleme ortamında `dotnet`, `msbuild`, `xbuild`, `mono`, `csc` ve `mcs` derleyicileri mevcut olmadığından gerçek Release derlemesi çalıştırılamadı. Bu nedenle rapor, statik kaynak incelemesine dayanıyor; derleme temizliği henüz doğrulanmış değildir.

**Öneri:** Windows üzerinde Visual Studio veya Build Tools ile `VoidErase.Framework48.csproj` dosyası Release yapılandırmasında derlenmeli. İlk teknik hedef, uyarıları görünür hale getirerek tüm uyarıların ve hataların kayıt altına alınması olmalı.

### 3. Büyük sorumlulukların `Program.cs` içinde toplanması

`Program.cs` yaklaşık 4.484 satır uzunluğunda ve kullanıcı arayüzü, dosya/klasör taraması, silme akışı, durum güncellemesi, geçmiş, kabuk yenileme ve bazı depolama operasyonu çağrılarını aynı dosyada barındırıyor. Bu durum test edilebilirliği ve hata ayıklamayı zorlaştırır.

**Öneri:** Öncelikle şu katmanlar ayrıştırılmalı: `FileDeletionService`, `TargetEnumerationService`, `SafetyPolicy`, `OperationHistoryStore`, `ProgressReporter` ve `MainFormController`. Form sınıfları yalnızca kullanıcı etkileşimini yönetmeli; dosya işlemleri servis sınıflarına taşınmalı.

### 4. Test kapsamı sınırlı görünüyor

Arşivde `HddLogicalClearExecutionTest.cs` bulunuyor; ancak genel dosya silme, reparse point, erişim reddi, kilitli dosya, yarış durumu, gizli dosya, sistem yolu ve iptal senaryoları için düzenli bir test projesi görünmüyor.

**Öneri:** Gerçek diske dokunmayan bir test projesi oluşturulmalı. Dosya sistemi testleri geçici klasörlerle, cihaz analiz testleri ise sahte WMI/probe sağlayıcılarıyla çalıştırılmalı. Özellikle hedef yolun işlem sırasında junction/reparse point'e dönüşmesi ve iptal isteğinin farklı aşamalarda gelmesi test edilmeli.

### 5. Güvenlik açısından bağımsız son onay akışı güçlendirilmeli

Kodda çeşitli gate ve preflight sınıfları bulunuyor. Ancak cihaz seviyesinde ileride gerçek yürütücü eklenmesi planlanıyorsa, planın hazırlanması ile son onayın verilmesi arasında hedefin yeniden doğrulanması, kullanıcı onayının işlem özetiyle eşleştirilmesi ve model/seri/disk numarası değişikliklerinin kesin biçimde reddedilmesi zorunlu olmalı.

**Öneri:** Yürütme öncesinde değiştirilemez bir `ApprovalToken` veya benzeri işlem özeti oluşturulmalı. Bu özet hedefin fiziksel kimliğini, boyutunu, medya tipini, sistem diski durumunu, zaman damgasını ve kullanıcı onayını içermeli. Gerçek yürütücü, yeni bir preflight sonucu bu özetle birebir eşleşmiyorsa durmalı.

## Önerilen geliştirme sırası

| Öncelik | Geliştirme | Amaç |
|---:|---|---|
| 1 | Windows üzerinde gerçek Release derlemesi | Mevcut kodun derlenebilirliğini doğrulamak |
| 2 | Kaynak ağacını temizleme | Eski/yedek dosya kaynaklı sürüm hatalarını önlemek |
| 3 | Dosya silme motorunu `Program.cs` dışına çıkarma | Test edilebilirlik ve sürdürülebilirlik |
| 4 | Dry-run/preview ekranını güçlendirme | Kullanıcıya kesin hedef ve kapsam göstermek |
| 5 | İptal, hata ve yeniden deneme akışlarını standartlaştırma | Kullanıcı deneyimi ve güvenilirlik |
| 6 | Otomatik test projesi ekleme | Regresyonları ve güvenlik kapısı hatalarını yakalamak |
| 7 | Lokalizasyon ve günlükleme iyileştirmesi | Türkçe/İngilizce tutarlılığı ve denetlenebilirlik |

## Sonuç

Proje, güvenli silme konusunda önemli koruma katmanları eklenmiş, ancak hâlen **büyük ve karmaşık bir geliştirme ağacına** sahip görünüyor. En doğru sonraki adım, yeni özellik eklemeden önce Windows ortamında Release derlemesini doğrulamak ve ardından `Program.cs` içindeki sorumlulukları kademeli olarak ayırmaktır.

Bir sonraki geliştirme için üç uygulanabilir seçenek bulunuyor: mevcut derleme hatalarını düzeltmek, arayüzde belirli bir özellik eklemek veya dosya silme/güvenlik katmanını refactor ederek test altyapısı kurmak. Kullanıcının önceliği belirlendiğinde ilgili kaynak dosyalarında kontrollü değişiklik yapılabilir.

> Not: Bu inceleme sırasında arşivdeki gerçek silme veya cihaz seviyesinde yıkıcı operasyonlar çalıştırılmamıştır.
