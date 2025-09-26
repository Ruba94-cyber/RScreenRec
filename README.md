# RScreenRecord - Advanced Screen Recording Tool

Un'applicazione C# per la registrazione avanzata dello schermo con supporto per overlay visivi e input touch.

## Caratteristiche

### 🎥 Registrazione Schermo
- **Registrazione multi-monitor**: Rileva automaticamente il monitor sotto il cursore
- **Alta qualità**: Registrazione a 30 FPS in formato AVI non compresso
- **DPI Aware**: Supporto ottimizzato per schermi ad alta risoluzione (es. Panasonic FG-Z2)
- **Gestione automatica file**: Numerazione progressiva e timestamp automatici

### 🎯 Overlay Visivi
- **Indicatore di registrazione**: Pallino lampeggiante per indicare lo stato di registrazione attiva
- **Cursore mouse**: Visualizzazione del cursore nelle registrazioni con punto rosso
- **Touch overlay**: Visualizzazione degli input touch con cerchi rossi bordati

### 🔧 Funzionalità Tecniche
- **Lock file system**: Prevenzione di istanze multiple
- **Thread-safe**: Gestione sicura dei thread per overlay e registrazione
- **Gestione errori robusta**: Exception handling completo
- **Ottimizzato per performance**: Ridotte allocazioni di memoria e gestione efficiente delle risorse

## Requisiti di Sistema

- **OS**: Windows 7 o superiore
- **.NET Framework**: 4.7.2 o superiore
- **Hardware**: Supporto DirectX per acquisizione schermo
- **Touch**: Opzionale - supporto Windows Touch per overlay touch

## Utilizzo

### Avvio Rapido
```bash
ScreenshotFlash.exe
```

### Funzionamento
1. **Primo avvio**: L'applicazione rileva il monitor corrente e inizia la registrazione
2. **Secondo avvio**: Durante una registrazione attiva, ferma la registrazione corrente
3. **File output**: I video vengono salvati in `%USERPROFILE%\Videos\Captures\`

### Denominazione File
I file seguono il pattern: `rec_[numero]_[timestamp].avi`
- **numero**: Contatore progressivo automatico
- **timestamp**: Formato `HHhMMmSSs_dd-MM-yyyy`

Esempio: `rec_1_14h30m45s_26-09-2025.avi`

## Architettura del Codice

### Componenti Principali

#### `Program.cs`
- Entry point dell'applicazione
- Gestione lock file per istanze multiple
- Coordinamento dei thread per overlay
- Gestione ciclo di vita dell'applicazione

#### `ScreenRecorder.cs`
- Core engine per la cattura schermo
- Generazione AVI con writer custom
- Gestione thread-safe della registrazione
- Overlay cursore mouse integrato

#### `AviWriter.cs`
- Writer AVI custom per output non compresso
- Gestione header e index AVI standard
- Ottimizzato per streaming real-time
- Supporto RGB24 bit

#### `RecordingOverlayForm.cs`
- Overlay indicatore di registrazione
- Finestra always-on-top trasparente
- Animazione lampeggio personalizzabile
- Posizionamento automatico schermo

#### `TouchOverlayForm.cs`
- Cattura e visualizzazione input touch
- Registrazione eventi Windows Touch
- Overlay trasparente full-screen
- Cleanup automatico degli input

## Configurazione Avanzata

### Performance Tuning
- **FPS**: Modificabile in `ScreenRecorder.cs` linea 21
- **Qualità**: RGB24 non compresso (modificabile in `AviWriter.cs`)
- **Buffer size**: Calcolato automaticamente basato su risoluzione

### Personalizzazione Overlay
- **Posizione indicatore**: `RecordingOverlayForm.cs` linee 19-20
- **Colori**: Modificabili nelle rispettive classi overlay
- **Dimensioni**: Configurabili nei costruttori dei form

## Risoluzione Problemi

### Problemi Comuni

**Registrazione non si avvia**
- Verificare permessi cartella di output
- Controllare spazio disco disponibile
- Verificare che non ci siano istanze multiple attive

**Performance scadenti**
- Chiudere applicazioni non necessarie
- Verificare utilizzo CPU/memoria
- Ridurre risoluzione schermo se necessario

**Overlay non visibili**
- Verificare impostazioni DPI Windows
- Controllare configurazione multi-monitor
- Riavviare con permessi amministratore se necessario

### File di Log
Gli errori vengono mostrati nella console. Per debug avanzato:
```bash
ScreenshotFlash.exe > log.txt 2>&1
```

## Sviluppo

### Build Requirements
- Visual Studio 2019 o superiore
- .NET Framework 4.7.2 SDK
- Windows SDK per funzionalità touch

### Struttura Progetto
```
RScreenRecord_VSproject/
├── Program.cs              # Entry point
├── ScreenRecorder.cs       # Core recording engine
├── AviWriter.cs           # AVI file writer
├── RecordingOverlayForm.cs # Recording indicator
├── TouchOverlayForm.cs    # Touch input overlay
├── ScreenshotFlash.csproj # Project configuration
└── README.md              # Documentazione
```

### Build Commands
```bash
# Debug build
dotnet build --configuration Debug

# Release build
dotnet build --configuration Release

# Publish self-contained
dotnet publish --configuration Release --self-contained true
```

## Licenza

Questo progetto è distribuito sotto licenza MIT. Vedere il file LICENSE per dettagli.

## Contributi

I contributi sono benvenuti! Per contribuire:
1. Fork del repository
2. Creare branch feature (`git checkout -b feature/nuova-funzionalita`)
3. Commit delle modifiche (`git commit -am 'Aggiunta nuova funzionalità'`)
4. Push del branch (`git push origin feature/nuova-funzionalita`)
5. Aprire Pull Request

## Versioning

- **v1.0**: Release iniziale
- **v1.1**: Bug fixes e ottimizzazioni performance
- **v1.2**: Miglioramento gestione errori e thread safety

## Autore

Paolo - Sviluppo e manutenzione

## Supporto

Per bug reports e feature requests, creare issue nel repository del progetto.