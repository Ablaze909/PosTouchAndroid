"""Crea catalogo_prova.db: catalogo di prova (stesso schema del pos.db del PC) per provare POS Touch Android.
Uso: python3 tools/crea_catalogo_prova.py  → src/PosTouch.App/Resources/Raw/catalogo_prova.db"""
import sqlite3, os
dest = os.path.join(os.path.dirname(__file__), '..', 'src', 'PosTouch.App', 'Resources', 'Raw', 'catalogo_prova.db')
if os.path.exists(dest): os.remove(dest)
c = sqlite3.connect(dest)
c.executescript('''
CREATE TABLE Categorie (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL UNIQUE, RepartoFiscale INTEGER NOT NULL, AliquotaIVA INTEGER NOT NULL,
  Colore TEXT, Ordine INTEGER DEFAULT 0, Attiva INTEGER DEFAULT 1, ColoreTestoCategoria TEXT DEFAULT '#000000', Icona TEXT DEFAULT '',
  CentroProduzioneId INTEGER NOT NULL DEFAULT 0, EscludiComandaBanco INTEGER NOT NULL DEFAULT 0);
CREATE TABLE Prodotti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Codice TEXT NOT NULL UNIQUE, Descrizione TEXT NOT NULL, PrezzoCentesimi INTEGER NOT NULL,
  CategoriaId INTEGER NOT NULL, PathImmagine TEXT, Colore TEXT, IsPreferito INTEGER DEFAULT 0, Barcode TEXT, ColoreTestoTasto TEXT DEFAULT '#FFFFFF',
  Attivo INTEGER DEFAULT 1, CostoCentesimi INTEGER DEFAULT 0, DimensioneFontTasto INTEGER DEFAULT 12, CentroProduzioneId INTEGER NOT NULL DEFAULT 0,
  VenditaAPeso INTEGER NOT NULL DEFAULT 0, UnitaPeso TEXT DEFAULT 'Kg', IsCodiceBilancia INTEGER DEFAULT 0, IsCombinazione INTEGER NOT NULL DEFAULT 0,
  NumeroGusti INTEGER NOT NULL DEFAULT 2, PoliticaPrezzo INTEGER NOT NULL DEFAULT 0, VariantiProporzionali INTEGER NOT NULL DEFAULT 0,
  IsCoperto INTEGER NOT NULL DEFAULT 0, Ordine INTEGER DEFAULT 0, FOREIGN KEY(CategoriaId) REFERENCES Categorie(Id));
CREATE TABLE Varianti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL, Gruppo TEXT DEFAULT '', PrezzoAggiuntaCent INTEGER NOT NULL DEFAULT 0,
  PrezzoRimozioneCent INTEGER NOT NULL DEFAULT 0, Ordine INTEGER DEFAULT 0, Attiva INTEGER DEFAULT 1, Tipo INTEGER NOT NULL DEFAULT 0);
CREATE TABLE CategorieVarianti (CategoriaId INTEGER NOT NULL, VarianteId INTEGER NOT NULL, PRIMARY KEY (CategoriaId, VarianteId));
''')
# Id, nome, reparto, iva, colore, testo, icona, uscita, escludi banco
cat = [
 (1,'PIZZE',1,10,'#E53935','#FFFFFF','🍕',3,0), (2,'PRIMI',1,10,'#FB8C00','#FFFFFF','🍝',1,0),
 (3,'SECONDI',1,10,'#6D4C41','#FFFFFF','🥩',1,0), (4,'CONTORNI',1,10,'#43A047','#FFFFFF','🥗',1,0),
 (5,'BEVANDE',2,22,'#1E88E5','#FFFFFF','🥤',2,0), (6,'BIRRE',2,22,'#F9A825','#000000','🍺',2,0),
 (7,'CAFFETTERIA',2,22,'#5D4037','#FFFFFF','☕',2,1), (8,'DOLCI',1,10,'#D81B60','#FFFFFF','🍰',1,0),
 (9,'GASTRONOMIA',3,10,'#8D6E63','#FFFFFF','⚖️',0,0), (10,'SERVIZIO',1,10,'#607D8B','#FFFFFF','🍽️',0,0)]
