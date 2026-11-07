"""Burak'ın mod listesini herkese dağıtır.

  python -I tools/mod-paketi.py liste
      feed.json'a Burak'ın listesini, sırasını ve kapalı modlarını yazar (state.json'dan). Commit + push sonrası
      herkesin launcher'ı açılışta aynı değişikliği yapar (oyuncunun kendi eklediği/kapattığı modlar kalır).

  python -I tools/mod-paketi.py paket [çıkış.zip]
      İlk kurulum paketi: listedeki modların klasörleri (mods/...), elle konmuş kopyalar (yerel-modlar/..., ör.
      Viewpoint nightly) ve paket.json (her öğenin Atölye güncellenme zamanı + klasörleri). Oyun kapalıyken çalıştır.

  python -I tools/mod-paketi.py yayinla <Drive bağlantısı> [paket.zip]
      Zip Drive'a yüklenip bağlantısı "bağlantıya sahip herkes" yapıldıktan sonra feed.json'a pack alanını yazar.
"""
import json, os, sys, time, zipfile

REPO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
FEED = os.path.join(REPO, "feed.json")
STATE = os.path.join(os.environ["LOCALAPPDATA"], "ZomboidFpsLauncher", "state.json")
DEFAULT_ZIP = os.path.join(os.path.expanduser("~"), "Desktop", "zomboid-mod-paketi.zip")


def load(p):
    with open(p, encoding="utf-8-sig") as f:
        return json.load(f)


def save_feed(feed):
    with open(FEED, "w", encoding="utf-8", newline="\n") as f:
        json.dump(feed, f, ensure_ascii=False, indent=2)
        f.write("\n")


def ci(d, key):
    """state.json alan adları büyük harfle başlıyor; yine de harf farkına takılma."""
    return next((v for k, v in d.items() if k.lower() == key.lower()), None)


def liste():
    state, feed = load(STATE), load(FEED)
    base = {m["workshopId"] for m in feed["baseMods"]}
    off_items = set(ci(state, "DisabledMods") or [])
    # Atölye'de olmayan, yalnız Burak'ın bilgisayarındaki modlar (ör. KnoxMap haritası) dağıtılmaz
    on_workshop = {int(k) for k in (ci(state, "ModUpdated") or {})}
    local_only = [i for i in ci(state, "UserMods") if i not in on_workshop]
    mods = [i for i in ci(state, "UserMods") if i not in base and i not in off_items and i in on_workshop]
    if local_only:
        print(f"yalnız bu bilgisayardaki {len(local_only)} mod listeye alınmadı: {local_only}")
    feed["listMods"] = mods
    feed["modOrder"] = ci(state, "ModOrder")
    feed["disabledModIds"] = ci(state, "DisabledModIds")
    save_feed(feed)
    print(f"feed.json: {len(mods)} mod, {len(feed['modOrder'])} sıra, {len(feed['disabledModIds'])} kapalı mod")
    if off_items:
        print(f"tamamen kapalı {len(off_items)} öğe listeye alınmadı: {sorted(off_items)}")


def item_folders(root, item):
    """Atölye öğesindeki mod klasörleri: önce elle konmuş kopya, sonra SteamCMD indirmesi."""
    for d in (os.path.join(root, "yerel-modlar", str(item)),
              os.path.join(root, "araclar", "steamcmd", "steamapps", "workshop", "content", "108600", str(item))):
        mods = os.path.join(d, "mods")
        if os.path.isdir(mods):
            names = [n for n in os.listdir(mods) if os.path.isdir(os.path.join(mods, n))]
            if names:
                return d, names
    return None, []


def paket(out):
    state, feed = load(STATE), load(FEED)
    root = ci(state, "InstallRoot")
    mods_dir = os.path.join(root, "Kullanici", "Zomboid", "mods")
    updated = {int(k): v for k, v in (ci(state, "ModUpdated") or {}).items()}
    ids = list(dict.fromkeys([m["workshopId"] for m in feed["baseMods"]] + (feed.get("listMods") or [])))
    items, folders, local = {}, [], []
    for i in ids:
        src, names = item_folders(root, i)
        names = [n for n in names if os.path.isdir(os.path.join(mods_dir, n))
                 and not os.path.exists(os.path.join(mods_dir, n + ".kuruluyor"))]
        if not names:
            print("kurulu değil, pakete girmedi:", i)
            continue
        if i not in updated:
            print("güncellenme zamanı bilinmiyor (ilk açılışta Atölye'den yeniden iner):", i)
        items[str(i)] = {"updated": updated.get(i, 0), "folders": names}
        folders += names
        if src and os.path.basename(os.path.dirname(src)) == "yerel-modlar":
            local.append(src)
    manifest = {"created": int(time.time()), "items": items}
    tmp = out + ".yaziliyor"
    count = 0
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
        z.writestr("paket.json", json.dumps(manifest, ensure_ascii=False, indent=1))
        for base, prefix, names in [(mods_dir, "mods", folders)] + [(os.path.dirname(d), "yerel-modlar", [os.path.basename(d)]) for d in local]:
            for n in names:
                for dp, _, fs in os.walk(os.path.join(base, n)):
                    for f in fs:
                        full = os.path.join(dp, f)
                        z.write(full, prefix + "/" + os.path.relpath(full, base).replace("\\", "/"))
                        count += 1
                        if count % 5000 == 0:
                            print(f"  {count} dosya...")
    os.replace(tmp, out)
    print(f"paket: {out} — {len(items)} öğe, {count} dosya, {os.path.getsize(out) / 2**30:.2f} GB")


def yayinla(url, zip_path):
    feed = load(FEED)
    with zipfile.ZipFile(zip_path) as z:
        created = json.loads(z.read("paket.json"))["created"]
    feed["pack"] = {"url": url, "sizeBytes": os.path.getsize(zip_path), "id": time.strftime("%Y-%m-%d-%H%M", time.localtime(created))}
    save_feed(feed)
    print("feed.json pack:", feed["pack"])


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "liste":
        liste()
    elif cmd == "paket":
        paket(sys.argv[2] if len(sys.argv) > 2 else DEFAULT_ZIP)
    elif cmd == "yayinla" and len(sys.argv) > 2:
        yayinla(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else DEFAULT_ZIP)
    else:
        print(__doc__)
