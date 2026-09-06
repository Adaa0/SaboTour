# Playtest'e Yeni Build Yükleme — Adım Adım

Bu klasördeki dosyalar SteamPipe (Steam'in build yükleme sistemi) için.
Her yeni sürümde aynı adımları tekrarlayacaksın, sadece 3. ve 5. adımlar
değişecek.

## 1. Bir kere kurulacaklar (ilk seferde)

1. **SteamCMD'yi indir**: https://developer.valvesoftware.com/wiki/SteamCMD
   ZIP'i indir, örneğin `C:\steamcmd\` klasörüne çıkar.
2. ~~Depot ID'ni bul~~ — ✅ zaten dolduruldu: Depot ID **5071181**
   ("SaboTour Playtest Content"), `app_build.vdf` ve
   `depot_build_5071181.vdf` içine yazıldı. Bu adımı bir daha yapmana
   gerek yok.

## 2. Unity'den build al

- `File > Build Settings` → Platform Windows → **Build**.
- Çıktı klasörünü şuraya ver: proje kökünün YANINDAKİ (Assets'in
  dışındaki) `SteamBuildOutput` klasörü — yoksa oluştur.
  (`C:\Users\adaae\Desktop\SaboTour\SteamBuildOutput\`)
- **ÖNEMLİ**: `steam_appid.txt` dosyasını bu build klasörüne KOYMA —
  o sadece Editor'de/Steam dışı test için vardı (bkz. CLAUDE.md). Steam
  üzerinden başlatılan gerçek build'de buna gerek yok, kafa karıştırmasın
  diye içeride olmasın.
- Mode'un (`TransportSwitcher`) **Steam** olduğundan emin ol, build almadan
  önce sahneyi kaydet (CLAUDE.md'deki bilinen tuzak).

## 3. steamcmd ile yükle

PowerShell/CMD aç, SteamCMD'nin olduğu klasöre git ve şunu çalıştır
(kendi Steamworks kullanıcı adını yaz — Steamworks'te oturum açtığın
hesap, senin normal Steam hesabın DEĞİL olabilir, "Yayıncı" hesabı):

```
steamcmd.exe +login KULLANICI_ADIN +run_app_build "C:\Users\adaae\Desktop\SaboTour\SteamPipeScripts\app_build.vdf" +quit
```

- Şifreni soracak, gir.
- Steam Guard/Mobil Onaylayıcı kodu isteyecek (telefonundaki Steam
  uygulamasından), gir.
- Yükleme bitince "Successfully finished AppID 5071180 build" gibi bir
  mesaj görürsün. Build birkaç dakika sürebilir (dosya boyutuna göre).

## 4. Build'i playtest'e canlı yap

Bu adım OTOMATİK olmuyor — yüklemek yeterli değil, hangi "branch"e
(sürüm dalı) canlı olacağını Steamworks'ten SEN seçmelisin:

1. Steamworks → SaboTour Playtest → **SteamPipe → Builds**.
2. Az önce yüklediğin build'i listede bul (tarih/saatle tanırsın).
3. Karşısındaki **"default"** (ya da playtest'in kullandığı branch adı
   neyse) açılır menüsünden bu build'i seç.
4. **"Set build live on branches"** butonuna bas, onayla.

Bundan sonra playtest'e erişimi olan kişiler Steam istemcisinde
otomatik güncelleme alır (ya da bir sonraki açılışta).

## Her yeni sürümde tekrar edeceğin kısa özet

1. Unity'den yeni build al (`SteamBuildOutput` klasörüne, üzerine yaz).
2. İstersen `app_build.vdf` içindeki `"desc"` alanını güncelle (ör.
   "v0.3 - kurtarma sistemi eklendi") — sadece senin görebileceğin bir
   not, oyunculara görünmüyor.
3. Yukarıdaki steamcmd komutunu tekrar çalıştır.
4. SteamPipe → Builds'ten yeni build'i branch'e canlı yap.
