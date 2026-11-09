#!/usr/bin/env python3
"""
genshin_update.py — Auto-aggiornamento GenshinTool ogni ~20 giorni (nuovo banner)
Aggiorna: GenshinTool.html (GENSHIN_EVENTS, TEATRO_SEASON), inventorylists/version.txt,
          inventorylists/characters.json, inventorylists/weapons.json
"""

import json
import re
import sys
import os
import datetime
import urllib.request
import urllib.error
import urllib.parse
import html
from pathlib import Path

# ─── Percorsi ────────────────────────────────────────────────────────────────
BASE_DIR = Path(__file__).parent
HTML_FILE = BASE_DIR / "GenshinTool.html"
INV_DIR   = BASE_DIR / "inventorylists"
VERSION_FILE = INV_DIR / "version.txt"
CHARS_FILE   = INV_DIR / "characters.json"
WEAPONS_FILE = INV_DIR / "weapons.json"

# ─── Utilità HTTP ─────────────────────────────────────────────────────────────
HEADERS = {
    "User-Agent": "Mozilla/5.0 GenshinToolUpdater/1.0",
    "Accept": "application/json",
}

def fetch_json(url: str, timeout: int = 10) -> dict | list | None:
    req = urllib.request.Request(url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode())
    except Exception as e:
        print(f"  [WARN] fetch_json({url.split('?')[0]}): {e}", flush=True)
        return None

