# Requisiti di conformità per Microsoft Store

Questa applicazione desktop è stata verificata rispetto alle principali policy Microsoft Store per le app Win32 convertite in MSIX.

## Idoneità
- L'app è un registratore dello schermo privo di contenuti vietati.
- Non utilizza API non documentate o non supportate.
- Non installa driver o servizi in background permanenti.

## Sicurezza e privacy
- Nessun componente scarica codice aggiuntivo da Internet durante l'esecuzione.
- Non viene effettuata alcuna raccolta di dati personali.
- Viene fornita un'informativa sulla privacy (`Store/PrivacyPolicy.md`) accessibile dallo Store e dall'app.

## Comportamento dell'app
- L'app si avvia in finestra e richiede l'interazione dell'utente per avviare le registrazioni.
- Non modifica impostazioni di sistema senza consenso.
- Supporta la chiusura tramite le normali azioni di Windows.

## Contenuto e metadati
- Le risorse grafiche dello Store sono fornite nel pacchetto MSIX (`StorePackaging/StoreAssets`).
- La descrizione e le parole chiave saranno fornite in italiano e inglese nel portale Partner Center.

## Prezzo
- Il prezzo di listino previsto per la pubblicazione è 0,99 € / $0.99.

Per ulteriori dettagli consultare la documentazione ufficiale: [Microsoft Store Policies](https://learn.microsoft.com/windows/uwp/publish/store-policies).
