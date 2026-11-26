using System;
using PosTouch.Tests;

// Test automatici di POS Touch Android (logica del Core).
// POSTOUCH_TEST_DB=0 salta i test sui database (servono le librerie SQLite vere).
Verifica.Esegui("Carrello", TestCarrello.Esegui);
Verifica.Esegui("Tastierino", TestTastierino.Esegui);
Verifica.Esegui("Varianti", TestVarianti.Esegui);
Verifica.Esegui("Barcode bilancia", TestBilancia.Esegui);
Verifica.Esegui("Opzioni di stampa", TestStampe.Esegui);
if (Environment.GetEnvironmentVariable("POSTOUCH_TEST_DB") != "0")
{
    Verifica.Esegui("Archivio tavoli (SQLite)", TestArchivio.Esegui);
    Verifica.Esegui("Import catalogo (SQLite)", TestCatalogo.Esegui);
    Verifica.Esegui("Emissione scontrino (registratore finto)", TestVendita.Esegui);
    Verifica.Esegui("Driver 3i contro registratore finto (guasti)", TestFinto3i.Esegui);
    Verifica.Esegui("Driver Epson contro stampante finta (guasti)", TestFintoEpson.Esegui);
}

Console.WriteLine($"\n{Verifica.Totale - Verifica.Errori}/{Verifica.Totale} controlli superati, {Verifica.Errori} errori");
return Verifica.Errori == 0 ? 0 : 1;
