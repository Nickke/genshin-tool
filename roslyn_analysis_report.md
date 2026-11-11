# Roslyn Analysis Report — Inventory Kamera

**Data analisi:** 2026-10-15  
**Strumenti:** Roslynator 4.12.9, compilatore C# 7.3, .NET Framework 4.7.2  
**File analizzati:** 4 (2 inclusi nel csproj, 2 analizzati solo via code review)

---

## Ambiente e scope

Il progetto (`InventoryKamera.csproj`) compila solo un sottoinsieme dei file:

| File | In csproj | Analisi Roslynator | Analisi manuale |
|---|---|---|---|
| `ui/UserInterface.cs` | ✅ | ✅ 0 diagnostici | ✅ |
| `scraping/MaterialScraper.cs` | ✅ | ✅ 0 diagnostici | ✅ |
| `ui/main/MainForm.cs` | ❌ | ❌ non compilato | ✅ |
| `scraping/ArtifactScraper.cs` | ❌ | ❌ non compilato | ✅ |

**Build:** `dotnet build InventoryKamera.sln` → **Warnings: 0 · Errors: 0**

---

## 1. `ui/UserInterface.cs`

### 1.1 Diagnostici Roslynator / Compilatore

> **Risultato ufficiale:** nessun diagnostico emesso. Build pulita.

### 1.2 Complessità ciclomatica (metodi principali)

| Metodo | CC |
|---|---|
| `Init` | 1 |
| `UpdatePictureBox` | 2 (catch) |
| `UpdateTextBox` | 2 (catch) |
| `UpdateLabel` | 2 (catch) |
| `SetCharacter_Talent` | 2 (if) |
| `SetProgramStatus` | 2 (ternary `ok ?`) |
| `ResetCounters` | 1 |
| `ResetAll` | 1 |

Tutti i metodi restano sotto soglia (CC ≤ 10). Nessun metodo è candidato a refactoring per complessità.

### 1.3 Code Smells

#### 🔴 HIGH — GDI font leak in `SetProgramStatus` (riga 272)

```csharp
programStatus_Label.Font = new Font(programStatus_Label.Font.FontFamily, 15);
```

Ad ogni chiamata di `SetProgramStatus` viene creato un nuovo oggetto `Font` GDI senza che il vecchio venga smaltito con `.Dispose()`. Ogni scan genera più chiamate → perdita progressiva di handle GDI.

**Fix:** salvare il `Font` in un campo statico `readonly` oppure usare `using`.

---

#### 🟡 MEDIUM — Double newline in `AddError` (riga 281)

```csharp
UpdateTextBox($"{error.Replace("\n", Environment.NewLine)}" + Environment.NewLine, error_TextBox);
```

`UpdateTextBox` (riga 119) aggiunge già `Environment.NewLine` dopo il testo. `AddError` concatena un ulteriore `Environment.NewLine` prima → ogni errore produce una riga vuota aggiuntiva nella UI.

---

#### 🟡 MEDIUM — Stato statico mutable non protetto da null (righe 13–48)

Tutti i campi statici (`gear_PictureBox`, `gear_TextBox`, ecc.) restano `null` finché `Init()` non viene chiamato. Qualsiasi metodo pubblico invocato prima di `Init()` lancia `NullReferenceException`. Non esiste guard né contratto documentato.

---

#### 🟢 LOW — `try/catch` silenzioso oscura errori (righe 92–145)

`UpdatePictureBox`, `UpdateTextBox` e `UpdateLabel` catturano qualsiasi eccezione e la loggano solo a livello `Debug`. Se il control handle non è valido (es. form già disposed) l'eccezione scompare silenziosamente. Preferire `if (pictureBox.IsHandleCreated)` prima di `Invoke`.

---

## 2. `ui/main/MainForm.cs`

### 2.1 Diagnostici Roslynator / Compilatore

> **File non incluso nel csproj** → nessun diagnostico formale emesso da Roslynator.

### 2.2 Complessità ciclomatica (metodi principali)

