using System.Text;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Sinema.Domain;

public enum SeatType { Standard, Vip, Accessible, Couple }

/// <summary>Salondaki bir koltuk. Kod sıra harfi + koltuk numarasıdır (ör. "C7").</summary>
public sealed record SeatInfo(string Code, int Row, int Col, char RowLetter, int Number, SeatType Type);

/// <summary>
/// Salonun koltuk planı. Metin olarak saklanır: her satır bir sıra, satırlar "/" ile ayrılır.
/// <c>S</c> standart, <c>V</c> VIP, <c>E</c> engelli erişimli, <c>C</c> çift (sevgili) koltuk, <c>.</c> boşluk/koridor.
/// Sıra harfleri perdeye en yakın sıradan başlar; tamamen boş satırlar (ara koridor) harf almaz.
/// Koltuk numaraları her sırada soldan sağa artar.
/// </summary>
public sealed class HallLayout
{
    public const int MaxRows = 26;
    public const int MaxCols = 40;
    const string Letters = "ABCDEFGHIJKLMNOPRSTUVYZQWX";

    readonly char[][] _cells;

    HallLayout(char[][] cells)
    {
        _cells = cells;
        Seats = BuildSeats(cells);
    }

    public int Rows => _cells.Length;
    public int Cols => _cells.Length == 0 ? 0 : _cells.Max(r => r.Length);
    public IReadOnlyList<SeatInfo> Seats { get; }
    public int Capacity => Seats.Count;

    public char Cell(int row, int col) => row < _cells.Length && col < _cells[row].Length ? _cells[row][col] : '.';

    public SeatInfo? Find(string code) => Seats.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

    public int Count(SeatType type) => Seats.Count(s => s.Type == type);