for o,(i,n,r,iva,col,t,ico,u,e) in enumerate(cat):
    c.execute('INSERT INTO Categorie VALUES (?,?,?,?,?,?,1,?,?,?,?)',(i,n,r,iva,col,o,t,ico,u,e))
P=[]  # (cat, descr, cent, pref, barcode, extra dict)
def a(cat_, d, p, pref=0, bc=None, **k): P.append((cat_, d, p, pref, bc, k))
for d,p in [('MARGHERITA',600),('MARINARA',500),('DIAVOLA',750),('CAPRICCIOSA',850),('QUATTRO FORMAGGI',850),('BUFALA',900),
            ('NAPOLI',700),('VEGETARIANA',800),('WURSTEL E PATATINE',750),('CALZONE',800),('PROSCIUTTO E FUNGHI',850),('TONNO E CIPOLLA',800)]:
    a(1,d,p,1 if d in ('MARGHERITA','DIAVOLA') else 0)
a(1,'PIZZA ½ + ½',0,1,IsCombinazione=1,NumeroGusti=2,PoliticaPrezzo=0,VariantiProporzionali=1,Colore='#B71C1C')
a(1,'PIZZA 3 GUSTI',0,0,IsCombinazione=1,NumeroGusti=3,PoliticaPrezzo=1,VariantiProporzionali=1,Colore='#B71C1C')
for d,p in [('SPAGHETTI POMODORO',800),('PENNE ARRABBIATA',850),('CARBONARA',1000),('GNOCCHI SORRENTINA',950),('RISOTTO FUNGHI',1100),('LASAGNA',1000)]: a(2,d,p)
for d,p in [('COTOLETTA',1100),('HAMBURGER',900),('TAGLIATA',1600),('GRIGLIATA MISTA',1500),('FRITTURA DI PESCE',1400)]: a(3,d,p)
for d,p in [('PATATINE FRITTE',400),('INSALATA MISTA',450),('VERDURE GRIGLIATE',500),('PATATE AL FORNO',400)]: a(4,d,p)
for d,p,bc in [('ACQUA NATURALE 50cl',150,'8001234560017'),('ACQUA FRIZZANTE 50cl',150,'8001234560024'),('COCA COLA 33cl',300,'5449000000996'),
               ('FANTA 33cl',300,'5449000011527'),('SPRITE 33cl',300,'5449000014535'),('ACQUA 1L',300,None),('VINO ROSSO 1/4',450,None),('VINO BIANCO 1/4',450,None)]:
    a(5,d,p,1 if 'ACQUA NATURALE' in d or 'COCA' in d else 0,bc)
