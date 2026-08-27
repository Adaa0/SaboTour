using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ÇEVİRİ SÖZLÜĞÜ — oyundaki her oyuncu metni burada, iki dilde.
///
/// KULLANIMI (kod):   Loc.T("race.finallap")      → "SON TUR!" / "FINAL LAP!"
/// Değişkenli metin:  Loc.T("race.lap", 2, 3)     → "Tur 2/3"  / "Lap 2/3"
///
/// ── NEDEN TEK DOSYADA SÖZLÜK (CSV/JSON değil) ──
/// Çeviriler kodla birlikte derleniyor. Ayrı bir veri dosyası olsaydı onu
/// build'e dahil etmeyi unutmak (Resources/StreamingAssets tuzağı) ya da
/// "Editor'de çalışıyor, gerçek build'de yok" tipi bir sorun yaşamak
/// mümkündü — bu projede tam olarak o hata MinimapController'da bir kez
/// yaşandı (Shader.Find, shader stripping). Sözlük kodun içindeyse böyle
/// bir risk yok.
///
/// ── YENİ METİN EKLERKEN ──
/// Anahtar biçimi: "bölüm.ad" (menu.host, race.finallap, sab.armed).
/// Kodda düz string yazma alışkanlığına dönersen o metin İngilizce oynayan
/// oyuncuda Türkçe kalır — ve bunun fark edilmesi çok zordur.
///
/// ── YENİ DİL EKLEMEK ──
/// GameLanguage'daki Language enum'una bir değer + DisplayNames'e bir isim
/// ekle, buradaki her satıra üçüncü bir string ekle. Sözlük tek yerde
/// olduğu için eksik çeviri aramak da tek dosyada arama demek.
/// </summary>
public static class Loc
{
    /// <summary>Anahtar → { Türkçe, İngilizce }. Sıra Language enum'uyla aynı.</summary>
    private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        // ─────────────── ANA MENÜ / LOBİ ───────────────
        ["menu.play"]           = new[] { "OYNA", "PLAY" },
        ["menu.host"]           = new[] { "Oyun Kur", "Host Game" },
        ["menu.quickjoin"]      = new[] { "Hızlı Katıl", "Quick Join" },
        ["menu.invite"]         = new[] { "Davet Et", "Invite Friend" },
        ["menu.howtoplay"]      = new[] { "Nasıl Oynanır", "How to Play" },
        ["menu.feedback"]       = new[] { "Geri Bildirim", "Feedback" },
        ["menu.ready"]          = new[] { "Hazırım", "Ready" },
        ["menu.notready"]       = new[] { "Hazır Değilim", "Not Ready" },
        ["menu.namehint"]       = new[] { "İsmini yaz...", "Enter your name..." },
        ["menu.loading"]        = new[] { "Yükleniyor...", "Loading..." },
        ["menu.loadingplayers"] = new[] { "Oyuncular yükleniyor...", "Waiting for players..." },
        ["menu.playerready"]    = new[] { "Hazır", "Ready" },
        ["menu.playerwaiting"]  = new[] { "Bekleniyor", "Waiting" },

        // Ana menüdeki geri bildirim hatırlatıcısı. İki kademeli: oyuncu
        // HENÜZ oynamadıysa sakin bir davet, en az bir yarıştan sonra
        // doğrudan bir soru (söyleyecek bir şeyi olduğu an orası).
        ["menu.reminder.before"] = new[]
        {
            "Bu bir playtest — fikirlerini yazman oyunun gelişmesinin tek yolu.",
            "This is a playtest — your feedback is the only way this game improves."
        },
        ["menu.reminder.after"]  = new[]
        {
            "Nasıl geçti? Geri Bildirim'e tıklayıp yaz — 30 saniyeni alır.",
            "How did it go? Click Feedback and tell us — it takes 30 seconds."
        },

