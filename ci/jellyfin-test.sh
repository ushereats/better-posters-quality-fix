#!/usr/bin/env bash
# Real Jellyfin 12 on the CI machine: a tiny library (a 4K HDR10 + Dolby Digital film, a 1080p film, a show),
# the plugin installed (from a zip, or from a plugin repository like a user would), the poster task run,
# and the resulting posters saved to $OUT so they can be looked at.
#   ci/jellyfin-test.sh zip <plugin.zip> <outdir>
#   ci/jellyfin-test.sh repo <manifest url> <outdir>
set -eo pipefail
MODE="$1"; SRC="$2"; OUT="$3"
mkdir -p "$OUT"
J=http://localhost:8096
AUTH='MediaBrowser Client="ci", Device="ci", DeviceId="ci-bp", Version="1.0"'
GUID=066ff97a-0628-4d1d-b256-840f1e25f8df

echo "== Jellyfin"
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://repo.jellyfin.org/jellyfin_team.gpg.key | sudo gpg --dearmor -o /etc/apt/keyrings/jellyfin.gpg
echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/jellyfin.gpg] https://repo.jellyfin.org/ubuntu $(lsb_release -cs) main" | sudo tee /etc/apt/sources.list.d/jellyfin.list >/dev/null
sudo apt-get update -qq
sudo apt-get install -y -qq jellyfin >/dev/null
sudo systemctl stop jellyfin || true
dpkg -l | grep -i jellyfin-server | awk '{print $2, $3}' | tee "$OUT/jellyfin-version.txt"
FF=/usr/lib/jellyfin-ffmpeg/ffmpeg

echo "== library"
sudo mkdir -p /srv/media/movies /srv/media/shows && sudo chown -R "$USER" /srv/media
M1="/srv/media/movies/The Shawshank Redemption (1994)"; mkdir -p "$M1"
$FF -v error -y -f lavfi -i "testsrc2=s=3840x2160:d=1:r=24" -f lavfi -i "anullsrc=r=48000:cl=5.1" -t 1 \
  -c:v libx264 -preset ultrafast -profile:v high10 -pix_fmt yuv420p10le \
  -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc -c:a eac3 -b:a 384k "$M1/The Shawshank Redemption (1994).mkv"
printf '<movie><title>The Shawshank Redemption</title><year>1994</year><imdbid>tt0111161</imdbid><uniqueid type="imdb" default="true">tt0111161</uniqueid></movie>' > "$M1/movie.nfo"
M2="/srv/media/movies/Inception (2010)"; mkdir -p "$M2"
$FF -v error -y -f lavfi -i "testsrc2=s=1920x800:d=1:r=24" -f lavfi -i "anullsrc=r=48000:cl=stereo" -t 1 -c:v libx264 -preset ultrafast -c:a aac "$M2/Inception (2010).mkv"
printf '<movie><title>Inception</title><year>2010</year><imdbid>tt1375666</imdbid><uniqueid type="imdb" default="true">tt1375666</uniqueid></movie>' > "$M2/movie.nfo"
S="/srv/media/shows/Breaking Bad (2008)"; mkdir -p "$S/Season 01"
printf '<tvshow><title>Breaking Bad</title><year>2008</year><imdbid>tt0903747</imdbid><uniqueid type="imdb" default="true">tt0903747</uniqueid></tvshow>' > "$S/tvshow.nfo"
$FF -v error -y -f lavfi -i "testsrc2=s=1280x720:d=1:r=24" -f lavfi -i "anullsrc=r=48000:cl=stereo" -t 1 -c:v libx264 -preset ultrafast -c:a aac "$S/Season 01/Breaking Bad S01E01.mkv"
sudo chmod -R a+rX /srv/media

echo "== plugin"
PLUG=/var/lib/jellyfin/plugins
if [ "$MODE" = zip ]; then
  sudo mkdir -p "$PLUG/BetterPostersQualityFix_ci" && sudo unzip -o -q "$SRC" -d "$PLUG/BetterPostersQualityFix_ci"
  sudo chown -R jellyfin:jellyfin "$PLUG"
fi

start() {
  sudo systemctl start jellyfin
  for i in $(seq 1 120); do curl -fs $J/System/Info/Public >/dev/null 2>&1 && return 0; sleep 1; done
  sudo journalctl -u jellyfin --no-pager | tail -50; exit 1
}
api() { # method path [json]
  local m=$1 p=$2 d=${3:-} i out rc
  for i in $(seq 1 60); do
    if [ -n "$d" ]; then out=$(curl -sS -f -X "$m" "$J$p" -H "Authorization: $AUTH${TOKEN:+, Token=\"$TOKEN\"}" -H 'Content-Type: application/json' -d "$d" 2>&1) && rc=0 || rc=$?
    else out=$(curl -sS -f -X "$m" "$J$p" -H "Authorization: $AUTH${TOKEN:+, Token=\"$TOKEN\"}" 2>&1) && rc=0 || rc=$?; fi
    if [ $rc -eq 0 ]; then printf '%s' "$out"; return 0; fi
    if [ $rc -ne 7 ] && [ $rc -ne 52 ] && [ $rc -ne 56 ] && ! echo "$out" | grep -q " 503"; then echo "API $m $p failed: $out" >&2; return $rc; fi
    sleep 1
  done
  return 7
}
start

