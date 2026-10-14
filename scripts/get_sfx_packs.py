"""Exult's sound effect packs into assets/sfx.

usage: python scripts/get_sfx_packs.py [--zip <exult_audio.zip>]

Downloads Exult's all-in-one audio pack (the exult_audio.zip Exult's Windows installer
fetches, checked against the installer's SHA-256) unless --zip names a copy, and extracts
the four sound effect packs with their readmes: sqsfxbg.flx and sqsfxsi.flx (Simon Quinn,
recorded from a Roland MT-32) and jmsfx.flx and jmsisfx.flx (Joseph Morris, from a
SoundBlaster). The zip's digital music is left in it. The packs are recordings of the
originals' sounds: assets/ is never committed.
"""
import hashlib, os, sys, tempfile, urllib.request, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
URL = "https://downloads.sourceforge.net/project/exult/exult-data/exult_audio.zip"
# exult/exult-1.12.1/win32/exult_installer.iss
SHA256 = "72e10efa8664a645470ceb99f6b749ce99c3d5fd1c8387c63640499cfcdbbc68"
WANTED = ["sqsfxbg.flx", "sqsfxsi.flx", "jmsfx.flx", "jmsisfx.flx",
          "README_audiopack.txt", "readme_sqsfxbg.txt", "readme_sqsfxsi.txt",
          "readme_jmsfx.txt", "readme_jmsisfx.txt"]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    args = sys.argv[1:]
    with tempfile.TemporaryDirectory() as tmp:
        if args[:1] == ["--zip"] and len(args) == 2:
            path = args[1]
        else:
            path = os.path.join(tmp, "exult_audio.zip")
            print(f"downloading {URL}")
            urllib.request.urlretrieve(URL, path)
        digest = sha256(path)
        if digest != SHA256:
            sys.exit(f"{path}: SHA-256 {digest}, not Exult's {SHA256}")
        out = os.path.join(ROOT, "assets", "sfx")
        os.makedirs(out, exist_ok=True)
        with zipfile.ZipFile(path) as z:
            for name in WANTED:
                # Only these names, written by name: nothing in the zip picks a path.
                data = z.read(name)
                with open(os.path.join(out, name), "wb") as f:
                    f.write(data)
                print(f"  {name} ({len(data)} bytes)")
    print(f"packs in {out}")


if __name__ == "__main__":
    main()