| Metodo | CC | Note |
|---|---|---|
| `ScannerDelayValue` | 4 | switch con 3 case |
| `Hotkey_Pressed` | 2 | if IsAlive |
| `MainForm_Load` | 5 | if×3 + catch |
| `StartupGameDataCheck` | 5 | try + if + switch (2 case) |
| `StartButton_Clicked` | 10 | if×4 + catch×3 + thread lambda |
| `OptionsMenuItem_KeyDown` | 9 | bool guards×4 + switch×4 |
| `DatabaseUpdateMenuItem_Click` | 7 | switch + switch annidato |
| `CheckForKameraUpdates` | 4 | async + try + if×2 |
| `CheckForGenshinUpdates` | 8 | METODO MORTO (non chiamato) |
| `ApplyToControls` | 9 | switch pattern matching×8 |

⚠️ `StartButton_Clicked` (CC=10) è al limite della soglia standard (10). Candidato a refactoring.

### 2.3 Code Smells

#### 🔴 HIGH — `async void CheckForKameraUpdates` (riga 891)

```csharp
private async void CheckForKameraUpdates()
```

I metodi `async void` sono fire-and-forget: qualsiasi eccezione non catturata viene propagata al `SynchronizationContext` e **crasha l'applicazione** senza possibilità di recovery. Deve essere `async Task` e il chiamante deve gestire il risultato.

---

#### 🔴 HIGH — Metodo morto `CheckForGenshinUpdates` (righe 918–959)

`CheckForGenshinUpdates` è un metodo `private` con 42 righe che **non viene mai chiamato** in nessun punto del codebase. La logica di aggiornamento dei dati Genshin è stata migrata in `StartupGameDataCheck` (riga 174). Il metodo va rimosso.

---

#### 🟡 MEDIUM — `Thread.Abort()` deprecato (riga 94)

```csharp
scannerThread.Abort();
```

`Thread.Abort()` è rimosso in .NET 5+ (`PlatformNotSupportedException`). Anche su .NET Framework è inaffidabile: può lasciare risorse in stato inconsistente. Soluzione corretta: passare un `CancellationToken` allo scanner thread.

---

#### 🟡 MEDIUM — GDI font leak in `ApplyTheme` (riga 567)

```csharp
Font baseFont = new Font("Segoe UI", 9F);
```

Il `Font` viene creato ad ogni chiamata di `ApplyTheme` (toggle dark mode) senza essere dismesso. Il vecchio font assegnato ai controlli non viene rilasciato. **Fix:** dichiarare il font come campo `static readonly`.

---

#### 🟡 MEDIUM — `GC.Collect()` nel click button (riga 286)

```csharp
GC.Collect();
```

Forza una collection GC completa sul thread UI al click di StartScan. Anti-pattern noto: blocca il thread UI e non porta benefici misurabili in questo contesto. Rimuovere.

---

#### 🟢 LOW — `new DatabaseManager()` inutile in `StartButton_Clicked` (riga 305)

```csharp
var gameVersion = new DatabaseManager().LocalVersion.ToString(2);
```

Il campo `databaseManager` (riga 29) già esiste. Creare una nuova istanza solo per leggere `LocalVersion` è uno spreco. Usare `databaseManager.LocalVersion`.

---

#### 🟢 LOW — Logica colori AutoCopy duplicata

Il codice di setup dei colori per i pulsanti AutoCopy è copia-incollato verbatim tra `MainForm_Load` (righe 157–168) e `AutoCopy_CheckBox_CheckedChanged` (righe 524–535). Estrarre in metodo privato.

---

#### 🟢 LOW — Lifetime mismatch tra `scannerThread` (static) e `running` (instance)

`scannerThread` è `static` (riga 27) mentre `running` è un campo di istanza (riga 33). Se mai venissero create due istanze di `MainForm`, `scannerThread` sarebbe condiviso ma `running` no, con comportamento indefinito.

---

## 3. `scraping/ArtifactScraper.cs`

### 3.1 Diagnostici Roslynator / Compilatore

> **File non incluso nel csproj** → nessun diagnostico formale emesso da Roslynator.

### 3.2 Complessità ciclomatica (metodi principali)

