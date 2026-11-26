# POS Touch Android — mappa del codice

Regola: **la logica sta in `PosTouch.Core`** (libreria .NET senza interfaccia, testabile); **`PosTouch.App`** contiene solo schermate e dialoghi che chiamano il Core.
Ogni file ha una responsabilità sola; i commenti in testa a ogni classe dicono da quale parte del software Windows deriva.

**File divisi in parti (`partial`)**: le classi grandi sono spezzate per area con il nome `Classe.Area.cs`
(es. `RT3iConnector.Flusso.cs`, `SessioneTavolo.Comande.cs`, `VenditaPage.Portate.cs`). Una correzione su un'area
tocca solo quel file. Nessun file supera ~370 righe: un file nuovo che cresce oltre va diviso allo stesso modo.
I tipi di supporto (esiti, risposte, modelli) stanno in `Modelli*.cs` accanto alla classe.

## PosTouch.Core
| Cartella | File | Cosa fa |
|---|---|---|
| `Fiscale/` | `RT3iConnector`, `EpsonConnector` | Driver RT copiati dal PC (non modificare senza test su RT). Parti: connessione (file principale), `Flusso`/`Protocollo` (invio, XON/XOFF, risposte), `Vendita`, `Chiusura`, `Rapporti`, `AnnulloReso`, `Recovery` |
| | `ModelliRT`, `ModelliEpson` | Risposte e risultati dei registratori (TX_FOOTER, 50F, frame Epson) |
| | `AccessoRT` | Un solo dialogo alla volta con l'RT |
| | `TestoRT` | Accentate e frazioni → ASCII per il 3i |
| `Vendita/` | `Carrello` (+ `.Combinazioni`, `.Portate`) | Righe scontrino: articoli, sconti, portate, gusti, righe comandate (storno) |
| | `Tastierino` | Tastiera numerica (qta, X, €) |
| | `CombinazioneInCorso` | Articoli multi-gusto (apertura, gusti, chiusura, annulla) |
| | `Combinazioni` | Prezzo della combinazione (più caro / media / fisso + varianti, anche proporzionali) |
| | `Varianti` | VAR＋ / VAR﹣: riga a cui applicarle, prezzo con segno, testo comanda |
| | `BarcodeBilancia` | Etichette EAN-13 della bilancia |
| | `ServizioVendita` (+ `.Emissione`) | Emissione scontrino: `Emissione.cs` ha le fasi in ordine di protocollo (valida → RT pronto → righe → sconti → chiusura → verifica → cassetto); ogni fase restituisce null o l'esito con cui fermarsi. Il registratore si può sostituire con uno finto nei test (`tests/RegistratoreFinto.cs`) |
| `Conti/` | `ServizioConto` | Tasto CONTO (conto non fiscale su termica) |
| `Sale/` | `SessioneTavolo` (+ `.Comande`, `.Avanza`, `.Operazioni`) | Tavolo / conto cliente aperto: uscite (principale), comanda e storno, VAI, sposta/unisci/preconto |
| | `Portate` | Numero logico dei separatori (mai per posizione), riordino sezioni |
| | `InstradamentoStampa` | Sala × centro → stampante (eccezioni tipo POSBY) |
| | `ArchivioTavoli` (+ `.Coda`, `.Numeratori`, `.SpostaUnisci`) | `tavoli.db`: tavoli aperti, sposta/scambia/unisci, coda di stampa, deviazioni, progressivo comande ed eliminacode, registro storni |
| | `SpoolerComande` | Coda di stampa in sottofondo con riprova |
| `Stampa/` | `EscPos`, `StampanteTermica`, `AccessoStampanti` | Comandi ESC/POS, collegamento TCP, una stampa alla volta per stampante |
| | `LayoutConto`, `LayoutComanda` | Impaginazioni di conto, comanda, storno, VAI (identiche al PC) |
| `Dati/` | `Catalogo`, `GestioneCatalogo` (+ `.Categorie`, `.Articoli`, `.Varianti`) | `catalogo.db` (schema pos.db del PC); import con copie `.bak` |
| | `ArchivioVendite` (+ `.Conti`) | `vendite.db`: scontrini, conti, righe con "fotografia" dei dati (anche le varianti) |
| `Dati/` | `ArchivioOperatori` | `operatori.db`: operatori (PIN con hash PBKDF2, mai in chiaro; un PIN = una persona), permessi, turni |
| `Vendita/` | `ServizioOperatori` | Turni: un solo turno alla volta, accesso con PIN, sospensione, chiusura (solo il suo operatore o un amministratore), autorizzazione al volo con PIN |
| `Configurazione/` | `Impostazioni`, `ConfigSale`, `ConfigStampe` | `impostazioni.json`; `ConfigStampe` = IP, uscite 1-8 e pagine di stampa per sala (stile POSBY) |