        // ─────────────── DURAKLATMA MENÜSÜ ───────────────
        ["pause.resume"]        = new[] { "Devam Et", "Resume" },
        ["pause.settings"]      = new[] { "Ayarlar", "Settings" },
        ["pause.leave"]         = new[] { "Oyundan Ayrıl", "Leave Game" },
        ["pause.quit"]          = new[] { "Oyunu Kapat", "Quit Game" },
        ["pause.back"]          = new[] { "Geri", "Back" },

        // ─────────────── AYARLAR ───────────────
        ["set.language"]        = new[] { "Dil", "Language" },
        ["set.fps"]             = new[] { "FPS Sınırı", "FPS Limit" },
        ["set.fullscreen"]      = new[] { "Tam Ekran", "Fullscreen" },
        ["set.resolution"]      = new[] { "Çözünürlük", "Resolution" },
        ["set.volume"]          = new[] { "Ses Seviyesi", "Volume" },
        ["set.sensitivity"]     = new[] { "Fare Hassasiyeti", "Mouse Sensitivity" },
        ["set.fps.unlimited"]   = new[] { "Sınırsız", "Unlimited" },

        // ─────────────── YARIŞ ───────────────
        ["race.go"]             = new[] { "BAŞLA!", "GO!" },
        ["race.finallap"]       = new[] { "SON TUR!", "FINAL LAP!" },
        ["race.lap"]            = new[] { "Tur {0}/{1}", "Lap {0}/{1}" },
        ["race.rematch"]        = new[] { "Tekrar Oyna", "Play Again" },
        ["race.hostrestarts"]   = new[] { "Host yeni yarışı başlatabilir.", "The host can start a new race." },

        // ─────────────── ROL İPUÇLARI (yarış başında, oturumda bir kez) ───────────────
        // Sağ üstteki küçük panelde görünüyor — dar bir kutu, satırlar KISA
        // tutulmalı. Uzun bir çeviri burada kutuyu taşırır.
        ["hint.racer"]          = new[]
        {
            "YARIŞÇISIN\n" +
            "W/S — gaz ve fren\n" +
            "A/D — direksiyon\n" +
            "Space — el freni\n" +
            "Turlarını bitir.\n" +
            "Sabotajcı checkpoint'lere\n" +
            "tuzak kuruyor.\n" +
            "Detay için ESC.",

            "YOU ARE A RACER\n" +
            "W/S — throttle & brake\n" +
            "A/D — steering\n" +
            "Space — handbrake\n" +
            "Finish your laps.\n" +
            "The saboteur is trapping\n" +
            "checkpoints.\n" +
            "ESC for details."
        },
        ["hint.saboteur"]       = new[]
        {
            "SABOTAJCISIN\n" +
            "1) Haritadan checkpoint seç\n" +
            "2) Skil butonuna bas\n" +
            "3) Kırmızı butonla ateşle\n" +
            "Yeşil marker = hazır\n" +
            "Kırmızı = seçili/soğuyor\n" +
            "Detay için ESC.",

            "YOU ARE THE SABOTEUR\n" +
            "1) Click a checkpoint\n" +
            "2) Press a skill button\n" +
            "3) Fire with the red button\n" +
            "Green marker = ready\n" +
            "Red = selected/cooling\n" +
            "ESC for details."
        },

        // ─────────────── SIRALAMA TABLOSU ───────────────
        ["lb.title"]            = new[] { "SIRALAMA", "STANDINGS" },
        ["lb.finished"]         = new[] { "BİTİRDİ", "FINISHED" },
        ["lb.timeup"]           = new[] { "SÜRE DOLDU", "TIME UP" },
        ["lb.player"]           = new[] { "Oyuncu", "Player" },

        // ─────────────── PODYUM / SONUÇ ───────────────
        ["end.racerswin"]       = new[] { "YARIŞÇILAR KAZANDI!", "RACERS WIN!" },
        ["end.saboteurwins"]    = new[] { "SABOTAJCI KAZANDI!", "SABOTEUR WINS!" },
        ["end.allracersleft"]   = new[] { "Tüm yarışçılar oyundan ayrıldı.\nYarış sona erdi.", "All racers left the game.\nThe race has ended." },
        ["end.saboteurleft"]    = new[] { "Sabotajcı oyundan ayrıldı.\nYarış sona erdi.", "The saboteur left the game.\nThe race has ended." },