for d,p in [('BIRRA PICCOLA',350),('BIRRA MEDIA',550),('BIRRA BOTTIGLIA 66cl',500),('BIRRA ARTIGIANALE',650)]: a(6,d,p)
for d,p in [('CAFFÈ',120),('DECAFFEINATO',130),('CAPPUCCINO',150),('CAFFÈ CORRETTO',200),('AMARO',350),('LIMONCELLO',300)]: a(7,d,p,1 if d=='CAFFÈ' else 0)
for d,p in [('TIRAMISÙ',500),('PANNA COTTA',450),('BABÀ',400),('SFOGLIATELLA',250)]: a(8,d,p)
a(9,'MOZZARELLA DI BUFALA',1600,0,'00011',VenditaAPeso=1,UnitaPeso='Kg',IsCodiceBilancia=1)
a(9,'PROSCIUTTO CRUDO',350,0,'00012',VenditaAPeso=1,UnitaPeso='Etto',IsCodiceBilancia=1)
a(9,'SALAME NAPOLI',280,0,'00013',VenditaAPeso=1,UnitaPeso='Etto',IsCodiceBilancia=1)
a(9,'PROVOLA AFFUMICATA',1400,0,None,VenditaAPeso=1,UnitaPeso='Kg')
a(10,'COPERTO',200,0,IsCoperto=1)
a(10,'COPERTO TERRAZZA',300,0,IsCoperto=1)
a(10,'ARTICOLO A PREZZO LIBERO',0,0)
for i,(ct,d,p,pref,bc,k) in enumerate(P, start=1):
    colore = k.pop('Colore', dict((x[0],x[4]) for x in cat)[ct])
    testo = dict((x[0],x[5]) for x in cat)[ct]
    cols = {'Codice':str(i),'Descrizione':d,'PrezzoCentesimi':p,'CategoriaId':ct,'Colore':colore,'IsPreferito':pref,'Barcode':bc,
            'ColoreTestoTasto':testo,'Attivo':1,'CostoCentesimi':int(p*0.35),'DimensioneFontTasto':12,'Ordine':i}
    cols.update(k)
    c.execute(f"INSERT INTO Prodotti ({','.join(cols)}) VALUES ({','.join('?'*len(cols))})", list(cols.values()))
# varianti: (nome, gruppo, +cent, -cent, tipo, categorie)
V = [('MOZZARELLA','AGGIUNTE',100,0,0,[1]),('BUFALA','AGGIUNTE',200,0,0,[1]),('PROSCIUTTO COTTO','AGGIUNTE',150,0,0,[1]),
     ('FUNGHI','AGGIUNTE',100,0,0,[1,2]),('SALSICCIA','AGGIUNTE',150,0,0,[1]),('PATATINE','AGGIUNTE',100,0,0,[1,3]),
     ('CIPOLLA','TOGLI / AGGIUNGI',50,0,0,[1,4]),('AGLIO','TOGLI / AGGIUNGI',0,0,0,[1,2]),('ORIGANO','TOGLI / AGGIUNGI',0,0,0,[1]),
     ('BASILICO','TOGLI / AGGIUNGI',0,0,0,[1,2]),('IMPASTO INTEGRALE','IMPASTO',150,0,1,[1]),('SENZA GLUTINE','IMPASTO',300,0,1,[1,2]),
     ('BEN COTTA','COTTURA',0,0,1,[1,3]),('POCO COTTA','COTTURA',0,0,1,[1]),('AL SANGUE','COTTURA',0,0,1,[3]),('MEDIA','COTTURA',0,0,1,[3]),
     ('PICCANTE','GUSTO',0,0,1,[2,3]),('PARMIGIANO','AGGIUNTE',50,0,0,[2]),('CON GHIACCIO','SERVIZIO',0,0,1,[5,6]),
     ('CON LIMONE','SERVIZIO',0,0,1,[5]),('MACCHIATO','SERVIZIO',0,0,1,[7]),('IN TAZZA GRANDE','SERVIZIO',0,0,1,[7]),
     ('PANNA','AGGIUNTE',50,0,0,[7,8]),('MAIONESE','SALSE',30,0,0,[3,4]),('KETCHUP','SALSE',30,0,0,[3,4])]
for o,(n,g,piu,meno,t,cats) in enumerate(V, start=1):
    cur = c.execute('INSERT INTO Varianti (Nome,Gruppo,PrezzoAggiuntaCent,PrezzoRimozioneCent,Ordine,Attiva,Tipo) VALUES (?,?,?,?,?,1,?)',(n,g,piu,meno,o,t))
    for ct in cats: c.execute('INSERT INTO CategorieVarianti VALUES (?,?)',(ct,cur.lastrowid))
c.commit()
print(c.execute('select count(*) from Categorie').fetchone(), c.execute('select count(*) from Prodotti').fetchone(), c.execute('select count(*) from Varianti').fetchone(), c.execute('PRAGMA integrity_check').fetchone())
c.close()
