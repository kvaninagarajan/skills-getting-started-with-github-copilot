using System.Globalization;
using System.IO.Compression;
using AddressMapper.Model;
using ExcelDataReader;

namespace AddressMapper.Api;

public static class ExcelImport
{
    public static List<AddressLocation> Read(Stream stream)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Count > 200 || archive.Entries.Sum(x => x.Length) > 10 * 1024 * 1024)
                throw new ArgumentException("Workbook contents exceed the 10 MB/200-entry limit.");
        }
        stream.Position = 0;
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        if (!reader.Read())
            throw new ArgumentException("The first worksheet is empty.");
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 0; column < reader.FieldCount; column++)
        {
            var header = Text(reader.GetValue(column));
            if (header.Length > 0 && !headers.TryAdd(header, column))
                throw new ArgumentException($"Duplicate column: {header}.");
        }
        if (!headers.ContainsKey("addressId") || !headers.ContainsKey("address"))
            throw new ArgumentException("The first row must contain addressId and address columns.");
        if (headers.ContainsKey("latitude") != headers.ContainsKey("longitude"))
            throw new ArgumentException("Provide both latitude and longitude columns, or neither.");

        var rows = new List<AddressLocation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rowNumber = 1;
        while (reader.Read())
        {
            rowNumber++;
            if (rowNumber > 1000)
                throw new ArgumentException("The first worksheet must contain at most 1,000 rows including blanks.");
            if (Enumerable.Range(0, reader.FieldCount).All(i => string.IsNullOrWhiteSpace(Text(reader.GetValue(i)))))
                continue;
            string Cell(string name) => headers.TryGetValue(name, out var column)
                ? Text(reader.GetValue(column)) : "";
            var id = Cell("addressId");
            var address = Cell("address");
            if (id.Length is 0 or > 100 || address.Length is 0 or > 500)
                throw new ArgumentException($"Row {rowNumber}: addressId (1–100 characters) and address (1–500) are required.");
            if (!ids.Add(id))
                throw new ArgumentException($"Row {rowNumber}: duplicate addressId.");
            var row = new AddressLocation { AddressId = id, Address = address };
            var latitude = Cell("latitude");
            var longitude = Cell("longitude");
            if (latitude.Length > 0 || longitude.Length > 0)
            {
                if (!double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    || !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                    || !double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180)
                    throw new ArgumentException($"Row {rowNumber}: supply valid latitude (-90..90) and longitude (-180..180).");
                row.Latitude = lat;
                row.Longitude = lon;
                row.Status = "located";
            }
            rows.Add(row);
            if (rows.Count > 50)
                throw new ArgumentException("Import at most 50 addresses at a time.");
        }
        if (rows.Count == 0)
            throw new ArgumentException("The worksheet has no addresses.");
        return rows;
    }

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
}
