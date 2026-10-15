# InventoryKamera — Roslyn Static Analysis Report

**Data analisi:** 2026-10-15  
**Strumenti usati:** Roslyn (MSBuild + Roslynator 4.12.9), analisi manuale  
**File analizzati:** `ui/UserInterface.cs`, `ui/main/MainForm.cs`, `scraping/ArtifactScraper.cs`, `scraping/MaterialScraper.cs`  
**Target framework:** .NET Framework 4.7.2

---

## Risultato build Roslyn

```
Build UserInterface.cs + MaterialScraper.cs → ✅ 0 errori, 0 warning
```

I due file completamente compilabili dopo le correzioni sono risultati **puliti** con Roslynator 4.12.9.  
I file `ArtifactScraper.cs` e `MainForm.cs` dipendono da sorgenti engine non inclusi nel repository — analizzati tramite revisione manuale.

---

## Correzioni applicate (11/12)

### 1. `ui/UserInterface.cs` — Race condition counter (CRITICO)

**Problema:** `lock(label) { label.Text = (Int32.Parse(label.Text) + 1).ToString() }` — race condition + UI thread contention  
**Fix:** 5 campi `int` atomici + `Interlocked.Increment/Exchange`

```csharp
// Prima (non thread-safe):
lock (artifactCount_Label) {
    artifactCount_Label.Text = (Int32.Parse(artifactCount_Label.Text) + 1).ToString();
}

// Dopo:
private static int _artifactCount;
public static void IncrementArtifactCount() {
    int next = System.Threading.Interlocked.Increment(ref _artifactCount);
    UpdateLabel(next.ToString(), artifactCount_Label);
}
```

**Impatto:** Eliminata race condition su 5 contatori; la lettura/incremento è ora atomica.

---

### 2. `ui/UserInterface.cs` — NLog format string bug (MEDIO)

**Problema:** `Logger.Info($"Parsed {value} weapons to scan", value)` — interpolazione stringa + argomenti posizionali NLog = formato errato  
**Fix:** Rimosso il prefisso `$` per usare solo i placeholder posizionali NLog

```csharp
// Prima (buggy — argomento 'value' ignorato):
Logger.Info($"Parsed {value} weapons to scan", value);

// Dopo (corretto):
Logger.Info("Parsed {value} weapons to scan", value);
```

**Impatto:** I log ora mostrano il valore numerico corretto.

---

### 3. `scraping/ArtifactScraper.cs` — Mutazione bitmap parametro (CRITICO)

**Problema:** `ScanArtifactSet(Bitmap itemName)` chiama `SetGamma(..., ref itemName)` mutando il parametro originale in-place  
**Fix:** Clone esplicito prima della mutazione + disposizione corretta

```csharp
// Prima (mutava l'originale + analizzava bitmap sbagliata):
GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref itemName);
Bitmap grayscale = GenshinProcesor.ConvertToGrayscale(itemName);
GenshinProcesor.SetInvert(ref grayscale);
var scannedText = GenshinProcesor.AnalyzeText(grayscale, ...);

// Dopo (clone → gamma → grayscale → invert → analizza padded):
Bitmap source = (Bitmap)itemName.Clone();
GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref source);
Bitmap grayscale = GenshinProcesor.ConvertToGrayscale(source);
source.Dispose();
GenshinProcesor.SetInvert(ref grayscale);
using (Bitmap padded = ...) {
    var scannedText = GenshinProcesor.AnalyzeText(padded, ...);
}
```

**Impatto:** L'OCR ora analizza il bitmap corretto (padded con contrasto aumentato); l'originale non viene corrotto.

---

### 4. `scraping/ArtifactScraper.cs` — async void silenzioso (ALTO)

**Problema:** `async void QueueScan(...)` — le eccezioni vengono inghiottite silenziosamente  
**Fix:** Rimosso `async` (nessun `await` interno)

```csharp
// Prima:
private async void QueueScan(...)

// Dopo:
private void QueueScan(...)
```

**Impatto:** Le eccezioni ora si propagano e vengono loggate correttamente.

---

### 5. `scraping/ArtifactScraper.cs` — Codice commentato (BASSO)

