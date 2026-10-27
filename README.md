# Banco di prova font — bfontb.com

App web per provare i font di una cartella senza installarli in Libro Font.
È scritta in C# con **Blazor WebAssembly**: il codice gira nel browser, quindi i font
vengono letti dal computer di chi usa l'app e non vengono mai caricati su un server.

## Struttura per linguaggio

```
bfontb.com/
├── bfontb.com.sln
├── bfontb.com.csproj          progetto Blazor WebAssembly (.NET 10)
├── CSharp/                    tutto il C#
│   ├── Program.cs             avvio e registrazione dei servizi
│   ├── Models/                dati: file, stili, famiglie, crediti, proposta, correzioni
│   ├── Services/              logica
│   │   ├── OpenTypeReader.cs  legge crediti e dati tecnici dai file OTF/TTF/TTC/WOFF
│   │   ├── StyleNames.cs      peso, corsivo, nome dello stile, famiglia e larghezza
│   │   ├── FamilyOrganizer.cs famiglie dalle cartelle o dall'analisi, superfamiglie, doppioni
│   │   ├── FontLibrary.cs     stato dell'app e preferenze
│   │   ├── FontLoader.cs      caricamento pigro dei font (6 alla volta)
│   │   ├── AnalysisRunner.cs  analisi della cartella con avanzamento e stop
│   │   ├── AnalysisCache.cs   memoria dell'analisi nel browser
│   │   ├── BrowserBridge.cs   chiamate verso il ponte JavaScript
│   │   ├── GlyphReader.cs     tutti i caratteri di un file: cmap, metriche, alternative (GSUB)
│   │   ├── GlyphLoader.cs     legge e ricorda i caratteri degli stili aperti
│   │   ├── UnicodeBlocks.cs   nomi italiani dei blocchi Unicode
│   │   ├── DemoFonts.cs       i sei caratteri di esempio
│   │   └── Fmt.cs             formattazione di numeri e collegamenti
│   └── Ui/                    code-behind dei componenti (stesso nome dei file .razor)
├── Html/                      markup dei componenti (.razor: solo HTML e collegamenti ai dati)
│   ├── App.razor              struttura della pagina
│   ├── Views/                 Catalogo, Famiglia, Stile, Confronto, Preparazione
│   └── Parts/                 riga di prova, crediti, percorso, griglia dei caratteri, famiglia nella proposta…
└── wwwroot/                   file statici pubblicati così come sono
    ├── index.html
    ├── css/                   tokens, layout, specimens, style-page, prep
    └── js/bfontb.js           ponte minimo verso le API del browser
```

Il file JavaScript contiene solo ciò che il C# non può fare da solo nel browser: scegliere
una cartella con i percorsi delle sottocartelle, gestire il trascinamento, registrare un
`FontFace`, sapere quando un elemento entra nello schermo e usare `localStorage`/IndexedDB.
Non contiene logica dell'app.

## Avviare in locale

Serve l'SDK di .NET 10.

```bash
dotnet run
```

Si apre su http://localhost:5111 (o https://localhost:7245): all'indirizzo `/` c'è la presentazione, l'app è su `/app`.

## Indirizzi

`wwwroot/index.html` fa due cose: all'indirizzo `/` mostra la presentazione (HTML semplice, senza avviare .NET),
da `/app` in poi avvia l'app. Dentro l'app ogni vista ha il suo indirizzo e la pagina non si ricarica:

| Indirizzo | Vista |
|---|---|
| `/app` | catalogo |
| `/app/family/abc-marfa` | pagina della famiglia |
| `/app/family/abc-marfa/mono-bold` | pagina di uno stile (variante e stile, senza il nome della famiglia) |
| `/app/settings`, `/app/credits` | impostazioni, crediti |
| `/app/report`, `/app/suggest` | segnalare un problema, proporre un'idea |

Gli indirizzi li calcola `FontLibrary` (`UrlFor`, `ApplyUrl`); `App` li aggiorna a ogni cambio di vista e
`bfontb.js` (`goUrl`, `popstate`) li passa al browser, così Indietro e Avanti funzionano. Filtri, ricerca, testo
e dimensioni non cambiano l'indirizzo. Un link a una famiglia aperto senza cartella resta in attesa e si apre appena
la cartella arriva.

## Pubblicare

```bash
dotnet publish -c Release -o publish
```

La cartella `publish/wwwroot` è un sito statico: si può pubblicare su Azure Static Web Apps,
GitHub Pages o qualsiasi hosting statico. L'hosting deve rispondere con `index.html` agli indirizzi che non sono file
(`/app/credits`…): per Azure Static Web Apps lo fa già `wwwroot/staticwebapp.config.json`, per IIS il `web.config`
creato dalla pubblicazione; Netlify e Cloudflare Pages vogliono un file `_redirects` con `/* /index.html 200`. Per una build più leggera conviene installare il
workload `wasm-tools` (`dotnet workload install wasm-tools`).

## Icone

Tutte le icone del progetto sono di [Phosphor](https://phosphoricons.com), peso regular (la stella piena usa il peso fill).
I tracciati sono in `CSharp/Ui/Icons.cs`, copiati dal pacchetto ufficiale `@phosphor-icons/core`; si usano con il componente `<Icon Name="…" />`.

## Numeri animati

I conteggi del catalogo (famiglie e stili che rispondono a filtri e ricerca) usano [NumberFlow](https://number-flow.barvian.me) di Maxwell Barvian, licenza MIT.
La libreria è copiata in `wwwroot/js/vendor/number-flow.js` (versione 0.6.2, raccolta in un solo file con esbuild), così funziona anche senza connessione.
Si usa con il componente `<FlowNumber Value="…" />`.
