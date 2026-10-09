# Zomboid FPS Launcher — tasarım (v1)

Onay: Burak, 2026-10-09 (Bölüm 1–4 ayrı ayrı onaylandı, görsel taslak v4 onaylandı).

## Amaç
Project Zomboid'i Project Viewpoint (ellu & norkus) ile birinci şahıs oynamak için tek tıkla kurulum.
Launcher, Steam'deki oyunu ayrı bir klasöre kopyalar, ZombieBuddy + Viewpoint + temel paketi resmi
Steam Workshop'tan indirir, bellek ayarını yapar ve kopyayı ana Zomboid'den tamamen izole çalıştırır.

- Kitle: önce arkadaş grubu, sonra herkese açık.
- Kapsam dışı (v1): sunucu/çok oyunculu, giriş sistemi, Workshop'ta isimle arama, Mac/Linux.
- Teknoloji: WPF (.NET), tek exe, Windows.

## Kurallar (değişmez)
- **Tam izolasyon:** kopyanın kayıtları, ayarları, modları, ZombieBuddy onayları ve Steam abonelikleri ana Zomboid'den ayrı.
- **Dağıtım:** Launcher hiçbir mod dosyasını içinde taşımaz, değiştirmez. Her mod kullanıcının bilgisayarına doğrudan resmi Workshop'tan iner (Viewpoint Discord kural 6, 7, 10).
- **Korsan yok:** Steam'de kurulu oyun bulunmadan kurulum yapılmaz.
- **Ad:** "Zomboid FPS Launcher". Viewpoint adı/logosu ürün adı olarak kullanılmaz; "Project Viewpoint (ellu & norkus) kullanır" yazılır. Herkese açık sürümde "Project Zomboid'in resmi ürünü değildir" notu.

## Denenmiş teknik gerçekler (2026-10-09)
- Kopya Steam klasörü dışında `ProjectZomboid64.exe` ile açılıyor.
- `ProjectZomboid64.json` → `vmArgs`:
  - `-Xmx<N>m` bellek
  - `-Dzomboid.steam=0` → Steam'siz mod (abonelikler sızmaz). Bedeli: Steam MP yok (v1 kapsam dışı).
  - `-Duser.home=<Kurulum>/Kullanici` → veri `<Kurulum>/Kullanici/Zomboid`. (`-Ddeployment.user.cachedir` çalışmıyor.)
  - `-agentlib:zbNative=config_dir=<Kurulum>/zombie_buddy` (json'da `--` gerekmez).
- ZombieBuddy: `libs/zbNative.dll` ve `libs/ZombieBuddy.jar` oyun klasörüne; mod klasörü `Zomboid/mods/ZombieBuddy`.
- **ZB 2.3.4 hatası:** `mod.info`'dan 5 üst klasör yukarı çıkıp dosya adı okuyor; mod klasörü sürücü köküne yakınsa NPE. `Kullanici` katmanı bunu çözer. Launcher kurulum yolunu doğrular (mod.info'dan 6 üst klasör var olmalı).
- SteamCMD: `+login anonymous +workshop_download_item 108600 <id>`. İlk çalıştırma kendini güncelleyip çıkar.
- Workshop içerik düzeni: `<id>/mods/<ModAdı>/{42|common}/mod.info`.

