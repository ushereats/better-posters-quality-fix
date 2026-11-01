"""Adds a release to manifest.json (newest first) and drops entries whose download no longer exists.
python3 ci/manifest.py <version> <zip path> <download url> <changelog>"""
import hashlib, json, sys, datetime, urllib.request

version, zip_path, url, changelog = sys.argv[1:5]
GUID = "066ff97a-0628-4d1d-b256-840f1e25f8df"
m = json.load(open("manifest.json"))
p = [x for x in m if x["guid"] == GUID][0]
p["description"] = ("Better Posters from btttr.cc with quality badges read from your actual files (4K / 1080p, HDR, HDR10+, "
                    "Dolby Vision, Dolby Atmos), drawn in the Ushereats theme's style. New titles get their poster straight away.\n")
p["overview"] = "Better Posters with accurate quality badges from your own files"
md5 = hashlib.md5(open(zip_path, "rb").read()).hexdigest()
entry = {"version": version, "changelog": changelog, "targetAbi": "12.0.0.0", "sourceUrl": url, "checksum": md5,
         "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")}

def alive(u):
    try:
        req = urllib.request.Request(u, method="HEAD")
        return urllib.request.urlopen(req, timeout=20).status < 400
    except Exception:
        return False

old = [v for v in p["versions"] if v["version"] != version and alive(v["sourceUrl"])][:4]
p["versions"] = [entry] + old
json.dump(m, open("manifest.json", "w"), indent=4)
print(json.dumps(p["versions"], indent=1))