def fetch_text(url: str, timeout: int = 10) -> str | None:
    req = urllib.request.Request(url, headers={**HEADERS, "Accept": "text/html,*/*"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.read().decode("utf-8", errors="replace")
    except Exception as e:
        print(f"  [WARN] fetch_text({url}): {e}", flush=True)
        return None

# ─── Mapping elementi e armi ─────────────────────────────────────────────────
ELEMENT_MAP = {
    # ambr.top usa questi nomi
    "Ice":"cryo", "Fire":"pyro", "Water":"hydro",
    "Electric":"electro", "Wind":"anemo", "Rock":"geo", "Grass":"dendro",
    # genshin.dev usa questi
    "Pyro":"pyro","Hydro":"hydro","Cryo":"cryo","Electro":"electro",
    "Anemo":"anemo","Geo":"geo","Dendro":"dendro",
}
WEAPON_MAP = {
    "Sword":1,"Claymore":2,"Polearm":3,"Bow":4,"Catalyst":5,
    "Pole":3,  # ambr.top chiama i polearm "Pole"
}

GENSHINDB_BASE = "https://genshin-db-api.vercel.app/api/v5"
WEAPON_TYPE_MAP = {
    "WEAPON_SWORD_ONE_HAND": 1, "Sword": 1,
    "WEAPON_CLAYMORE": 2, "Claymore": 2,
    "WEAPON_POLE": 3, "Polearm": 3, "WEAPON_POLEARM": 3,
    "WEAPON_BOW": 4, "Bow": 4,
    "WEAPON_CATALYST": 5, "Catalyst": 5,
}

def _slug(s: str) -> str:
    """Genera una chiave slug lowercase senza caratteri speciali."""
    return re.sub(r"[^a-z0-9]", "", s.lower())

def _good(s: str) -> str:
    """Converte un nome in formato GOOD (senza spazi/apostrofi)."""
    return re.sub(r"[^A-Za-z0-9]", "", s)

# ─── 1. Versione gioco ────────────────────────────────────────────────────────
def _ver_tuple(v: str) -> tuple:
    """Converte "7.1" in (7, 1) per confronto ordinato."""
    try:
        return tuple(int(x) for x in str(v).split("."))
    except Exception:
        return (0, 0)

def get_current_version() -> str | None:
    """
    Legge la versione corrente del gioco campionando più personaggi
    per trovare il MAX della versione (non l'ultimo alfabetico).
    Fallback: version.txt locale.
    """
    names_data = fetch_json(f"{GENSHINDB_BASE}/characters?query=names&matchCategories=true")
    if isinstance(names_data, list) and names_data:
        # Campiona ~12 personaggi distribuiti nella lista (inizio, metà, fine)
        total = len(names_data)
        indices = set()
        for i in range(0, min(total, 12)):
            indices.add(int(i * total / 12))
        indices.add(total - 1)   # sempre l'ultimo
        indices.add(total - 2)   # e il penultimo

        versions = []
        for idx in sorted(indices):
            name = names_data[idx]
            if name in _SKIP_CHARS:
                continue
            d = fetch_json(
                f"{GENSHINDB_BASE}/characters?query={urllib.parse.quote(name)}"
                "&matchCategories=true&verboseCategories=true"
            )
            if isinstance(d, dict) and d.get("version"):
                v = str(d["version"])
                if re.match(r'^\d+\.\d+', v):
                    versions.append(v)

        if versions:
            best = max(versions, key=_ver_tuple)
            print(f"  Fonte: genshin-db-api (campione {len(versions)} chars) → {best}")
            return best

    # Fallback: version.txt locale
    if VERSION_FILE.exists():
        local_ver = VERSION_FILE.read_text("utf-8-sig").strip()
        if local_ver and re.match(r'^\d+\.\d+', local_ver):
            print(f"  Fonte: version.txt locale → {local_ver}")
            return local_ver

    return None

# ─── 2. Nuovi personaggi ─────────────────────────────────────────────────────
# Nomi da skippare (Traveler, NPC non giocabili)
_SKIP_CHARS = {"Aether", "Lumine"}

def _fetch_char_detail(name: str) -> dict | None:
    """Fetcha i dettagli di un personaggio da genshin-db-api."""
    return fetch_json(
        f"{GENSHINDB_BASE}/characters?query={urllib.parse.quote(name)}"
        "&matchCategories=true&verboseCategories=true"
    )

def get_new_characters(_version: str = "") -> tuple[list[dict], str]:
    """
    Restituisce (nuovi_personaggi, versione_max).
    Nuovi = presenti nell'API ma non ancora in characters.json.
    """
    existing = set()
    if CHARS_FILE.exists():
        try:
            existing = set(json.loads(CHARS_FILE.read_text("utf-8")).keys())
        except Exception:
            pass

    names = fetch_json(f"{GENSHINDB_BASE}/characters?query=names&matchCategories=true")
    if not isinstance(names, list):
        print("  [WARN] genshin-db-api non raggiungibile per personaggi.")
        return [], ""

    new_chars = []
    max_ver = ""
    for name in names:
        if name in _SKIP_CHARS:
            continue
        key = _slug(name)
        if key in existing:
            continue
        detail = _fetch_char_detail(name)
        if not isinstance(detail, dict):
            continue
        elem_raw = detail.get("elementText") or detail.get("element", "")
        wpn_raw  = detail.get("weaponText")  or detail.get("weaponType", "")
        ver      = str(detail.get("version", ""))
        constellation = detail.get("constellation", "")
        new_chars.append({
            "key":          key,
            "GOOD":         _good(name),
            "element":      ELEMENT_MAP.get(elem_raw, elem_raw.lower()),
            "weaponType":   WEAPON_TYPE_MAP.get(wpn_raw, 1),
            "constellation": constellation,
            "version":      ver,
        })
        if ver and re.match(r'^\d+\.\d+', ver):
            if not max_ver or tuple(int(x) for x in ver.split(".")) > tuple(int(x) for x in max_ver.split(".")):
                max_ver = ver

    return new_chars, max_ver

# ─── 3. Nuove armi ────────────────────────────────────────────────────────────
def get_new_weapons(_version: str = "") -> tuple[list[dict], str]:
    """
    Restituisce (nuove_armi, versione_max).
    Nuove = presenti nell'API ma non ancora in weapons.json.
    """
    existing = set()
    if WEAPONS_FILE.exists():
        try:
            existing = set(json.loads(WEAPONS_FILE.read_text("utf-8")).keys())
        except Exception:
            pass

    names = fetch_json(f"{GENSHINDB_BASE}/weapons?query=names&matchCategories=true")
    if not isinstance(names, list):
        print("  [WARN] genshin-db-api non raggiungibile per armi.")
        return [], ""

    new_weapons = []
    max_ver = ""
    for name in names:
        key = _slug(name)
        if key in existing:
            continue
        detail = fetch_json(
            f"{GENSHINDB_BASE}/weapons?query={urllib.parse.quote(name)}"
            "&matchCategories=true&verboseCategories=true"
        )
        if not isinstance(detail, dict):
            continue
        type_raw = detail.get("weaponText") or detail.get("weaponType", "")
        ver      = str(detail.get("version", ""))
        new_weapons.append({
            "key":    key,
            "GOOD":   _good(name),
            "type":   WEAPON_TYPE_MAP.get(type_raw, 1),
            "rarity": detail.get("rarity", 4),
            "version": ver,
        })
        if ver and re.match(r'^\d+\.\d+', ver):
            if not max_ver or tuple(int(x) for x in ver.split(".")) > tuple(int(x) for x in max_ver.split(".")):
                max_ver = ver

    return new_weapons, max_ver

# ─── 4. Dati banner ───────────────────────────────────────────────────────────
def _parse_banners(banners: list, today: str) -> list:
    """Filtra i banner attivi oggi da una lista generica."""
    result = []
    for b in banners:
        if not isinstance(b, dict):
            continue
        btype  = b.get("type", "")
        chars5 = b.get("featured_5_star") or b.get("featured5") or b.get("characters5", [])
        chars4 = b.get("featured_4_star") or b.get("featured4") or b.get("characters4", [])
        weapons= b.get("featured_weapons") or b.get("featuredWeapons") or b.get("weapons", [])
        start  = b.get("start") or b.get("start_date") or b.get("time_start") or ""
        end    = b.get("end")   or b.get("end_date")   or b.get("time_end")   or ""
        note   = b.get("name") or b.get("title") or ""
        if not start or not end:
            continue
        # Normalizza a "YYYY-MM-DD"
        start_d = start[:10]
        end_d   = end[:10]
        if start_d <= today <= end_d:
            if btype in ("weapon", "weapons", "arm", "armi"):
                result.append({"type":"weapon","weapons":[re.sub(r"[^A-Za-z0-9]","",w) for w in weapons],"start":start_d,"end":end_d})
            else:
                result.append({"type":"char","chars5":[c.replace(" ","") for c in chars5],"chars4":[c.replace(" ","") for c in chars4],"start":start_d,"end":end_d,"note":note})
    return result

def get_banner_data() -> dict:
    """
    Tenta di leggere i banner correnti da più fonti.
    Restituisce un dict con chiave bannerCurrent (lista dei banner attivi).
    """
    today = datetime.date.today().isoformat()
    banner_urls = [
        "https://paimon.moe/api/banners/current",
        "https://paimon.moe/api/banners",
        "https://paimon.moe/api/banners/list",
        "https://genshin.dev/api/banners/current",
    ]
    for url in banner_urls:
        raw = fetch_json(url)
        if isinstance(raw, list) and raw:
            banners = _parse_banners(raw, today)
            if banners:
                print(f"  Banner trovati da {url}")
                return {"bannerCurrent": banners}
        elif isinstance(raw, dict):
            # potrebbe essere {banners:[...]} o {data:[...]}
            for key in ("banners", "data", "current", "result"):
                if isinstance(raw.get(key), list):
                    banners = _parse_banners(raw[key], today)
                    if banners:
                        print(f"  Banner trovati da {url} (chiave '{key}')")
                        return {"bannerCurrent": banners}

    print("  [WARN] Nessun dato banner trovato — bannerCurrent non verrà aggiornato.")
    return {"bannerCurrent": []}

# ─── 5. Legge GENSHIN_EVENTS esistente da HTML ───────────────────────────────
def read_js_var(html_text: str, var_name: str) -> str | None:
    """
    Estrae il valore grezzo (testo JS) di una variabile var X = {...};
    """
    pattern = rf'var\s+{re.escape(var_name)}\s*=\s*(\{{[\s\S]*?\}});'
    m = re.search(pattern, html_text)
    return m.group(1) if m else None

def parse_js_object_field(js_text: str, field: str) -> str | None:
    """Estrae il valore di un campo stringa da un oggetto JS (best-effort)."""
    m = re.search(rf'["\']?{re.escape(field)}["\']?\s*:\s*["\']([^"\']*)["\']', js_text)
    return m.group(1) if m else None

def read_current_events(html_text: str) -> dict:
    """
    Legge GENSHIN_EVENTS dall'HTML e restituisce un dict con i campi base.
    Se non trovato restituisce un dict vuoto con struttura minima.
    """
    js = read_js_var(html_text, "GENSHIN_EVENTS")
    if not js:
        return {"version": "", "lastUpdated": "", "bannerPhase1": [], "bannerCurrent": [], "bannerNext": [], "events": []}
    version = parse_js_object_field(js, "version") or ""
    last_upd = parse_js_object_field(js, "lastUpdated") or ""
    return {"version": version, "lastUpdated": last_upd, "_raw_js": js}

def read_current_teatro(html_text: str) -> dict:
    js = read_js_var(html_text, "TEATRO_SEASON")
    if not js:
        return {}
    return {"_raw_js": js, "version": parse_js_object_field(js, "version") or ""}

# ─── 6. Costruisce nuovo GENSHIN_EVENTS JS ────────────────────────────────────
def build_events_js(current: dict, banner: dict, version: str, today: str) -> str:
    """
    Costruisce il testo JS per la variabile GENSHIN_EVENTS.
    Mantiene bannerPhase1 dal corrente, aggiorna bannerCurrent/bannerNext con i dati freschi.
    """
    # Cerca di estrarre bannerPhase1 dal raw JS esistente
    raw = current.get("_raw_js", "")
    # Se abbiamo il raw, manteniamo la struttura esistente e aggiorniamo i campi chiave
    # Strategia: sostituzione dei campi version, lastUpdated, bannerCurrent
    if raw:
        # Aggiorna version e lastUpdated
        raw = re.sub(r'version\s*:\s*"[^"]*"', f'version: "{version}"', raw, count=1)
        raw = re.sub(r'lastUpdated\s*:\s*"[^"]*"', f'lastUpdated: "{today}"', raw, count=1)
        # Aggiorna bannerCurrent solo se abbiamo dati freschi
        if banner.get("bannerCurrent"):
            new_bc = json.dumps(banner["bannerCurrent"], ensure_ascii=False, indent=4)
            raw = re.sub(
                r'bannerCurrent\s*:\s*\[[\s\S]*?\](?=\s*,)',
                f'bannerCurrent: {new_bc}',
                raw, count=1
            )
        return raw
    # Fallback: costruisce da zero con struttura minima
    today_str = today
    banner_current_js = json.dumps(banner.get("bannerCurrent", []), ensure_ascii=False, indent=4)
    return f"""{{
  version: "{version}",
  lastUpdated: "{today_str}",
  bannerPhase1: [],
  bannerCurrent: {banner_current_js},
  bannerNext: [],
  events: []
}}"""

# ─── 7. Aggiorna variabile JS in HTML ────────────────────────────────────────
def replace_js_var(html_text: str, var_name: str, new_value_js: str) -> tuple[str, bool]:
    """
    Sostituisce var <var_name> = {...}; nel testo HTML.
    Restituisce (nuovo_testo, successo).
    """
    pattern = rf'(var\s+{re.escape(var_name)}\s*=\s*)(\{{[\s\S]*?\}})(;)'
    replacement = rf'\g<1>{new_value_js}\3'
    new_text, n = re.subn(pattern, replacement, html_text, count=1)
    return new_text, n > 0

# ─── 8. Aggiorna characters.json ─────────────────────────────────────────────
def update_characters(new_chars: list[dict]) -> int:
    """Aggiunge nuovi personaggi a characters.json (merge additivo). Restituisce numero aggiunti."""
    if not new_chars:
        return 0
    data = {}
    if CHARS_FILE.exists():
        try:
            data = json.loads(CHARS_FILE.read_text("utf-8-sig"))  # gestisce BOM
        except Exception:
            pass
    added = 0
    for c in new_chars:
        key = c["key"]
        if key in data:
            continue
        # ConstellationOrder di default: skill → burst (personalizzabile)
        data[key] = {
            "GOOD": c["GOOD"],
            "ConstellationName": [c.get("constellation") or "Unknown"],
            "ConstellationOrder": ["skill", "burst"],
            "Element": [c["element"]],
            "WeaponType": c["weaponType"],
        }
        added += 1
    if added:
        CHARS_FILE.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8"
        )
    return added

# ─── 9. Aggiorna weapons.json ────────────────────────────────────────────────
def update_weapons(new_weapons: list[dict]) -> int:
    """Aggiunge nuove armi a weapons.json (merge additivo). Restituisce numero aggiunte."""
    if not new_weapons:
        return 0
    data = {}
    if WEAPONS_FILE.exists():
        try:
            data = json.loads(WEAPONS_FILE.read_text("utf-8-sig"))  # gestisce BOM
        except Exception:
            pass
    added = 0
    for w in new_weapons:
        key = w["key"]
        if key in data:
            continue
        data[key] = {
            "GOOD": w["GOOD"],
            "type": w["type"],
            "rarity": w["rarity"],
        }
        added += 1
    if added:
        WEAPONS_FILE.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8"
        )
    return added

