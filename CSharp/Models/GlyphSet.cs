namespace Bfontb.Models;

/// <summary>
/// Un carattere da mostrare nella pagina dello stile: un punto di codice Unicode, oppure una forma alternativa
/// (legatura, set stilistico, maiuscoletto…) che si ottiene accendendo una funzione OpenType sul testo indicato.
/// </summary>
/// <param name="Id">Posizione nell'elenco completo dello stile (per la selezione e le frecce).</param>
/// <param name="Text">Il testo da disegnare.</param>
/// <param name="CodePoint">Il punto di codice, per i caratteri codificati.</param>
/// <param name="Feature">Il valore CSS di font-feature-settings, per le forme alternative (es. 'ss01' 1).</param>
/// <param name="Glyph">Il glifo del file che si vede (se è uno solo), per misurarne il disegno (vedi GlyphSet.InkOf).</param>
public sealed record GlyphItem(int Id, string Text, int? CodePoint, string? Feature, int? Glyph = null);

/// <summary>
/// Il disegno di un glifo, in em, rispetto al punto da cui parte: da sinistra a destra, dal basso all'alto
/// (0 è la linea di base), e la larghezza teorica (avanzamento).
/// </summary>
public sealed record InkBox(double Left, double Right, double Bottom, double Top, double Advance)
{
    public double Center => (Left + Right) / 2;
}

/// <summary>Un gruppo di caratteri: un blocco Unicode (Latino di base…) o una funzione OpenType (Legature…).</summary>
public sealed record GlyphGroup(string Key, string Title, string? Note, List<GlyphItem> Items);

/// <summary>Le misure verticali del carattere, in unità del font.</summary>
public sealed record GlyphMetrics(int UnitsPerEm, int Ascender, int Descender, int? CapHeight, int? XHeight);

/// <summary>Tutto quello che il file contiene da mostrare: caratteri codificati, forme alternative, misure.</summary>
public sealed class GlyphSet
{
    public GlyphMetrics? Metrics { get; init; }
    public List<GlyphGroup> Blocks { get; init; } = [];
    public List<GlyphGroup> Features { get; init; } = [];

    /// <summary>Glifi disegnati nel file (tabella maxp), compresi quelli raggiungibili solo con regole contestuali.</summary>
    public int TotalGlyphs { get; init; }

    /// <summary>Quanti glifi diversi del file si vedono qui (codificati o come forme alternative).</summary>
    public int Reachable { get; init; }

    public int Encoded => Blocks.Sum(b => b.Items.Count);
    public int Alternates => Features.Sum(f => f.Items.Count);

    /// <summary>Tutti gli elementi nell'ordine in cui compaiono, per le frecce avanti e indietro.</summary>
    public List<GlyphItem> All { get; init; } = [];

    public string? Error { get; init; }

    /// <summary>Il disegno di un glifo del file (vedi GlyphBounds); null se il formato non si sa leggere.</summary>
    public Func<int, InkBox?>? InkOf { get; init; }

    public InkBox? Ink(GlyphItem? it) => it?.Glyph is int g ? InkOf?.Invoke(g) : null;
}
