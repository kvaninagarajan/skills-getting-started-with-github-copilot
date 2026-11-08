using AddressMapper.Api;
using AddressMapper.Model;
using Microsoft.EntityFrameworkCore;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "web"))
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.AddDbContext<AddressDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Addresses") ?? "Data Source=addresses.db"));
builder.Services.AddSingleton<Geocoder>();
builder.Services.AddSingleton<SemaphoreSlim>(_ => new(1, 1));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AddressDbContext>().Database.Migrate();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/addresses", async (AddressDbContext db, CancellationToken ct) =>
    await db.Addresses.AsNoTracking().OrderBy(x => x.AddressId).ToListAsync(ct));

app.MapPost("/api/addresses/import", async (HttpRequest request, AddressDbContext db,
    Geocoder geocoder, SemaphoreSlim importLock, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Use multipart/form-data with a file field." });

    List<AddressLocation> rows;
    bool geocode;
    try
    {
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || file.Length == 0 || file.Length > 1024 * 1024)
            return Results.BadRequest(new { error = "Upload one .xlsx file, at most 1 MB." });
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        rows = ExcelImport.Read(stream);
        geocode = string.Equals(form["geocode"], "true", StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is InvalidDataException or ArgumentException
        or System.Xml.XmlException or ExcelDataReader.Exceptions.HeaderException)
    {
        return Results.BadRequest(new { error = ex is ArgumentException
            ? ex.Message : "The file is not a valid .xlsx workbook." });
    }

    await importLock.WaitAsync(ct);
    try
    {
        var ids = rows.Select(x => x.AddressId).ToArray();
        var existing = await db.Addresses.Where(x => ids.Contains(x.AddressId))
            .ToDictionaryAsync(x => x.AddressId, ct);
        foreach (var row in rows)
        {
            existing.TryGetValue(row.AddressId, out var saved);
            if (row.Latitude is null && saved?.Address == row.Address && saved.Latitude is not null)
            {
                row.Latitude = saved.Latitude;
                row.Longitude = saved.Longitude;
                row.Status = saved.Status;
            }
            if (row.Latitude is null && geocode)
                await geocoder.LocateAsync(row, ct);
            if (saved is null)
                db.Addresses.Add(row);
            else
                db.Entry(saved).CurrentValues.SetValues(row);
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { imported = rows.Count, located = rows.Count(x => x.Latitude is not null), addresses = rows });
    }
    finally
    {
        importLock.Release();
    }
});

app.Run();