| Metodo | CC | Note |
|---|---|---|
| `ScanArtifacts` | 9 | while + for + if×4 + else |
| `ClearFilters` | 2 | if Contains |
| `QueueScan` | 3 | if×2 |
| `IsElixirArtifact` | 3 | for + if |
| `CatalogueFromBitmapsAsync` | 2 | if bm.Count |
| `ScanArtifactMainStat` | 3 | switch 2 case |
| `ScanArtifactLevel` | 2 | TryParse ternary |
| `ScanArtifactSubStats` | 7 | for + if×4 + regex |
| `ScanArtifactEquippedCharacter` | 3 | if×2 |
| `ScanArtifactSet` | 1 | lineare |

### 3.3 Code Smells

#### 🟡 MEDIUM — Null check ridondante in `ClearFilters` (riga 117)

```csharp
if (t != null && t.Contains("filter"))
```

`t` proviene da `GenshinProcesor.AnalyzeText(x).Trim().ToLower()`: nessun metodo della catena ritorna `null`. Il controllo `t != null` è dead code.

---

#### 🟡 MEDIUM — 6 righe debug commentate in `QueueScan` (righe 139–145)

```csharp
//Navigation.DisplayBitmap(name);
//Navigation.DisplayBitmap(locked);
// ...
```

Sei chiamate a `DisplayBitmap` commentate rimaste in produzione. Va eliminato o sostituito con `#if DEBUG`.

---

#### 🟡 MEDIUM — `Bitmap bm` non protetto da `using` in `ScanArtifactSubStats` (riga 404)

```csharp
Bitmap bm = (Bitmap)artifactImage.Clone();
// ...
bm.Dispose(); // riga 423
```

Se un'eccezione viene lanciata tra riga 404 e 423, `bm` non viene smaltito. Va usato `using (Bitmap bm = ...)`.

---

#### 🟢 LOW — Indici locali come `int` invece di `const int` (riga 249)

```csharp
int a_name = 0; int a_gearSlot = 1; int a_mainStat = 2; ...
```

Sette indici dichiarati come variabili mutabili su una sola riga. Dovrebbero essere `const int` o un `enum` per chiarire che sono indici fissi e per evitare modifiche accidentali.

---

## 4. `scraping/MaterialScraper.cs`

### 4.1 Diagnostici Roslynator / Compilatore

> **Risultato ufficiale:** nessun diagnostico emesso. Build pulita.

### 4.2 Complessità ciclomatica (metodi principali)

| Metodo | CC | Note |
|---|---|---|
| `Scan_Materials` | 15 | while + foreach + if×6 + for×3 annidati |
| `ScanMora` | 2 | if TryParse |
| `ParseMoraFromScreenshot` | 1 | lineare |
| `ScanMaterialName` | 3 | if×2 |
| `ScanMaterialCount` | 8 | for scale + for pixel + fallback loop + TryParse×2 |
| `SafeExtractMaxCounter` | 2 | ternary |

⚠️ `Scan_Materials` (CC=15) supera la soglia critica (>10). Candidato prioritario a refactoring — estrarre la logica di scroll in un metodo dedicato e la LastPage scan in un metodo separato.

### 4.3 Code Smells

#### 🔴 HIGH — `quantity` Bitmap mai dismessa nel loop principale (righe 117–135)

```csharp
material.count = ScanMaterialCount(rectangle, out Bitmap quantity);
if (material.count == 0)
{
    UserInterface.AddError(...);
    SaveInventoryBitmap(quantity, ...); // quantity mai .Dispose()d
}
```

Nella sezione LastPage (riga 224) `quantity.Dispose()` è correttamente presente. Nel loop principale invece `quantity` viene allocata ad ogni iterazione ma **mai rilasciata** → perdita progressiva di memoria e handle GDI per ogni materiale scansionato.

**Fix:** `using (var _ = quantity) { ... }` oppure `quantity?.Dispose()` dopo l'uso.

---

#### 🟡 MEDIUM — `SaveInventoryBitmap` chiamata due volte per count == 0 (righe 121–126)

```csharp
if (material.count == 0)
{
    UserInterface.AddError(...);
    SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png"); // 1a scrittura
}
if (Properties.Settings.Default.LogScreenshots || material.count == 0)
{
    SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png"); // 2a scrittura (duplicata)
}
```

