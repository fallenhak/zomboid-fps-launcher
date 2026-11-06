"""Burak'ın oyun içi ayarlarını launcher'ın hazır ayarlarına (src/Zfl.Core/Defaults) kopyalar.
Kullanım (oyun kapalıyken): python -I tools/hazir-ayarlari-al.py ["C:/Zomboid FPS/Kullanici/Zomboid"]
Bilgisayara özel satırlar (çözünürlük, dil, mikrofon) alınmaz; launcher onları her makinede kendisi yazar."""
import os, sys

SRC = sys.argv[1] if len(sys.argv) > 1 else r"C:\Zomboid FPS\Kullanici\Zomboid"
DST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Zfl.Core", "Defaults")

FILES = [
    "options.ini", "viewpoint-live.properties", "viewpoint-frame-caps.properties", "rolling-hills.properties",
    "vramsteady.properties", "MvdoxGrass_HiddenPlants.txt", "ViewpointTrueBallistics/settings.ini",
    "Lua/ModOptions.ini", "Lua/keysB42.ini", "Lua/layout.ini", "Lua/AttachmentsUI-options-v1.txt",
    "Lua/ViewpointBloodFXAddon.ini", "Lua/alife_menu_prefs.ini",
    "Lua/MvdoxGrass_LastUpdateNotice.txt", "Lua/MvdoxGrass_UI.txt", "Lua/MvdoxGrass_WelcomeSeen.txt",
]
# options.ini'de makineye özel anahtarlar
MACHINE = {"borderless", "fullScreen", "width", "height", "language", "voiceRecordDeviceName", "seenNews", "doneNewSaveFolder"}

for rel in FILES:
    src = os.path.join(SRC, rel)
    if not os.path.exists(src):
        print("yok, atlandı:", rel); continue
    text = open(src, "rb").read().decode("utf-8")
    if rel == "options.ini":
        text = "".join(l for l in text.splitlines(True) if l.split("=", 1)[0].strip() not in MACHINE)
    name = rel.replace("/", "__")
    open(os.path.join(DST, name), "wb").write(text.encode("utf-8"))
    print("alındı:", name)
