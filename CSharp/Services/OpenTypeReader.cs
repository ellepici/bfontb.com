using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Legge i crediti e i dati tecnici da un file OpenType/TrueType (anche .ttc e .woff).
/// Tabelle usate: name (nomi e crediti), OS/2 (peso, larghezza, corsivo, fornitore, classe, PANOSE),
/// post (spaziatura fissa), fvar (font variabile), maxp (numero di glifi).
/// </summary>
public static partial class OpenTypeReader
{
    /// <summary>Nota mostrata nei crediti dei file woff2 (anche quelli letti da una versione precedente, salvati in italiano).</summary>
    public const string WoffNote = "woff2 files don’t contain credits that can be read here: open the OTF or TTF version.";

    public static FontInfo Read(byte[] data)
    {
        if (data.Length < 12) return new FontInfo { Error = true, Schema = FontInfo.CurrentSchema };
        var sig = Tag(data, 0);
        if (sig == "wOF2") return new FontInfo { Note = WoffNote, Schema = FontInfo.CurrentSchema };

        var tables = sig == "wOFF" ? WoffTables(data) : SfntTables(data, sig == "ttcf" ? (int)U32(data, 12) : 0);
        var info = new FontInfo { Schema = FontInfo.CurrentSchema };

        if (tables.TryGetValue("name", out var name)) ReadNames(name(), info);
        if (tables.TryGetValue("OS/2", out var os2))
        {
            var t = os2();
            if (t.Length >= 8) { info.WeightClass = U16(t, 4); info.WidthClass = U16(t, 6); }
            if (t.Length >= 42)
            {
                info.FamilyClass = t[30];          // byte alto di sFamilyClass
                info.PanoseFamily = t[32];
                info.PanoseSerif = t[33];
                info.PanoseProportion = t[35];
            }
            if (t.Length >= 64) { info.Vendor = Tag(t, 58).Trim(); info.FsSelection = U16(t, 62); }
        }
        if (tables.TryGetValue("post", out var post)) { var t = post(); if (t.Length >= 16) info.FixedPitch = U32(t, 12) != 0; }
        info.IsVariable = tables.ContainsKey("fvar");
        if (tables.TryGetValue("maxp", out var maxp)) { var t = maxp(); if (t.Length >= 6) info.Glyphs = U16(t, 4); }
        return info;
    }

    // ---- table directories ----

    /// <summary>Le tabelle del file (anche .ttc, dove si usa il primo carattere, e .woff); null per woff2 o file non validi.</summary>
    internal static Dictionary<string, Func<byte[]>>? Tables(byte[] data)
    {
        if (data.Length < 12) return null;
        var sig = Tag(data, 0);
        if (sig == "wOF2") return null;
        try { return sig == "wOFF" ? WoffTables(data) : SfntTables(data, sig == "ttcf" ? (int)U32(data, 12) : 0); }
        catch { return null; }
    }

    private static Dictionary<string, Func<byte[]>> SfntTables(byte[] d, int b)
    {
        var tables = new Dictionary<string, Func<byte[]>>();
        int n = U16(d, b + 4);
        for (int i = 0; i < n; i++)
        {
            int o = b + 12 + i * 16;
            if (o + 16 > d.Length) break;
            string tag = Tag(d, o);
            int off = (int)U32(d, o + 8), len = (int)U32(d, o + 12);
            if (off < 0 || len < 0 || off + len > d.Length) continue;
            tables[tag] = () => d.AsSpan(off, len).ToArray();
        }
        return tables;
    }

    private static Dictionary<string, Func<byte[]>> WoffTables(byte[] d)
    {
        var tables = new Dictionary<string, Func<byte[]>>();
        int n = U16(d, 12);
        for (int i = 0; i < n; i++)
        {
            int o = 44 + i * 20;
            if (o + 20 > d.Length) break;
            string tag = Tag(d, o);
            int off = (int)U32(d, o + 4), comp = (int)U32(d, o + 8), orig = (int)U32(d, o + 12);
            if (off + comp > d.Length) continue;
            tables[tag] = () =>
            {
                if (comp >= orig) return d.AsSpan(off, orig).ToArray();
                using var z = new ZLibStream(new MemoryStream(d, off, comp), CompressionMode.Decompress);
                using var ms = new MemoryStream(orig);
                z.CopyTo(ms);
                return ms.ToArray();
            };
        }
        return tables;
    }

    // ---- name table ----