# ─── 10. Aggiorna version.txt ─────────────────────────────────────────────────
def update_version(version: str) -> bool:
    if not version:
        return False
    INV_DIR.mkdir(exist_ok=True)
    old = VERSION_FILE.read_text("utf-8-sig").strip() if VERSION_FILE.exists() else ""
    # Non sovrascrivere se la versione locale è già più recente dell'API
    if old and _ver_tuple(old) > _ver_tuple(version):
        print(f"  version.txt: versione locale ({old}) > API ({version}), skip")
        return False
    if old != version:
        VERSION_FILE.write_text(version, encoding="utf-8")
        print(f"  version.txt: {old or '(vuoto)'} → {version}")
        return True
    print(f"  version.txt: già aggiornato ({version})")
    return False

# ─── Funzione interattiva: aggiornamento manuale Teatro / Banner ──────────────
PROMPT_TEATRO = """
━━━ TEATRO IMMAGINARIO — Aggiornamento Manuale ━━━
Il sistema non ha trovato dati aggiornati automaticamente.
Vuoi inserire i dati del Teatro manualmente? (s/n): """

def ask_manual_teatro(html_text: str) -> str | None:
    """
    Chiede all'utente se vuole aggiornare TEATRO_SEASON manualmente.
    Restituisce il nuovo JS o None se skip.
    In modalità --auto salta sempre.
    """
    if "--auto" in sys.argv:
        print("  [AUTO] TEATRO_SEASON: skip prompt manuale (modalità automatica).")
        return None
    ans = input(PROMPT_TEATRO).strip().lower()
    if ans not in ("s", "si", "sì", "y", "yes"):
        return None
    print("\nInserisci i nuovi dati del Teatro (una riga per campo):")
    name    = input("  Nome stagione (es. 'Stagione 29 — Novembre 2026'): ").strip()
    ver     = input("  Versione (es. '7.2'): ").strip()
    elems   = input("  Elementi (es. 'electro,hydro,cryo'): ").strip()
    opening = input("  Personaggi apertura (separati da virgola): ").strip()
    guests  = input("  Ospiti (separati da virgola): ").strip()
    blessing= input("  Benedizione principale: ").strip()

    elems_list   = [e.strip() for e in elems.split(",") if e.strip()]
    opening_list = [c.strip() for c in opening.split(",") if c.strip()]
    guests_list  = [g.strip() for g in guests.split(",") if g.strip()]

    new_js = f"""{{
  name:"{name}", version:"{ver}",
  elems:{json.dumps(elems_list)},
  opening:{json.dumps(opening_list)},
  guests:{json.dumps(guests_list)},
  blessing:"{blessing}",
  blessings:[],
  charsPerAct:4, maxUses:2,
  acts:[],
  rotation:[],
  pruneBuffs:[]
}}"""
    return new_js