    public static HallLayout Parse(string? text)
    {
        var rows = (text ?? "").Split('/', StringSplitOptions.None).Select(r => r.Trim().ToUpperInvariant()).ToList();
        while (rows.Count > 0 && rows[^1].Length == 0) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) throw new DomainException("Koltuk planı boş olamaz.");
        if (rows.Count > MaxRows) throw new DomainException($"Bir salonda en fazla {MaxRows} sıra olabilir.");
        var width = rows.Max(r => r.Length);
        if (width > MaxCols) throw new DomainException($"Bir sırada en fazla {MaxCols} hücre olabilir.");
        var cells = new char[rows.Count][];
        for (var r = 0; r < rows.Count; r++)
        {
            var line = rows[r].PadRight(width, '.');
            foreach (var ch in line)
                if ("SVEC.".IndexOf(ch) < 0) throw new DomainException($"Koltuk planında geçersiz karakter: '{ch}'.");
            cells[r] = line.ToCharArray();
        }
        var layout = new HallLayout(cells);
        if (layout.Capacity == 0) throw new DomainException("Salonda en az bir koltuk olmalı.");
        var letterRows = cells.Count(r => r.Any(c => c != '.'));
        if (letterRows > Letters.Length) throw new DomainException("Çok fazla sıra var.");
        return layout;
    }

    /// <summary>Boş bir plan (hepsi standart koltuk, ortada koridor).</summary>
    public static HallLayout Grid(int rows, int cols, int aisleAt = -1)
    {
        rows = Math.Clamp(rows, 1, MaxRows);
        cols = Math.Clamp(cols, 1, MaxCols);
        var sb = new StringBuilder();
        for (var r = 0; r < rows; r++)
        {
            if (r > 0) sb.Append('/');
            for (var c = 0; c < cols; c++) sb.Append(c == aisleAt ? '.' : 'S');
        }
        return Parse(sb.ToString());
    }

    /// <summary>Bir hücreyi değiştirir ve yeni planı döndürür (tasarımcı bunu kullanır).</summary>
    public HallLayout With(int row, int col, char cell)
    {
        if ("SVEC.".IndexOf(cell) < 0) throw new DomainException("Geçersiz hücre.");
        var copy = _cells.Select(r => (char[])r.Clone()).ToArray();
        if (row < 0 || row >= copy.Length || col < 0 || col >= copy[row].Length) return this;
        copy[row][col] = cell;
        return new HallLayout(copy);
    }

    public HallLayout Resize(int rows, int cols)
    {
        rows = Math.Clamp(rows, 1, MaxRows);
        cols = Math.Clamp(cols, 1, MaxCols);
        var copy = new char[rows][];
        for (var r = 0; r < rows; r++)
        {
            copy[r] = new char[cols];
            for (var c = 0; c < cols; c++) copy[r][c] = r < _cells.Length && c < _cells[r].Length ? _cells[r][c] : 'S';
        }
        return new HallLayout(copy);
    }

    public HallLayout FillRow(int row, char cell)
    {
        var copy = _cells.Select(r => (char[])r.Clone()).ToArray();
        if (row < 0 || row >= copy.Length) return this;
        for (var c = 0; c < copy[row].Length; c++) copy[row][c] = cell;
        return new HallLayout(copy);
    }

    public HallLayout FillCol(int col, char cell)
    {
        var copy = _cells.Select(r => (char[])r.Clone()).ToArray();
        foreach (var r in copy)
            if (col >= 0 && col < r.Length) r[col] = cell;
        return new HallLayout(copy);
    }

    public override string ToString() => string.Join("/", _cells.Select(r => new string(r)));

    static List<SeatInfo> BuildSeats(char[][] cells)
    {
        var seats = new List<SeatInfo>();
        var letterIndex = 0;
        for (var r = 0; r < cells.Length; r++)
        {
            if (!cells[r].Any(c => c != '.')) continue;
            var letter = Letters[Math.Min(letterIndex++, Letters.Length - 1)];
            var number = 0;
            for (var c = 0; c < cells[r].Length; c++)
            {
                var type = cells[r][c] switch
                {
                    'S' => SeatType.Standard,
                    'V' => SeatType.Vip,
                    'E' => SeatType.Accessible,
                    'C' => SeatType.Couple,
                    _ => (SeatType?)null,
                };
                if (type is null) continue;
                number++;
                seats.Add(new SeatInfo($"{letter}{number}", r, c, letter, number, type.Value));
            }
        }
        return seats;
    }

    /// <summary>Hazır şablonlar (tasarımcıda tek tıkla).</summary>
    public static IReadOnlyList<(string Name, string Layout)> Templates { get; } =
    [
        ("Küçük salon (≈70)", Build(8, 10, [5], vipRows: 0, accessibleRow: 7)),
        ("Orta salon (≈130)", Build(11, 14, [3, 11], vipRows: 0, accessibleRow: 10)),
        ("Büyük salon (≈210)", Build(13, 20, [4, 16], vipRows: 2, accessibleRow: 12)),
        ("VIP salon (≈40)", "VV.VVVV.VV/VV.VVVV.VV/VV.VVVV.VV/........../CC.CCCC.CC/CC.CCCC.CC"),
    ];

    /// <summary>Koridorlu plan üretir: son <paramref name="vipRows"/> sıra VIP, <paramref name="accessibleRow"/> sırasının uçları engelli.</summary>
    public static string Build(int rows, int seatsPerRow, int[] aisles, int vipRows, int accessibleRow)
    {
        var sb = new StringBuilder();
        for (var r = 0; r < rows; r++)
        {
            if (r > 0) sb.Append('/');
            var vip = r >= rows - vipRows;
            for (var c = 0; c < seatsPerRow; c++)
            {
                if (aisles.Contains(c)) sb.Append('.');
                var edge = c == 0 || c == seatsPerRow - 1;
                sb.Append(r == accessibleRow && edge ? 'E' : vip ? 'V' : 'S');
            }
        }
        return sb.ToString();
    }
}

/// <summary>Sinema salonu. Koltuk planı ileri tarihli bileti olan seanslar varken değiştirilemez (altyapı kontrol eder).</summary>
public sealed class Hall : FullAuditableEntity
{
    private Hall() { }

    public Hall(string name, string layout, string? features = null, bool supports3D = false)
    {
        Rename(name);
        SetLayout(layout);
        Features = features;
        Supports3D = supports3D;
        IsActive = true;
    }

    public string Name { get; private set; } = "";
    public string Layout { get; private set; } = "";
    public int Capacity { get; private set; }
    /// <summary>Ses/görüntü özellikleri (ör. "Dolby Atmos · Lazer projeksiyon").</summary>
    public string? Features { get; private set; }
    public bool Supports3D { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public HallLayout ParsedLayout() => HallLayout.Parse(Layout);

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Salon adı boş olamaz.");
        Name = name.Trim();
    }

    public void SetLayout(string layout)
    {
        var parsed = HallLayout.Parse(layout);
        Layout = parsed.ToString();
        Capacity = parsed.Capacity;
    }

    public void SetFeatures(string? features, bool supports3D)
    {
        Features = string.IsNullOrWhiteSpace(features) ? null : features.Trim();
        Supports3D = supports3D;
    }

    public void SetActive(bool active) => IsActive = active;

    public void SetSortOrder(int order) => SortOrder = order;
}