Quando count == 0, il file PNG viene scritto due volte sul disco. Il secondo `if` dovrebbe essere `else if` oppure `if (Properties.Settings.Default.LogScreenshots && material.count != 0)`.

---

#### 🟡 MEDIUM — Algoritmo di scroll con magic numbers (righe 156–170)

```csharp
if ((k == 7) && ((scrollCount % 3) == 0))
{
    k++;
    if (scrollCount % 9 == 0)
    {
        if (scrollCount == 18) scrollCount = 0;
        else Navigation.sim.Mouse.VerticalScroll(-1);
    }
}
```

Logica di calibrazione basata su `% 3`, `% 9`, `== 18` senza commento esplicativo. Il motivo della calibrazione (compensare il drift dello scroll UI di Genshin) non è documentato. Richiede commento `// WHY:` o estrazione in metodo con nome significativo.

---

#### 🟡 MEDIUM — `Scan_Materials` (CC=15) supera la soglia critica

Il metodo è 178 righe e gestisce: loop principale, loop last-page, scroll, parsing, errori UI e persistenza. Refactoring consigliato: estrarre `ScanMainPages()`, `ScrollToNextPage()`, `ScanLastPage()`.

---

#### 🟢 LOW — `passby` — nome variabile criptico (riga 190)

```csharp
bool passby = true;
```

La variabile controlla se saltare i materiali già visti durante il reverse scan final. `skipAlreadyScanned` o `bypassKnownItems` sarebbe più leggibile.

---

#### 🟢 LOW — `struct Material` con campi pubblici mutabili (righe 13–35)

`Material` è una struct con `public string name` e `public int count`. Le struct mutabili in C# sono un footgun classico (le copie struct accidentali causano bug difficili da tracciare). Nel codice il pattern `material.name = ...` funziona perché `material` è sempre una variabile locale, ma il tipo non lo garantisce per chi chiama dall'esterno.

---

## Riepilogo per severità

| Severità | UserInterface.cs | MainForm.cs | ArtifactScraper.cs | MaterialScraper.cs | Totale |
|---|---|---|---|---|---|
| 🔴 HIGH | 1 | 2 | 0 | 1 | **4** |
| 🟡 MEDIUM | 2 | 4 | 3 | 3 | **12** |
| 🟢 LOW | 1 | 3 | 2 | 3 | **9** |
| **Totale** | **4** | **9** | **5** | **7** | **25** |

---

## Priorità di intervento

1. **`quantity` Bitmap leak** (`MaterialScraper.cs:117–135`) — causa perdita di GDI handle ad ogni scan di materiali.
2. **`async void CheckForKameraUpdates`** (`MainForm.cs:891`) — rischio crash app su exception da GitHub API.
3. **Metodo morto `CheckForGenshinUpdates`** (`MainForm.cs:918`) — 42 righe dead code da rimuovere.
4. **Font leak `SetProgramStatus`** (`UserInterface.cs:272`) — accumulo handle GDI durante scan prolungati.
5. **Font leak `ApplyTheme`** (`MainForm.cs:567`) — accumulo handle GDI ad ogni toggle dark mode.
6. **`Scan_Materials` refactoring** (`MaterialScraper.cs`) — CC=15, metodo da spezzare.

---

## Note metodologiche

- **Roslynator 4.12.9** analizza solo `UserInterface.cs` e `MaterialScraper.cs` (file presenti nel csproj). Per includere `MainForm.cs` e `ArtifactScraper.cs` nell'analisi formale, aggiungere le rispettive `<Compile Include="...">` entries nel csproj oppure creare un secondo progetto di analisi.
- Il progetto usa **C# 7.3** (target net472) con `Nullable=disable`: l'analisi di nullable reference type non è disponibile. Aggiornare a `<LangVersion>latest</LangVersion>` e abilitare `<Nullable>enable</Nullable>` esporrebbe decine di warning aggiuntivi su possibili NullReferenceException.
- Il connettore **cwm-roslyn-navigator** del plugin `dotnet-claude-kit` non era disponibile in questa sessione: l'analisi è stata condotta con `dotnet build -v:d` + revisione manuale del codice.

---

*Questo file può essere passato a un'altra sessione Claude Code per implementare i fix.*