# ─── MAIN ────────────────────────────────────────────────────────────────────
def main():
    today = datetime.date.today().isoformat()
    print(f"\n{'='*60}")
    print(f"  GenshinTool Auto-Update — {today}")
    print(f"{'='*60}\n")

    # Leggi HTML esistente
    if not HTML_FILE.exists():
        print(f"[ERR] GenshinTool.html non trovato in {BASE_DIR}")
        sys.exit(1)
    html_text = HTML_FILE.read_text("utf-8")
    print(f"✔ GenshinTool.html letto ({len(html_text):,} caratteri)")

    # 2. Nuovi personaggi (prima della versione, così ne ricaviamo la versione)
    print("\n[2/5] Controllo nuovi personaggi...")
    new_chars, chars_ver = get_new_characters()
    if new_chars:
        added = update_characters(new_chars)
        print(f"  Aggiunti {added} personaggi a characters.json")
        for c in new_chars[:5]:
            print(f"    + {c['GOOD']} ({c['element']}, W:{c['weaponType']}, v{c.get('version','')})")
    else:
        print("  Nessun nuovo personaggio trovato.")

    # 3. Nuove armi
    print("\n[3/5] Controllo nuove armi...")
    new_weapons, weapons_ver = get_new_weapons()
    if new_weapons:
        added = update_weapons(new_weapons)
        print(f"  Aggiunte {added} armi a weapons.json")
        for w in new_weapons[:3]:
            print(f"    + {w['GOOD']} (rarity:{w['rarity']}, v{w.get('version','')})")
    else:
        print("  Nessuna nuova arma trovata.")

    # 1. Versione gioco — ricavata da nuovi chars/armi o da version.txt
    print("\n[1/5] Determinazione versione gioco...")
    # Prendi la versione più alta tra chars e weapons, o quella dall'API
    version = ""
    for v in [chars_ver, weapons_ver]:
        if v and re.match(r'^\d+\.\d+', v):
            if not version or tuple(int(x) for x in v.split(".")) > tuple(int(x) for x in version.split(".")):
                version = v
    if not version:
        # Nessun nuovo contenuto, prova API dedicata
        version = get_current_version()
    if not version:
        print("[ERR] Versione gioco non determinabile (API down e version.txt assente).")
        print("      Aggiornamento HTML annullato — characters/weapons già aggiornati.")
        sys.exit(0)   # exit 0: chars/weapons OK, solo HTML saltato
    print(f"  Versione corrente: {version}")

    # Controlla se la versione nell'HTML è già aggiornata
    current_events_check = read_current_events(html_text)
    html_version = current_events_check.get("version", "")
    if html_version == version and not new_chars and not new_weapons:
        print(f"  Tutto già aggiornato alla versione {version}. Nulla da fare.")
        sys.exit(0)

    update_version(version)

    # 4. Aggiorna GENSHIN_EVENTS
    print("\n[4/5] Aggiornamento GENSHIN_EVENTS...")
    current_events = read_current_events(html_text)
    print(f"  Versione attuale in HTML: {current_events.get('version','?')}")
    banner = get_banner_data()
    new_events_js = build_events_js(current_events, banner, version, today)
    html_text, ok = replace_js_var(html_text, "GENSHIN_EVENTS", new_events_js)
    if ok:
        print(f"  ✔ GENSHIN_EVENTS aggiornato (version → {version}, lastUpdated → {today})")
    else:
        print("  [WARN] GENSHIN_EVENTS non trovato nell'HTML, skip.")

    # 5. TEATRO_SEASON — solo aggiornamento campi version/lastUpdated automaticamente
    print("\n[5/5] Controllo TEATRO_SEASON...")
    current_teatro = read_current_teatro(html_text)
    teatro_ver = current_teatro.get("version", "")
    if teatro_ver and teatro_ver != version:
        raw = current_teatro.get("_raw_js", "")
        if raw:
            # aggiorna solo il campo version nel teatro
            raw_updated = re.sub(r'version\s*:\s*"[^"]*"', f'version:"{version}"', raw, count=1)
            html_text, ok2 = replace_js_var(html_text, "TEATRO_SEASON", raw_updated)
            if ok2:
                print(f"  ✔ TEATRO_SEASON version aggiornato: {teatro_ver} → {version}")
            else:
                print("  [WARN] TEATRO_SEASON non trovato, skip.")
        # Offri aggiornamento manuale completo
        new_teatro_js = ask_manual_teatro(html_text)
        if new_teatro_js:
            html_text, ok3 = replace_js_var(html_text, "TEATRO_SEASON", new_teatro_js)
            print(f"  ✔ TEATRO_SEASON aggiornato manualmente: {ok3}")
    else:
        print(f"  TEATRO_SEASON già sulla versione {teatro_ver}, nessuna modifica automatica.")
        if "--teatro" in sys.argv:
            new_teatro_js = ask_manual_teatro(html_text)
            if new_teatro_js:
                html_text, ok3 = replace_js_var(html_text, "TEATRO_SEASON", new_teatro_js)
                print(f"  ✔ TEATRO_SEASON aggiornato manualmente: {ok3}")

    # Salva HTML
    HTML_FILE.write_text(html_text, encoding="utf-8")
    print(f"\n✔ GenshinTool.html salvato ({len(html_text):,} caratteri)")

    print(f"\n{'='*60}")
    print("  Aggiornamento completato!")
    print(f"{'='*60}\n")