        // ─────────────── PİST BİLGİSİ EKRANI (geri sayımdan önce) ───────────────
        // {0} km · {1} saf tur sn · {2} süre sınırı sn · {3} hedef tur sn · {4} pay sn
        ["track.info"]          = new[]
        {
            "PİST: {0} km\nSaf tur tahmini ~{1} sn · süre sınırı {2} sn\nHedef ortalama tur: {3} sn (pay {4} sn)",
            "TRACK: {0} km\nClean lap est. ~{1}s · time limit {2}s\nTarget average lap: {3}s (margin {4}s)"
        },

        // ─────────────── SABOTAJCI ───────────────
        ["sab.cpselected"]      = new[] { "Checkpoint {0} seçildi.", "Checkpoint {0} selected." },
        ["sab.needcp"]          = new[] { "önce minimapten bir checkpoint seç", "select a checkpoint on the minimap first" },
        ["sab.notready"]        = new[] { "{0} henüz hazır değil (buton sönük)", "{0} is not ready yet (button is dim)" },
        ["sab.cpcooldown"]      = new[] { "checkpoint {0} az önce tuzaklandı (marker henüz yeşile dönmedi)", "checkpoint {0} was just trapped (marker has not turned green yet)" },
        ["sab.activated"]       = new[] { "{0} AKTİF!", "{0} ACTIVE!" },
        ["sab.oncooldown"]      = new[] { "{0} henüz hazır değil (bekleme süresinde).", "{0} is not ready yet (on cooldown)." },

        // ─────────────── YETENEK İSİMLERİ ───────────────
        ["skill.icebomb"]       = new[] { "Buz Bombası", "Ice Bomb" },
        ["skill.chicken"]       = new[] { "Tavuk Sürüsü", "Chicken Flock" },
        ["skill.engine"]        = new[] { "Motor Arızası", "Engine Failure" },

        // ─────────────── OYUN İÇİ UYARILAR ───────────────
        ["warn.enginefailure"]  = new[] { "MOTOR ARIZASI", "ENGINE FAILURE" },
        ["warn.caught"]         = new[] { "{0} yakalandı", "{0} got caught" },
        ["warn.spectatorbound"] = new[] { "İzleyici alanının sınırındasın.", "You have reached the edge of the spectator area." },

        // ─────────────── BAĞLANTI / STEAM ───────────────
        ["net.disconnected"]    = new[] { "Bağlantı koptu", "Connection lost" },
        ["net.creatinglobby"]   = new[] { "Oyun kuruluyor...", "Creating game..." },
        ["net.searching"]       = new[] { "Oyun aranıyor...", "Searching for a game..." },
        ["net.nogamefound"]     = new[] { "Açık oyun bulunamadı — senin oyunun kuruldu, başkaları katılabilir.", "No open game found — yours has been created, others can join." },
        ["net.joinfailed"]      = new[] { "Oyuna katılınamadı — oyun dolmuş ya da başlamış olabilir.", "Could not join — the game may be full or already started." },
        ["net.leavefailed"]     = new[] { "Önceki oyundan çıkılamadı, davete katılınamadı.", "Could not leave the previous game, so the invite could not be accepted." },