## Akış
1. **İlk açılış:** Steam'deki PZ'yi bul (Steam kayıt defteri + `libraryfolders.vdf`). Bulunamazsa açıklama + elle klasör seçimi. Kurulum klasörünü sor, gereken alanı ve boş alanı göster.
2. **Kurulum** (kesilirse kaldığı yerden sürer, adımlar `state.json`'da):
   1. Oyunu kopyala.
   2. SteamCMD'yi indir ve hazırla (`<Kurulum>/araclar/steamcmd`).
   3. Feed'deki temel paketi + kullanıcı modlarını indir.
   4. ZombieBuddy dosyalarını oyun klasörüne koy.
   5. Modları `Kullanici/Zomboid/mods`'a kopyala.
   6. `ProjectZomboid64.json`'u yaz (orijinal `.orijinal` olarak saklanır).
   7. Aktif mod listesini oyuna yaz (`Kullanici/Zomboid/mods/default.txt`).
3. **Oyna:** Steam sürümü ile kopyayı karşılaştır, değişen dosyaları kopyala. Oyun sürümü değiştiyse uyar ("Viewpoint bu sürümü desteklemiyor olabilir", seçim kullanıcıda). Mod güncellemelerini indir. Oyunu aç.
4. **Bellek:** sistem RAM'inin yarısı, en az 4 GB, en çok 12 GB; Ayarlar'da kaydırıcı.

## Bileşenler
| Bileşen | Görev |
|---|---|
| `SteamLocator` | Steam ve PZ kurulumunu bulur, oyun sürümünü okur. |
| `GameMirror` | Kopyalama ve fark güncellemesi (boyut + değişme zamanı, şüphede hash). |
| `LaunchConfigWriter` | `ProjectZomboid64.json` vmArgs'ını yazar. |
| `SteamCmd` | SteamCMD kurulumu, Workshop indirme, hata/yeniden deneme. |
| `WorkshopInfo` | `ISteamRemoteStorage/GetPublishedFileDetails` (anahtarsız): ad, boyut, güncelleme zamanı. |
| `ModInspector` | `mod.info` okur: id, require, versionMin/Max, javaJarFile. |
| `ModInstaller` | Modları mods klasörüne yerleştirir, ZB dosyalarını oyun klasörüne koyar, mod listesini yazar. |
| `Feed` | GitHub'daki `feed.json`: temel paket Workshop id'leri, desteklenen oyun sürümü, launcher sürümü. Mod dosyası yok, yalnız id. |
| `Launcher` | Oyunu başlatır, hemen kapanırsa "Hata bildir"e yönlendirir. |
| `BugReport` | Viewpoint, oyun ve ZB sürümü, GPU+sürücü, mod listesini panoya kopyalar; console.txt klasörünü açar. |
| `SelfUpdate` | Feed'deki launcher sürümüne göre kendini günceller. |

## Mod ekle/çıkar
- Workshop bağlantısı ya da numarası yapıştırılır → `WorkshopInfo` ad/boyut gösterir → "Ekle" → indir → `ModInspector` kontrolleri:
  - sürüm uyumsuz → uyarı; Java modu → güvenlik uyarısı; `require` eksik → hangi mod gerektiğini söyler.
- Kullanıcı modları kaldırılabilir veya kapatılabilir; temel paket kilitli.
- Yükleme sırası: temel paket (feed sırası), sonra kullanıcı modları eklenme sırasıyla.
- v1'de isimle arama yok (Steam Web API anahtarı gerekir) → v2.

## Arayüz
Onaylı taslak: `tasarim/launcher-taslak.html` (notlar klasöründe). Oyun içi panel dili: düz koyu zemin,
ince gri çerçeve, başlık şeridi, sekmeler (Oyna, Modlar, Ayarlar, Hata bildir), satır listeleri, efekt yok.
Font Noto Sans. Alt satırda Viewpoint atfı.

## Hata durumları
| Durum | Davranış |
|---|---|
| Steam'de PZ yok | Açıklama + elle klasör seçimi |
| Disk yetersiz | Kurulum başlamaz, gereken alan gösterilir |
| İnternet/Steam yok | Mevcut modlarla açılır, "güncellemeler kontrol edilemedi" |
| Mod inmedi | Kırmızı işaret + yeniden deneme; temel paket eksikse oyun açılmaz |
| Oyun sürümü değişti | Uyarı, seçim kullanıcıda |
| Oyun hemen kapandı | Kısa açıklama + Hata bildir ekranı |
| Kurulum kesildi | `state.json`'dan devam |
| Yeni launcher sürümü | Açılışta haber + tek tık güncelleme |

## Test
- Birim testleri (xUnit): sürüm karşılaştırma, json yazımı, mod.info ayrıştırma, Java modu tanıma, yol derinliği kontrolü, mod sırası, feed okuma.
- Uçtan uca: Burak oyun oynamıyorken gerçek kurulum.
- Dağıtım: önce 1–2 arkadaş, sonra grup.

## Açık konular
- SteamCMD anonim indirme herkese açık sürümden önce Viewpoint geliştiricilerine sorulmalı (kural 10 gri alan).
- ZB 2.3.4 klasör derinliği hatası ZombieBuddy'ye bildirilebilir.
