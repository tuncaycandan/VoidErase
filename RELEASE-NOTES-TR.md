# VoidErase — Dil Etiketi Çakışması Düzeltmesi

English arayüzünde `Language` etiketi, daraltılmış dil kutusuna fazla yaklaştığı için metinler görsel olarak çakışabiliyordu.

Dil etiketi artık otomatik genişliğini kullanır ve dil seçim kutusu etiketin gerçek sağ kenarından 8 piksel sonra başlar:

```csharp
langLabel.AutoSize = true;
langLabel.Location = new Point(24, 52);
language.SetBounds(langLabel.Right + 8, 49, 85, 28);
```

Dil seçim kutusu 85 piksel genişliğindedir; `Türkçe`, `English` ve açılır ok için yeterli alan bırakır. Türkçe ve İngilizce etiket uzunlukları farklı olsa da kutu artık her iki dilde de etikete göre doğru konumlanır.

## Derleme

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Framework48.ps1
```