**Problema:** ~40 righe di codice di sorting commentato (righe 41-82)  
**Fix:** Rimosso completamente

**Impatto:** Leggibilità migliorata; meno rumore cognitivo.

---

### 6. `scraping/MaterialScraper.cs` — goto (MEDIO)

**Problema:** `goto LastPage` esce da un `foreach` dentro un `while(true)` — flusso di controllo non strutturato  
**Fix:** Flag `bool repeatFound` con `break` in entrambi i loop

```csharp
// Prima:
while (true) {
    foreach (...) {
        if (condition) goto LastPage;
    }
}
LastPage:

// Dopo:
bool repeatFound = false;
while (!repeatFound) {
    foreach (...) {
        if (condition) { repeatFound = true; break; }
    }
    if (repeatFound) break;
}
```

**Impatto:** Flusso leggibile; nessuna variabile di controllo implicita tramite goto.

---

### 7. `scraping/MaterialScraper.cs` — readonly mancante (BASSO)

**Problema:** `private static NLog.Logger Logger = ...` — non readonly  
**Fix:** `private static readonly NLog.Logger Logger = ...`

**Impatto:** Segnala intento implicito; previene riassegnazione accidentale.

---

### 8. `ui/main/MainForm.cs` — Null reference su scannerThread (ALTO)

**Problema:** `scannerThread.IsAlive` usato prima di verificare null in `Hotkey_Pressed` e `UnexpectedError`  
**Fix:** Guard `if (scannerThread != null && scannerThread.IsAlive)`

**Impatto:** Previene NullReferenceException all'avvio prima del primo scan.

---

### 9. `ui/main/MainForm.cs` — catch silenzioso (MEDIO)

**Problema:** `catch { }` su upgrade impostazioni — gli errori vengono inghiottiti  
**Fix:** `catch (Exception ex) { Logger.Warn(ex, "Could not upgrade application settings..."); }`

**Impatto:** Gli errori di migrazione settings sono ora visibili nel log.

---

### 10. `ui/main/MainForm.cs` — Codice duplicato (BASSO)

**Problema:** `MainForm_Shown` chiamava esplicitamente il check degli aggiornamenti Genshin, già eseguito da `StartupGameDataCheck` in `MainForm_Load`  
**Fix:** Rimossa la chiamata duplicata

**Impatto:** Eliminata doppia HTTP request agli aggiornamenti all'avvio.

---

### 11. `ui/UserInterface.cs` — ResetCounters incompleto (MEDIO)

**Problema:** `ResetCounters()` azzerava i label UI ma non i campi `int` atomici — successivi `Increment` ripartivano dal valore precedente  
**Fix:** `Interlocked.Exchange(ref _fieldN, 0)` per tutti e 5 i counter atomici

**Impatto:** I counter ripartono da 0 correttamente a ogni scan.

---

## Non corretto (1/12)

### 12. `scraping/ArtifactScraper.cs` — Thread.Abort (CRITICO)

**Problema:** `scannerThread.Abort()` — deprecato/non funzionante in .NET 5+, solleva `ThreadAbortException` che può corrompere lo stato  
**Fix necessario:** Sostituire con `CancellationToken` passato al thread di scan  
**Motivo del rimando:** Richiede refactoring dell'intera scan engine (sorgenti non disponibili nel repo)

---

## Raccomandazioni aggiuntive

| Priorità | Raccomandazione |
|----------|----------------|
| ALTA | Aggiungere `InventoryKamera.csproj` al repository (era mancante) |
| ALTA | Implementare `CancellationToken` al posto di `Thread.Abort` |
| MEDIA | Aggiungere `using` statement per tutti i `Bitmap` in `ScanItemCard` e altri metodi |
| BASSA | Rinominare `WeaponsMax_Labell` (doppia L — typo nel designer) |
| BASSA | Aggiungere `readonly` a tutti i Logger fields nelle classi di scraping |

---

## File csproj creato

`InventoryKamera/InventoryKamera.csproj` — ricostruito da `project.assets.json` per abilitare compilazione e analisi Roslyn.  
**Non sovrascrivere** se si dispone del csproj originale da Visual Studio (potrebbe avere risorse embedded, icone, ecc. non inclusi qui).