echo "== setup"
TOKEN=""
api POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
api GET /Startup/User >/dev/null || true
api POST /Startup/User '{"Name":"ci","Password":"ci"}' >/dev/null
api POST /Startup/Complete >/dev/null
TOKEN=$(api POST /Users/AuthenticateByName '{"Username":"ci","Pw":"ci"}' | python3 -c "import json,sys; print(json.load(sys.stdin)['AccessToken'])")

if [ "$MODE" = repo ]; then
  echo "== install from the plugin repository: $SRC"
  api POST /Repositories "[{\"Name\":\"Better Posters QF\",\"Url\":\"$SRC\",\"Enabled\":true}]" >/dev/null
  api GET "/Packages" > "$OUT/packages.json"
  VER=$(python3 -c "import json; p=[x for x in json.load(open('$OUT/packages.json')) if x['guid']=='$GUID'][0]; print(p['versions'][0]['version'])")
  echo "newest in catalog: $VER" | tee "$OUT/install.txt"
  api POST "/Packages/Installed/Better%20Posters%20(Local%20Quality%20Fix)?assemblyGuid=$GUID&version=$VER" >/dev/null
  for i in $(seq 1 60); do ls $PLUG 2>/dev/null | grep -qi "Better" && break; sleep 2; done
  ls -la $PLUG | tee -a "$OUT/install.txt"
  sudo systemctl restart jellyfin; sleep 3; start
fi

api GET /Plugins | python3 -c "import json,sys; [print(p['Name'], p['Version'], p['Status']) for p in json.load(sys.stdin)]" | tee "$OUT/plugins.txt"
grep -q "Local Quality Fix).* Active" "$OUT/plugins.txt" || { echo "::error::plugin not active"; sudo journalctl -u jellyfin --no-pager | grep -i -B2 -A10 "better\|exception" | tail -80; exit 1; }

echo "== libraries"
api POST "/Library/VirtualFolders?name=Movies&collectionType=movies&paths=%2Fsrv%2Fmedia%2Fmovies&refreshLibrary=false" '{"LibraryOptions":{}}' >/dev/null
api POST "/Library/VirtualFolders?name=Shows&collectionType=tvshows&paths=%2Fsrv%2Fmedia%2Fshows&refreshLibrary=false" '{"LibraryOptions":{}}' >/dev/null
api POST /Library/Refresh >/dev/null
UID_=$(api GET /Users | python3 -c "import json,sys; print(json.load(sys.stdin)[0]['Id'])")
for i in $(seq 1 120); do
  N=$(api GET "/Items?userId=$UID_&Recursive=true&IncludeItemTypes=Movie,Series&HasImdbId=true&Limit=0" | python3 -c "import json,sys; print(json.load(sys.stdin)['TotalRecordCount'])")
  RUN=$(api GET "/ScheduledTasks?isHidden=false" | python3 -c "import json,sys; print(sum(1 for t in json.load(sys.stdin) if t.get('State')=='Running'))")
  echo "titles=$N running=$RUN"; [ "$N" -ge 3 ] && [ "$RUN" = 0 ] && break; sleep 2
done

echo "== poster task"
TASK=$(api GET "/ScheduledTasks?isHidden=false" | python3 -c "import json,sys; print([t['Id'] for t in json.load(sys.stdin) if t['Key']=='BetterPostersQualityFixUpdate'][0])")
api POST "/ScheduledTasks/Running/$TASK" >/dev/null
sleep 3
for i in $(seq 1 120); do
  ST=$(api GET "/ScheduledTasks/$TASK" | python3 -c "import json,sys; t=json.load(sys.stdin); print(t['State'], (t.get('LastExecutionResult') or {}).get('Status'))")
  echo "$ST"; case "$ST" in Idle*) break;; esac; sleep 2
done
api GET "/ScheduledTasks/$TASK" > "$OUT/task.json"
sudo journalctl -u jellyfin --no-pager | grep -i "better posters" | tail -20 | tee "$OUT/log.txt"

echo "== posters"
api GET "/Items?userId=$UID_&Recursive=true&IncludeItemTypes=Movie,Series&Fields=ProviderIds,MediaStreams" > "$OUT/items.json"
python3 - "$OUT/items.json" <<'PY' > "$OUT/ids.txt"
import json,sys
for it in json.load(open(sys.argv[1]))['Items']:
    print(it['Id'], it['Name'].replace(' ','_'), it.get('ImageTags',{}).get('Primary',''))
PY
cat "$OUT/ids.txt"
while read -r ID NAME TAG; do
  curl -sS -f "$J/Items/$ID/Images/Primary?tag=$TAG" -o "$OUT/poster-$NAME.jpg" || echo "no poster for $NAME"
done < "$OUT/ids.txt"
ls -la "$OUT"
grep -q '"Status":"Completed"' "$OUT/task.json" || { echo "::error::task did not complete"; cat "$OUT/task.json"; exit 1; }
