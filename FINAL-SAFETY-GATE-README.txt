VoidErase FinalStorageSafetyGate

Bu sınıf fiziksel aygıta yazmaz ve hiçbir erase/overwrite/format/TRIM/sanitize komutu çalıştırmaz.

Değiştirilemez kurallar:
- Windows sistem diski daima engellenir.
- Boot disk daima engellenir.
- Offline veya read-only disk engellenir.
- Windows ve Program Files yolları engellenir.
- Uygulamanın kendi EXE dosyası engellenir.
- Sonuç yalnızca DryRunOnly olabilir; bu sınıf gerçek yazma yetkisi vermez.

Entegrasyon:
- FinalStorageSafetyGate.cs proje dosyasına eklendi.
- UsbTargetExecutionGate.Verify ve HddLogicalClearExecutionGate.Verify girişinde çağrılıyor.
- Windows üzerinde Release derlemesi yapıldıktan sonra yalnızca sistem diski seçiminin engellendiği doğrulanmalıdır.

Testte gerçek silme başlatmayın. C:\Windows ve sistem/boot diskinde beklenen sonuç Blocked olmalıdır.
