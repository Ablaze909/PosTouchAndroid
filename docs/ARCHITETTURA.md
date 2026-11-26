# POS Touch Android — mappa del codice

Regola: **la logica sta in `PosTouch.Core`** (libreria .NET senza interfaccia, testabile); **`PosTouch.App`** contiene solo schermate e dialoghi che chiamano il Core.
Ogni file ha una responsabilità sola; i commenti in testa a ogni classe dicono da quale parte del software Windows deriva.

## PosTouch.Core
| Cartella | File | Cosa fa |
|---|---|---|
| `Fiscale/` | `RT3iConnector`, `EpsonConnector` | Driver RT copiati dal PC (non modificare senza test su RT) |
| | `AccessoRT` | Un solo dialogo alla volta con l'RT |
| | `TestoRT` | Accentate e frazioni → ASCII per il 3i |
| `Vendita/` | `Carrello` | Righe scontrino: articoli, sconti, portate, gusti, righe comandate (storno) |
| | `Tastierino` | Tastiera numerica (qta, X, €) |
| | `CombinazioneInCorso` | Articoli multi-gusto (apertura, gusti, chiusura, annulla) |
| | `Combinazioni` | Prezzo della combinazione (più caro / media / fisso + varianti, anche proporzionali) |
| | `Varianti` | VAR＋ / VAR﹣: riga a cui applicarle, prezzo con segno, testo comanda |
| | `BarcodeBilancia` | Etichette EAN-13 della bilancia |
| | `ServizioVendita` | Emissione scontrino (fasi, esito incerto, archivio) |
| `Conti/` | `ServizioConto` | Tasto CONTO (conto non fiscale su termica) |
| `Sale/` | `SessioneTavolo` | Tavolo / conto cliente aperto: comanda, riordino, storno, NO STAMPA, VAI, preconto, chiusura |
| | `Portate` | Numero logico dei separatori (mai per posizione), riordino sezioni |
| | `InstradamentoStampa` | Sala × centro → stampante (eccezioni tipo POSBY) |
| | `ArchivioTavoli` | `tavoli.db`: tavoli aperti, sposta/scambia/unisci, coda di stampa, deviazioni, progressivo comande, registro storni |
| | `SpoolerComande` | Coda di stampa in sottofondo con riprova |
| `Stampa/` | `EscPos`, `StampanteTermica`, `AccessoStampanti` | Comandi ESC/POS, collegamento TCP, una stampa alla volta per stampante |
| | `LayoutConto`, `LayoutComanda` | Impaginazioni di conto, comanda, storno, VAI (identiche al PC) |
| `Dati/` | `Catalogo`, `GestioneCatalogo` | `catalogo.db` (schema pos.db del PC) |
| | `ArchivioVendite` | `vendite.db`: scontrini, conti, righe con "fotografia" dei dati |
| `Configurazione/` | `Impostazioni`, `ConfigSale` | `impostazioni.json` |

## Regole che non vanno rotte (dai bug del PC)
1. Righe "comandate" segnate **solo nella stessa transazione** in cui la stampa entra in coda (`ArchivioTavoli.Salva`).
2. Portate per **numero logico** del separatore (`Portate.NumeroLogico`), mai contando le posizioni.
3. Toccare un gusto o il padre di una combinazione toglie **tutta** la combinazione (storno compreso), senza domande.
4. Togliere un articolo **non** comandato è una correzione silenziosa; togliere uno comandato genera STORNO.
5. Lo storico non dipende dal catalogo: ogni riga venduta ne salva una copia.
6. La riga variante tiene il prezzo **con segno**: togliere un "senza" restituisce il prezzo (sul PC lo scalava due volte).
7. Su un articolo già comandato non si aggiungono/tolgono varianti né si cambia la quantità: si elimina (storno) e si ribatte.
8. Il tavolo si libera solo uscendo o dopo il pagamento (`liberaSeVuoto`): il salvataggio continuo non lo cancella mai, anche se vuoto.
9. Gli storni non ancora stampati sono salvati in `RigheTavolo` con `InStorno=1`: non si perdono se l'app si chiude.
10. Se il salvataggio di un'uscita fallisce, flag "comandata" e portata tornano com'erano (`ConRipristino`): si può riprovare.
11. Le stampe di una stampante escono in ordine: una stampa in errore blocca le successive della stessa stampante.
12. Un centro senza stampante non segna gli articoli come comandati: restano da comandare.
13. I messaggi (MessaggioBox) non vengono mai scartati; le domande fiscali non si chiudono con il tasto indietro.
14. Stampe come POSBY (`ConfigStampe`): elenco IP, uscite 1-8 (= centro di produzione di categorie e articoli),
    PRECONTI / RIEPILOGHI / uscite con i loro parametri; con "impostazioni per sala" ogni sala (Banco = -1) ha la sua pagina.
    I layout producono solo il contenuto: salto righe, cicalino, taglio e inversione li aggiunge `Finitura`.
15. Comanda da banco (`ComandaBanco`): eliminacode preso PRIMA di emettere, comanda accodata solo a documento uscito.
