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

def fetch_json(url: str, timeout: int = 15) -> dict | list | None:
    req = urllib.request.Request(url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode())
    except Exception as e:
        print(f"  [WARN] fetch_json({url}): {e}")
        return None

def fetch_text(url: str, timeout: int = 15) -> str | None:
    req = urllib.request.Request(url, headers={**HEADERS, "Accept": "text/html,*/*"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.read().decode("utf-8", errors="replace")
    except Exception as e:
        print(f"  [WARN] fetch_text({url}): {e}")
        return None

# ─── 1. Versione gioco ────────────────────────────────────────────────────────
def get_current_version() -> str | None:
    """Recupera versione corrente da genshin.dev."""
    data = fetch_json("https://genshin.dev/api/version")
    if data and isinstance(data, dict):
        return data.get("current") or data.get("version")
    # fallback: wiki fandom
    text = fetch_text("https://genshin-impact.fandom.com/wiki/Version")
    if text:
        m = re.search(r"Version\s+(\d+\.\d+)", text)
        if m:
            return m.group(1)
    return None

# ─── 2. Nuovi personaggi ─────────────────────────────────────────────────────
ELEMENT_MAP = {
    "Pyro":"pyro","Hydro":"hydro","Cryo":"cryo","Electro":"electro",
    "Anemo":"anemo","Geo":"geo","Dendro":"dendro",
}
WEAPON_MAP = {"Sword":1,"Claymore":2,"Polearm":3,"Bow":4,"Catalyst":5}

def get_new_characters(version: str) -> list[dict]:
    """
    Tenta di recuperare i nuovi personaggi per la versione indicata.
    Fonte primaria: genshin.dev characters list + details.
    """
    chars_list = fetch_json("https://genshin.dev/api/characters")
    if not isinstance(chars_list, list):
        return []
    # Carica characters.json esistente per capire chi è già presente
    existing = set()
    if CHARS_FILE.exists():
        try:
            existing = set(json.loads(CHARS_FILE.read_text("utf-8")).keys())
        except Exception:
            pass
    new_chars = []
    for key in chars_list:
        if key.lower() in existing:
            continue
        detail = fetch_json(f"https://genshin.dev/api/characters/{key}")
        if not isinstance(detail, dict):
            continue
        elem_raw = detail.get("element", "")
        weapon_raw = detail.get("weapon_type") or detail.get("weaponType") or ""
        new_chars.append({
            "key": key.lower().replace(" ", "").replace("-", ""),
            "GOOD": detail.get("name", key).replace(" ", ""),
            "element": ELEMENT_MAP.get(elem_raw, elem_raw.lower()),
            "weaponType": WEAPON_MAP.get(weapon_raw, 1),
            "constellation": detail.get("constellation", ""),
        })
    return new_chars

# ─── 3. Nuove armi ────────────────────────────────────────────────────────────
def get_new_weapons(version: str) -> list[dict]:
    weapons_list = fetch_json("https://genshin.dev/api/weapons")
    if not isinstance(weapons_list, list):
        return []
    existing = set()
    if WEAPONS_FILE.exists():
        try:
            existing = set(json.loads(WEAPONS_FILE.read_text("utf-8")).keys())
        except Exception:
            pass
    new_weapons = []
    for key in weapons_list:
        slug = key.lower().replace(" ", "").replace("-", "").replace("'", "")
        if slug in existing:
            continue
        detail = fetch_json(f"https://genshin.dev/api/weapons/{key}")
        if not isinstance(detail, dict):
            continue
        wtype_raw = detail.get("type") or detail.get("weaponType") or ""
        new_weapons.append({
            "key": slug,
            "GOOD": detail.get("name", key).replace(" ", "").replace("'", ""),
            "type": WEAPON_MAP.get(wtype_raw, 1),
            "rarity": detail.get("rarity", 4),
        })
    return new_weapons

# ─── 4. Dati banner da Paimon.moe ─────────────────────────────────────────────
def get_banner_data() -> dict:
    """
    Tenta di leggere i banner correnti da paimon.moe o hoyowiki.
    Restituisce un dict con chiavi: version, bannerCurrent, bannerNext
    """
    data = fetch_json("https://paimon.moe/api/banners/current")
    if isinstance(data, list) and data:
        banners = data
    else:
        # fallback: genshin.dev schedule
        banners = fetch_json("https://genshin.dev/api/banners/current") or []

    result = {
        "version": "",
        "bannerCurrent": [],
        "bannerNext": [],
    }
    today = datetime.date.today().isoformat()
    for b in (banners if isinstance(banners, list) else []):
        btype = b.get("type", "")
        chars5 = b.get("featured_5_star") or b.get("featured5") or []
        chars4 = b.get("featured_4_star") or b.get("featured4") or []
        weapons = b.get("featured_weapons") or b.get("featuredWeapons") or []
        start = b.get("start") or b.get("start_date") or ""
        end   = b.get("end")   or b.get("end_date")   or ""
        note  = b.get("name") or ""
        if start <= today <= end:
            if btype in ("character", "char", ""):
                result["bannerCurrent"].append({
                    "type": "char",
                    "chars5": [c.replace(" ", "") for c in chars5],
                    "chars4": [c.replace(" ", "") for c in chars4],
                    "start": start, "end": end, "note": note,
                })
            else:
                result["bannerCurrent"].append({
                    "type": "weapon",
                    "weapons": [w.replace(" ", "") for w in weapons],
                    "start": start, "end": end,
                })
    return result

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
    """Aggiunge nuovi personaggi a characters.json. Restituisce numero aggiunti."""
    if not new_chars:
        return 0
    data = {}
    if CHARS_FILE.exists():
        try:
            data = json.loads(CHARS_FILE.read_text("utf-8"))
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
            "ConstellationName": [c.get("constellation", "Unknown")],
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
    """Aggiunge nuove armi a weapons.json. Restituisce numero aggiunte."""
    if not new_weapons:
        return 0
    data = {}
    if WEAPONS_FILE.exists():
        try:
            data = json.loads(WEAPONS_FILE.read_text("utf-8"))
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
    old = VERSION_FILE.read_text("utf-8").strip() if VERSION_FILE.exists() else ""
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

    # 1. Versione gioco
    print("\n[1/5] Recupero versione gioco...")
    version = get_current_version()
    if version:
        print(f"  Versione corrente: {version}")
        update_version(version)
    else:
        version = "0.0"
        print("  [WARN] Versione non trovata, skip version.txt")

    # 2. Nuovi personaggi
    print("\n[2/5] Controllo nuovi personaggi...")
    new_chars = get_new_characters(version)
    if new_chars:
        added = update_characters(new_chars)
        print(f"  Aggiunti {added} personaggi a characters.json")
        for c in new_chars[:5]:
            print(f"    + {c['GOOD']} ({c['element']}, W:{c['weaponType']})")
    else:
        print("  Nessun nuovo personaggio trovato.")

    # 3. Nuove armi
    print("\n[3/5] Controllo nuove armi...")
    new_weapons = get_new_weapons(version)
    if new_weapons:
        added = update_weapons(new_weapons)
        print(f"  Aggiunte {added} armi a weapons.json")
    else:
        print("  Nessuna nuova arma trovata.")

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
    version = get_current_version()
    print(f"Versione gioco: {version or 'non trovata'}")
    new_chars = get_new_characters(version or "")
    print(f"Nuovi personaggi: {len(new_chars)}")
    for c in new_chars[:3]:
        print(f"  + {c['GOOD']}")
    new_weapons = get_new_weapons(version or "")
    print(f"Nuove armi: {len(new_weapons)}")
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
