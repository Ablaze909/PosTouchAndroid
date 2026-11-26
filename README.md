# POS Touch Android — Fase 1: vendita al banco + scontrino fiscale

App per tablet Android (dalla 7.0 in su, quindi anche Android 11). Si collega in rete al registratore telematico (3i o Epson).

## Come avere l'APK (senza Visual Studio)

1. Ogni modifica caricata sul ramo `main` avvia la compilazione automatica (scheda **Actions** del repository).
2. Dopo circa 10 minuti apri l'ultima build verde e scarica **POS_Touch_Android_bN** dalla sezione *Artifacts*. È uno ZIP che contiene l'APK.
3. Copia l'APK sul tablet e installalo (al primo utilizzo va consentita l'installazione da "origini sconosciute").

L'APK è firmato sempre con la stessa chiave (`keystore/`), quindi ogni nuova versione **si installa sopra la precedente** e non si perdono catalogo e vendite. Non cancellare la cartella `keystore`.

## Primo avvio

1. **Impostazioni**: tipo registratore, IP, porta → **PROVA CONNESSIONE** → **SALVA**.
2. **Importa catalogo**: chiudi POS Touch sul PC, copia `pos.db` sul tablet e sceglilo da *Importa catalogo*. Categorie, reparti, prezzi e colori sono gli stessi del PC.
   Senza catalogo si può vendere comunque con il tasto **REPARTO** (importo libero).

## Cosa fa questa versione

- Vendita al banco: categorie, preferiti, prodotti, barcode (lettore USB/Bluetooth), quantità, sconto % o € sul totale, reparto a importo libero.
- Pagamenti: contanti (esatto o con resto), carta, misto.
- Funzioni cassa: lettura giornaliera, chiusura fiscale Z, ristampa ultimo scontrino, apertura cassetto, sblocco RT, incassi del giorno.
- Le vendite sono salvate in `vendite.db`, con le stesse tabelle del PC (`Vendite`, `RigheVendita`).

Esclusi in questa fase: tavoli, comande, tabacchi/Gratta e Vinci, terminale Nexi, operatori, resi e annulli.

## Struttura del codice

```
src/
├─ PosTouch.Core/            Logica, nessuna grafica (riusabile anche su Windows)
│  ├─ Fiscale/               Connettori RT3i ed Epson: copia identica della v17, cambia solo la lettura dei timeout
│  ├─ Vendita/               Carrello, accorpamento righe, ServizioVendita (sequenza di emissione dello scontrino)
│  ├─ Cassa/                 Funzioni di servizio sul registratore
│  ├─ Dati/                  Catalogo (legge pos.db) e ArchivioVendite (vendite.db)
│  ├─ Configurazione/        impostazioni.json
│  ├─ Modelli/               Categoria, Prodotto, RigaScontrino
│  └─ Diagnostica/           LogManager (debug.log)
└─ PosTouch.App/             App Android: solo schermate
   ├─ Pagine/                Vendita, Impostazioni, Funzioni cassa
   ├─ Servizi/               ContestoCassa (collega le pagine ai servizi), Importi
   └─ Platforms/Android/     Manifest e activity
```

Regola: le pagine non parlano mai direttamente col registratore o col database, passano dai servizi del Core. Una correzione ai connettori va fatta in `Core/Fiscale` e vale per tutta l'app.

## Log

*Impostazioni → CONDIVIDI LOG* invia `debug.log` via WhatsApp, email o altro, per la diagnosi.
