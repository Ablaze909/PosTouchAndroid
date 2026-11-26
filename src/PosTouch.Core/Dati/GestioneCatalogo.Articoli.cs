using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    public partial class GestioneCatalogo
    {
        // ══════════════ ARTICOLI ══════════════

        public void SalvaProdotto(Prodotto p)
        {
            if (string.IsNullOrWhiteSpace(p.Descrizione)) throw new ArgumentException("La descrizione è obbligatoria.");
            if (p.PrezzoCentesimi < 0) throw new ArgumentException("Prezzo non valido.");
            if (p.CategoriaId <= 0) throw new ArgumentException("Scegliere la categoria.");
            string barcode = (p.Barcode ?? "").Trim();

            Esegui(conn =>
            {
                if (barcode.Length > 0)
                {
                    using var chk = conn.CreateCommand();
                    chk.CommandText = "SELECT Descrizione FROM Prodotti WHERE Barcode=$b AND Id<>$id AND IFNULL(Attivo,1) >= 0";   // eliminati sul PC (-1): non bloccano
                    chk.Parameters.AddWithValue("$b", barcode);
                    chk.Parameters.AddWithValue("$id", p.Id);
                    if (chk.ExecuteScalar() is string altro)
                        throw new ArgumentException($"Barcode già usato da \"{altro}\".");
                    // anche i barcode multipli del PC (ProdottiBarcode): in vendita hanno la precedenza
                    if (TabellaEsiste(conn, "ProdottiBarcode"))
                    {
                        using var chk2 = conn.CreateCommand();
                        chk2.CommandText = @"SELECT p.Descrizione FROM ProdottiBarcode b JOIN Prodotti p ON p.Id = b.ProdottoId
                                             WHERE TRIM(b.Barcode)=$b AND b.ProdottoId<>$id AND IFNULL(p.Attivo,1) >= 0";
                        chk2.Parameters.AddWithValue("$b", barcode);
                        chk2.Parameters.AddWithValue("$id", p.Id);
                        if (chk2.ExecuteScalar() is string altro2)
                            throw new ArgumentException($"Barcode già usato da \"{altro2}\" (barcode multipli del PC).");
                    }
                }

                using var cmd = conn.CreateCommand();
                if (p.Id == 0)
                {
                    p.Codice = ProssimoCodice(conn);
                    cmd.CommandText = @"INSERT INTO Prodotti (Codice, Barcode, Descrizione, PrezzoCentesimi, CostoCentesimi, CategoriaId, Colore, ColoreTestoTasto, DimensioneFontTasto, IsPreferito, Attivo, CentroProduzioneId,
                                                              VenditaAPeso, UnitaPeso, IsCodiceBilancia, IsCombinazione, NumeroGusti, PoliticaPrezzo, VariantiProporzionali, IsCoperto)
                                        VALUES ($cod, $b, $d, $pr, $costo, $cat, $col, $colT, $font, $pref, $att, $centro,
                                                $peso, $unita, $bil, $comb, $gusti, $pol, $prop, $coperto)";
                    cmd.Parameters.AddWithValue("$cod", p.Codice);
                }
                else
                {
                    cmd.CommandText = @"UPDATE Prodotti SET Barcode=$b, Descrizione=$d, PrezzoCentesimi=$pr, CostoCentesimi=$costo, CategoriaId=$cat,
                                        Colore=$col, ColoreTestoTasto=$colT, DimensioneFontTasto=$font, IsPreferito=$pref, Attivo=$att, CentroProduzioneId=$centro,
                                        VenditaAPeso=$peso, UnitaPeso=$unita, IsCodiceBilancia=$bil, IsCombinazione=$comb, NumeroGusti=$gusti, PoliticaPrezzo=$pol, VariantiProporzionali=$prop, IsCoperto=$coperto WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", p.Id);
                }
                cmd.Parameters.AddWithValue("$b", barcode.Length == 0 ? DBNull.Value : barcode);   // NULL come il PC
                cmd.Parameters.AddWithValue("$d", p.Descrizione.Trim());
                cmd.Parameters.AddWithValue("$pr", p.PrezzoCentesimi);
                cmd.Parameters.AddWithValue("$cat", p.CategoriaId);
                cmd.Parameters.AddWithValue("$col", p.Colore ?? "");
                cmd.Parameters.AddWithValue("$colT", p.ColoreTesto ?? "#FFFFFF");
                cmd.Parameters.AddWithValue("$pref", p.IsPreferito ? 1 : 0);
                cmd.Parameters.AddWithValue("$costo", p.CostoCentesimi);
                cmd.Parameters.AddWithValue("$font", p.DimensioneFontTasto);
                cmd.Parameters.AddWithValue("$att", p.Attivo ? 1 : 0);
                cmd.Parameters.AddWithValue("$centro", p.CentroProduzioneId);
                cmd.Parameters.AddWithValue("$peso", p.VenditaAPeso ? 1 : 0);
                cmd.Parameters.AddWithValue("$unita", string.IsNullOrWhiteSpace(p.UnitaPeso) ? "Kg" : p.UnitaPeso);
                cmd.Parameters.AddWithValue("$bil", p.IsCodiceBilancia ? 1 : 0);
                cmd.Parameters.AddWithValue("$comb", p.IsCombinazione ? 1 : 0);
                cmd.Parameters.AddWithValue("$gusti", Math.Clamp(p.NumeroGusti, 2, 8));
                cmd.Parameters.AddWithValue("$pol", Math.Clamp(p.PoliticaPrezzo, 0, 2));
                cmd.Parameters.AddWithValue("$prop", p.VariantiProporzionali ? 1 : 0);
                cmd.Parameters.AddWithValue("$coperto", p.IsCoperto ? 1 : 0);
                cmd.ExecuteNonQuery();
                if (p.Id == 0)
                {
                    // l'articolo nuovo prende il suo Id: un secondo SALVA dalla stessa pagina aggiorna invece di duplicare
                    using var id = conn.CreateCommand();
                    id.CommandText = "SELECT last_insert_rowid()";
                    p.Id = Convert.ToInt32(id.ExecuteScalar());
                }
            });
            LogManager.Info($"Articolo salvato: {p.Descrizione} €{p.PrezzoCentesimi / 100m:F2} cat {p.CategoriaId}");
        }

        /// <summary>👁 Nascondi / 👁 Mostra di più articoli insieme (barra azioni del Back office).</summary>
        public void ImpostaAttivo(IEnumerable<int> ids, bool attivo)
        {
            var lista = new List<int>(ids);
            Esegui(conn =>
            {
                using var tx = conn.BeginTransaction();   // tutto o niente, e un solo salvataggio su disco
                foreach (int id in lista)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Prodotti SET Attivo=$a WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$a", attivo ? 1 : 0);
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }

        /// <summary>📂 Sposta più articoli in un'altra categoria.</summary>
        public void SpostaProdotti(IEnumerable<int> ids, int categoriaId)
        {
            var lista = new List<int>(ids);
            Esegui(conn =>
            {
                using var tx = conn.BeginTransaction();   // tutto o niente, e un solo salvataggio su disco
                foreach (int id in lista)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Prodotti SET CategoriaId=$c WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$c", categoriaId);
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }

        public void EliminaProdotti(IEnumerable<int> ids)
        {
            var lista = new List<int>(ids);
            Esegui(conn =>
            {
                using var tx = conn.BeginTransaction();   // tutto o niente, e un solo salvataggio su disco
                foreach (int id in lista)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM Prodotti WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                    EliminaBarcodeMultipli(conn, tx, id);
                }
                tx.Commit();
            });
        }

        public void EliminaProdotto(int id) => EliminaProdotti(new[] { id });

        /// <summary>Barcode multipli (tabella del PC) dell'articolo eliminato: non devono restare a puntare a un articolo che non c'è.</summary>
        private static void EliminaBarcodeMultipli(SqliteConnection conn, SqliteTransaction tx, int id)
        {
            if (!TabellaEsiste(conn, "ProdottiBarcode")) return;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM ProdottiBarcode WHERE ProdottoId=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        private static bool TabellaEsiste(SqliteConnection conn, string nome)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
            cmd.Parameters.AddWithValue("$n", nome);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }
}