        // ─────────────── NASIL OYNANIR PANELİ ───────────────
        ["howto.title"]         = new[] { "NASIL OYNANIR", "HOW TO PLAY" },
        ["howto.content"]       = new[]
        {
            "SaboTour asimetrik bir yarış oyunu: bir oyuncu SABOTAJCI, geri kalan herkes YARIŞÇI.\n\n" +
            "KAZANMA\n" +
            "• Yarışçılar turlarını bitirirse yarışçılar kazanır.\n" +
            "• Süre dolmadan kimse bitiremezse sabotajcı kazanır.\n\n" +
            "YARIŞÇI KONTROLLERİ\n" +
            "• W / S — gaz ve fren\n" +
            "• A / D — direksiyon\n" +
            "• Space — el freni (drift)\n" +
            "• ESC — menü\n" +
            "Pistten çok uzaklaşırsan ya da bir checkpoint'i atlarsan otomatik olarak geri ışınlanırsın.\n\n" +
            "SABOTAJCI KONTROLLERİ\n" +
            "• W A S D — yürü, Shift — koş, Space — zıpla\n" +
            "• Fare — bak, Sol tık — etkileşim\n" +
            "• ESC — imleci serbest bırak / menü\n\n" +
            "SABOTAJCI NE YAPAR\n" +
            "Kulenin içindeki masada pistin haritası var. Sırayla:\n" +
            "1. Haritadan bir checkpoint'e tıkla (seçilince kırmızı olur).\n" +
            "2. Bir skil butonuna bas (seçili buton basılı kalır).\n" +
            "3. Büyük kırmızı butona basıp tuzağı ateşle.\n\n" +
            "Buton basılı ve SÖNÜKSE o skil şarj oluyordur — üzerine bakınca kalan saniye görünür. " +
            "Yeşil marker hazır checkpoint, kırmızı marker seçili ya da az önce tuzaklanmış demektir.\n\n" +
            "ÜÇ SKİL\n" +
            "• Buz Bombası (mavi) — checkpoint'e bomba düşer, araçları savurur ve yeri kayganlaştırır.\n" +
            "• Tavuk Sürüsü (sarı) — piste tavuklar salar, çarpan yavaşlar.\n" +
            "• Motor Arızası (turuncu) — o checkpoint'ten geçen İLK araç birkaç saniye güç kaybeder.",

            "SaboTour is an asymmetric racing game: one player is the SABOTEUR, everyone else is a RACER.\n\n" +
            "WINNING\n" +
            "• If the racers finish their laps, the racers win.\n" +
            "• If nobody finishes before time runs out, the saboteur wins.\n\n" +
            "RACER CONTROLS\n" +
            "• W / S — throttle and brake\n" +
            "• A / D — steering\n" +
            "• Space — handbrake (drift)\n" +
            "• ESC — menu\n" +
            "If you stray too far from the track or miss a checkpoint, you are teleported back automatically.\n\n" +
            "SABOTEUR CONTROLS\n" +
            "• W A S D — walk, Shift — sprint, Space — jump\n" +
            "• Mouse — look, Left click — interact\n" +
            "• ESC — release the cursor / menu\n\n" +
            "WHAT THE SABOTEUR DOES\n" +
            "There is a map of the track on the table inside the tower. In order:\n" +
            "1. Click a checkpoint on the map (it turns red once selected).\n" +
            "2. Press a skill button (the selected button stays pressed down).\n" +
            "3. Hit the big red button to fire the trap.\n\n" +
            "If a button is pressed down and DIM, that skill is recharging — look at it to see the seconds left. " +
            "A green marker means the checkpoint is ready; a red marker means it is selected or was just trapped.\n\n" +
            "THE THREE SKILLS\n" +
            "• Ice Bomb (blue) — a bomb lands on the checkpoint, throwing cars aside and making the ground slippery.\n" +
            "• Chicken Flock (yellow) — releases chickens onto the track; anyone who hits them slows down.\n" +
            "• Engine Failure (orange) — the FIRST car through that checkpoint loses power for a few seconds."
        },

        // ─────────────── GERİ BİLDİRİM PANELİ ───────────────
        ["fb.title"]            = new[] { "Geri Bildirim", "Feedback" },
        ["fb.paneltitle"]       = new[] { "GERİ BİLDİRİM", "FEEDBACK" },
        ["fb.info"]             = new[]
        {
            "Ne beğendin, ne bozuk, ne eksik? Yazdıkların doğrudan geliştiriciye gider.\nPist numarası, rolün ve teknik bilgiler otomatik ekleniyor — yazmana gerek yok.",
            "What did you like, what is broken, what is missing? Your words go straight to the developer.\nTrack number, your role and technical details are added automatically — no need to type them."
        },
        ["fb.nameLabel"]        = new[] { "İsmin (isteğe bağlı)", "Your name (optional)" },
        ["fb.messageplaceholder"] = new[] { "Buraya yaz…", "Write here…" },
        ["fb.messageLabel"]     = new[] { "Ne düşünüyorsun?", "What do you think?" },
        ["fb.send"]             = new[] { "Gönder", "Send" },
        ["fb.sending"]          = new[] { "Gönderiliyor...", "Sending..." },
        ["fb.sent"]             = new[] { "Teşekkürler, ulaştı!", "Thank you, it went through!" },
        ["fb.empty"]            = new[] { "Önce bir şeyler yaz.", "Please write something first." },
        ["fb.cooldown"]         = new[] { "Az önce gönderdin — {0} saniye sonra tekrar deneyebilirsin.", "You just sent one — you can try again in {0} seconds." },