    private static void ReadNames(byte[] t, FontInfo info)
    {
        if (t.Length < 6) return;
        int count = U16(t, 2), strOff = U16(t, 4);
        var best = new Dictionary<int, (int Score, string Text)>();
        for (int i = 0; i < count; i++)
        {
            int r = 6 + i * 12;
            if (r + 12 > t.Length) break;
            int pid = U16(t, r), lid = U16(t, r + 4), nid = U16(t, r + 6), len = U16(t, r + 8), off = U16(t, r + 10);
            int score = pid == 3 ? (lid == 0x409 ? 4 : 3) : pid == 0 ? 2 : pid == 1 && lid == 0 ? 1 : 0;
            if (score == 0 || (best.TryGetValue(nid, out var have) && have.Score >= score)) continue;
            int start = strOff + off;
            if (start < 0 || start + len > t.Length) continue;
            var span = t.AsSpan(start, len);
            string s = pid == 1 ? Encoding.Latin1.GetString(span) : Encoding.BigEndianUnicode.GetString(span);
            s = s.Replace("\0", "").Trim();
            if (s.Length > 0) best[nid] = (score, s);
        }
        string? G(int id) => best.TryGetValue(id, out var v) ? v.Text : null;

        info.Copyright = G(0);
        info.Family = G(16) ?? G(1);
        info.Sub = G(17) ?? G(2);
        info.Full = G(4);
        info.Version = G(5);
        info.Trademark = G(7);
        info.Foundry = G(8);
        info.Designer = G(9);
        info.Description = G(10);
        info.VendorUrl = G(11);
        info.DesignerUrl = G(12);
        info.License = G(13);
        info.LicenseUrl = G(14);
        FixEmbeddedNames(info);

        var years = YearRe().Matches($"{info.Copyright} {info.Trademark}").Select(m => int.Parse(m.Value)).Order().ToList();
        if (years.Count > 0) info.Year = years[0] == years[^1] ? years[0].ToString() : $"{years[0]}–{years[^1]}";

        if (info.Foundry is null && info.Copyright is not null)
        {
            var m = CopyrightOwnerRe().Match(info.Copyright);
            if (m.Success)
            {
                var guess = m.Groups[1].Value.Trim();
                if (!NotAnOwnerRe().IsMatch(guess))
                    info.FoundryGuess = AllRightsRe().Replace(guess, "").Trim();
            }
        }
    }

    // ---- helpers ----

    private static int U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));
    private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
    private static string Tag(byte[] d, int o) => Encoding.ASCII.GetString(d, o, 4);

    /// <summary>
    /// I font estratti da un PDF hanno come nome di famiglia il nome PostScript con davanti il segno del sottoinsieme:
    /// "ZOUTJY+Rubis-Black", "ZOUTJY+Rubis-BoldItalic"… così ogni peso sembrerebbe una famiglia a sé.
    /// Qui si toglie il segno e si separa la famiglia ("Rubis") dallo stile ("Black", "Bold Italic").
    /// Si applica anche ai dati già salvati nella cache dell'analisi (più volte non cambia niente).
    /// </summary>
    public static void FixEmbeddedNames(FontInfo info)
    {
        if (info.Family is not { } fam || !SubsetTagRe().IsMatch(fam)) return;
        info.Subset = true;
        // dentro i PDF le parentesi dei testi sono scritte con la barra davanti: "\(c\) 2015 by…" → "(c) 2015 by…"
        static string? Unescape(string? s) => s?.Replace("\\(", "(").Replace("\\)", ")");
        info.Copyright = Unescape(info.Copyright); info.Trademark = Unescape(info.Trademark);
        info.Foundry = Unescape(info.Foundry); info.FoundryGuess = Unescape(info.FoundryGuess); info.Designer = Unescape(info.Designer);
        fam = SubsetTagRe().Replace(fam, "");
        if (info.Full is not null) info.Full = SubsetTagRe().Replace(info.Full, "");
        int dash = fam.IndexOf('-');
        if (!fam.Contains(' ') && dash > 0 && dash < fam.Length - 1)
        {
            string tail = fam[(dash + 1)..];
            static string Squash(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (info.Sub is null || Squash(info.Sub) == Squash(tail) || Squash(info.Sub) == "regular")
            {
                var file = fam + ".otf";
                info.Family = StyleNames.GuessFamily(file);   // "NeueHaas-Bold" → "Neue Haas"
                info.Sub = StyleNames.StyleLabel(file);        // "BoldItalic" → "Bold Italic"
                info.Full = $"{info.Family} {info.Sub}";
                return;
            }
        }
        info.Family = fam;
    }

    [GeneratedRegex(@"^[A-Z]{6}\+")] private static partial Regex SubsetTagRe();
    [GeneratedRegex(@"\b(1[89]\d{2}|20\d{2})\b")] private static partial Regex YearRe();
    [GeneratedRegex(@"(?:\(c\)|©|copyright)\s*(?:\d{4}(?:\s*[-–,]\s*\d{4})*)?\s*(?:by\s+)?([^.;,\n]{2,60})", RegexOptions.IgnoreCase)] private static partial Regex CopyrightOwnerRe();
    [GeneratedRegex(@"^(for|of|the|by) ", RegexOptions.IgnoreCase)] private static partial Regex NotAnOwnerRe();
    [GeneratedRegex("all rights reserved", RegexOptions.IgnoreCase)] private static partial Regex AllRightsRe();
}