# ─── Modalità --preview ───────────────────────────────────────────────────────
def preview():
    """Mostra cosa verrebbe aggiornato senza modificare nulla."""
    today = datetime.date.today().isoformat()
    print(f"\n--- PREVIEW (nessuna modifica) — {today} ---\n")
    new_chars, chars_ver = get_new_characters()
    new_weapons, weapons_ver = get_new_weapons()
    version = chars_ver or weapons_ver or get_current_version()
    print(f"Versione rilevata: {version or 'non trovata'}")
    print(f"Nuovi personaggi: {len(new_chars)}")
    for c in new_chars[:5]:
        print(f"  + {c['GOOD']} ({c['element']}, W:{c['weaponType']}, v{c.get('version','')})")
    print(f"Nuove armi: {len(new_weapons)}")
    for w in new_weapons[:3]:
        print(f"  + {w['GOOD']} (rarity:{w['rarity']}, v{w.get('version','')})")
    banner = get_banner_data()
    print(f"Banner correnti trovati: {len(banner.get('bannerCurrent', []))}")
    print("\n--- Fine preview ---\n")


if __name__ == "__main__":
    if "--preview" in sys.argv:
        preview()
    elif "--help" in sys.argv or "-h" in sys.argv:
        print(__doc__)
        print("Uso: python genshin_update.py [--preview] [--teatro] [--auto] [--help]")
        print("  --preview  Mostra cosa verrebbe aggiornato senza modificare")
        print("  --teatro   Forza aggiornamento manuale TEATRO_SEASON")
        print("  --auto     Modalità non interattiva (salta prompt manuali, per scheduler)")
    else:
        main()
