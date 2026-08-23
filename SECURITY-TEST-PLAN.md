# VoidErase Güvenlik Test Planı

Bu test planı, gerçek fiziksel disk işlemi çalıştırmadan dosya silme ve güvenlik kapılarını doğrulamak içindir. Testler yalnızca Windows üzerinde, geçici bir test klasörü ve test dosyalarıyla yürütülmelidir.

| Test | Beklenen sonuç |
|---|---|
| Geçici klasörde normal dosya | Dosya işlemi tamamlanır, son yol bulunamaz ve işlem özeti doğrulandı olarak işaretlenir. |
| Gizli dosya | Dosya normalleştirilir, işlemden sonra kaynak yol bulunamaz. |
| Salt okunur dosya | Güvenlik kuralları izin veriyorsa öznitelik kaldırılır ve işlem tamamlanır. |
| Sistem öznitelikli dosya | İşlem reddedilir; kaynak korunur. |
| Sembolik bağlantı veya reparse point | İşlem reddedilir veya öğe atlanır; bağlantının hedefi takip edilmez. |
| Windows klasörü | `VoidEraseSafety.IsProtectedPath` nedeniyle işlem reddedilir. |
| Program Files klasörü | İşlem reddedilir. |
| Çalışan VoidErase.exe | `IsSameAsExecutable` nedeniyle işlem reddedilir. |
| Kilitli dosya | İşlem kontrollü hata verir; işlem özeti başarısız öğeyi gösterir. |
| İptal isteği, yıkıcı aşamadan önce | İşlem iptal edilir ve kaynak korunur. |
| İptal isteği, yıkıcı aşama başladıktan sonra | Motor işlemi yarıda bırakmadan tamamlar; iptal sonraki aşamaya uygulanır. |
| Erişim reddi veya dosyanın işlem sırasında değiştirilmesi | Son doğrulama başarısızsa işlem güvenli hata olarak raporlanır. |

> Bu testler fiziksel HDD, SSD, NVMe veya USB aygıtları üzerinde çalıştırılmamalıdır. Cihaz seviyesindeki sanitize/purge işlemleri bu proje aşamasında etkinleştirilmemiştir.
