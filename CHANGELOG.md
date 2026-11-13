# Changelog

## v2.0.7 (2026-11-13)
- Fix: skip Manekin/Manekina in character scan (non hanno UI costellazioni standard)
- Fix: la scansione personaggi non si blocca più dopo Manekin/Manekina
- Fix: Dev Items ora vengono scansionati correttamente (dipendeva dal blocco Manekin)

## v2.0.6 (2026-11-13)
- Fix: regex livello personaggio (Qiqi e altri con livello 1 non riconosciuto)
- Fix: WeaponType Catalyst (conversione 1-based → 0-based)
- Aggiunto: Valeriy (Electro, Sword) con ConstellationName placeholder
- Fix: binding redirect Octokit 7.1.0 → 8.0.0 nel .exe.config

## v2.0.5 (2026-11-12)
- Fix: WeaponScraper e ArtifactScraper — StopScanning non si attiva su OCR fail
- Fix: scroll timing migliorato
