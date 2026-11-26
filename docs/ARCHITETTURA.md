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
| | `CombinazioneInCorso` | Articoli multi-gusto e prezzo (più caro / media / fisso) |
| | `BarcodeBilancia` | Etichette EAN-13 della bilancia |
| | `ServizioVendita` | Emissione scontrino (fasi, esito incerto, archivio) |
| `Conti/` | `ServizioConto` | Tasto CONTO (conto non fiscale su termica) |
| `Sale/` | `SessioneTavolo` | Tavolo / conto cliente aperto: comanda, riordino, storno, NO STAMPA, VAI, preconto, chiusura |
| | `Portate` | Numero logico dei separatori (mai per posizione), riordino sezioni |
| | `InstradamentoStampa` | Sala × centro → stampante (eccezioni tipo POSBY) |
| | `ArchivioTavoli` | `tavoli.db`: tavoli aperti, coda di stampa, progressivo comande, registro storni |
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