## PosTouch.App (solo interfaccia)
| Pagina | Parti |
|---|---|
| `VenditaPage` | `.Catalogo` (categorie/articoli/barcode), `.Lista` (righe), `.Tastiera`, `.Pagamento`, `.Varianti`, `.Combinazione`, `.Tavolo` (apertura/uscite/salvataggio), `.Posto` (banda "dove sono", barra tavolo), `.Portate` (preconto, VAI) |
| `TavoliPage` | `.Schede` (sale), `.Griglia` (tavoli) |
| `ImpostazioniPage` | `.Azioni` (import, dati di prova, log) + `Servizi/ManutenzioneDati` |
| `BackOfficePage` | `.Prodotti`, `.Categorie` |
| `ProdottoPage` | `.Tipo` (standard / coperto / peso / combinazione) |
| `SaleConfigPage` | `.Sale`, `.Comande` |
| `StampePage` | `.Uscite` |
| `CalcoloResto` | `.Logica` (calcolo identico al PC) |

## Regole che non vanno rotte (dai bug del PC)
- **Operatori**: con la gestione accesa non si vende senza turno aperto; il turno di un altro rimasto aperto blocca la cassa finché non viene chiuso; deve restare sempre un amministratore attivo; le autorizzazioni con PIN valgono per una sola operazione.
- **Listini**: il prezzo di un articolo si prende SEMPRE con `Prodotto.PrezzoListino(Carrello.Listino)` (mai `PrezzoCentesimi` diretto in vendita); il listino lo imposta `SessioneTavolo` dalla sala, il banco resta 1; le righe già battute non si ricalcolano.
- **Esclusioni uscite** (`Uscita.EscludiAltre/EscludiInAltre`): si applicano solo con `ConfigStampe.AltriPer` / `FlussoPer`, mai filtrando a mano nelle singole comande.
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

## Test automatici (`tests/PosTouch.Tests`, eseguiti in CI prima dell'APK)
- Logica: carrello, tastierino, varianti, bilancia, opzioni di stampa, archivio tavoli, import catalogo.
- **Registratori finti su TCP** (`Finto3i`, `FintoEpson`): i driver VERI parlano con un registratore simulato che sa
  fare guasti — coperchio aperto, carta finita in chiusura, riga o pagamento rifiutati, footer perso, rete che cade,
  RT senza TX_FOOTER. Regola verificata: **mai uno scontrino perso dato per emesso, mai un doppio scontrino**.
  Per un bug nuovo sul registratore: aggiungere il guasto al finto e il caso al test, poi correggere.

## Varianti a peso e combinazioni a mano
- Variante con prezzo su un articolo a peso: importo fisso in `RigaScontrino.SovrapprezzoCent` (non moltiplicato per i Kg). Sull'RT esce come riga "VARIANTI" (o sconto sulla riga se negativo). Salvato in RigheTavolo, RigheVendita, ContiRighe.
- Combinazione già chiusa: varianti aggiunte/tolte cambiano il prezzo solo della differenza (`Combinazioni.Aggiorna`), così un prezzo cambiato a mano resta.

## Password assistenza e UNISCI per portata
- Password assistenza (`PasswordAssistenza`, hash PBKDF2 in Impostazioni): vale al posto di password amministratore e PIN autorizzati (permessi, gestione operatori, chiusura turni altrui). Si salva subito; per cambiarla/toglierla serve quella attuale. Ogni uso finisce nel log. Non apre turni di vendita.
- UNISCI (`UnioneTavoli.Unisci`): ogni articolo resta nella sua portata, un solo separatore per numero, PortataCorrente resta quella del tavolo di destinazione (come il PC).
- Gestione operatori già con un amministratore: per riaccenderla serve il PIN amministratore (o l'assistenza).
- Impostazioni per sala riaccese con pagine vecchie: si chiede se ricopiare quelle attuali.