        // Gönderim hataları. Parantez içindeki "(Geliştirici: ...)" kısımları
        // BİLEREK korundu: playtest'çi ekran görüntüsü attığında sorunun
        // nerede olduğunu doğrudan görüyoruz.
        ["fb.err.closed"]       = new[]
        {
            "Geri bildirim şu an kapalı görünüyor. (Geliştirici: formun yayında ve yanıta açık olduğunu kontrol et.)",
            "Feedback appears to be closed right now. (Developer: check that the form is published and accepting responses.)"
        },
        ["fb.err.notfound"]     = new[]
        {
            "Gönderim adresi bulunamadı. (Geliştirici: Form Url yanlış.)",
            "Submission address not found. (Developer: the Form Url is wrong.)"
        },
        ["fb.err.rejected"]     = new[]
        {
            "Gönderi reddedildi. (Geliştirici: entry kimliklerini kontrol et.)",
            "Submission was rejected. (Developer: check the entry IDs.)"
        },
        ["fb.err.network"]      = new[]
        {
            "Gönderilemedi — internet bağlantını kontrol edip tekrar dener misin?",
            "Could not send — please check your internet connection and try again."
        },
    };

    /// <summary>
    /// Anahtarın karşılığını GÜNCEL dilde döndürüyor.
    /// Değişkenli metinler için: Loc.T("race.lap", 2, 3)
    /// </summary>
    public static string T(string key, params object[] args)
    {
        if (!Table.TryGetValue(key, out string[] values))
        {
            // Eksik anahtar SESSİZCE geçilmiyor: anahtarın kendisi ekranda
            // görünüyor ("race.finallap" gibi). Çirkin ama fark edilir —
            // sessiz bir boş string, eksik çevirinin aylarca gözden
            // kaçmasına sebep olurdu.
            Debug.LogWarning($"[Loc] Çeviri anahtarı bulunamadı: '{key}'");
            return key;
        }

        int index = (int)GameLanguage.Current;

        // Bir dilin karşılığı eksikse Türkçe'ye (0. sütun) düşüyor.
        // Boş yazı göstermektense yanlış dilde göstermek daha iyi.
        string result = (index < values.Length && !string.IsNullOrEmpty(values[index]))
            ? values[index]
            : values[0];

        return (args != null && args.Length > 0) ? string.Format(result, args) : result;
    }

    /// <summary>
    /// Yetenek adının çevirisi. Enum'un kendi adı (IceBomb, ChickenFlock)
    /// ekranda ham hâliyle gösterilmemeli — İngilizce'de bile "ChickenFlock"
    /// bitişik ve çirkin görünüyor.
    /// </summary>
    public static string Skill(SkillType skill)
    {
        switch (skill)
        {
            case SkillType.IceBomb: return T("skill.icebomb");
            case SkillType.ChickenFlock: return T("skill.chicken");
            case SkillType.EngineFailure: return T("skill.engine");
            default: return skill.ToString();
        }
    }

    /// <summary>Bu anahtar sözlükte var mı? (Editor araçları / tarama için.)</summary>
    public static bool Has(string key) => Table.ContainsKey(key);

    /// <summary>Sözlükteki tüm anahtarlar — eksik çeviri taraması yapan Editor aracı için.</summary>
    public static IEnumerable<string> AllKeys => Table.Keys;
}
