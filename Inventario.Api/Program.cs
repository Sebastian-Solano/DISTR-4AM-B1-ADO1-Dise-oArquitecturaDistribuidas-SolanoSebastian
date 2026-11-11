using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<InventarioDb>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("InventarioDB")));
builder.Services.AddHttpClient("catalogo", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Servicios:Catalogo"]!);
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddOpenApi();
var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "Inventario.Api v1"));
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<InventarioDb>().Database.EnsureCreatedAsync();

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex) when ((ex as SqlException ?? ex.InnerException as SqlException)?.Number
        is 1205 or 2601 or 2627)
    {
        await Results.Conflict("Hubo un cambio simultáneo. Reintente la operación.").ExecuteAsync(context);
    }
});

app.MapGet("/api/bodegas", async (InventarioDb db) => await db.Bodegas.ToListAsync());
app.MapGet("/api/bodegas/{id:int}", async (int id, InventarioDb db) =>
    await db.Bodegas.FindAsync(id) is { } bodega ? Results.Ok(bodega) : Results.NotFound());
app.MapPost("/api/bodegas", async (NuevaBodega entrada, InventarioDb db) =>
{
    if (string.IsNullOrWhiteSpace(entrada.Nombre) || string.IsNullOrWhiteSpace(entrada.Ciudad) ||
        string.IsNullOrWhiteSpace(entrada.Direccion))
        return Results.BadRequest("Nombre, ciudad y dirección son obligatorios.");
    var bodega = new Bodega(entrada.Nombre, entrada.Ciudad, entrada.Direccion);
    db.Add(bodega);
    await db.SaveChangesAsync();
    return Results.Created($"/api/bodegas/{bodega.IdBodega}", bodega);
});
app.MapGet("/api/existencias", async (InventarioDb db) => await db.Existencias.ToListAsync());
app.MapGet("/api/movimientos", async (InventarioDb db) => await db.Movimientos.ToListAsync());
app.MapGet("/api/movimientos/{id:int}", async (int id, InventarioDb db) =>
    await db.Movimientos.FindAsync(id) is { } movimiento ? Results.Ok(movimiento) : Results.NotFound());
app.MapGet("/api/reservas", async (InventarioDb db) => await db.Reservas.ToListAsync());
app.MapGet("/api/reservas/{id:int}", async (int id, InventarioDb db) =>
    await db.Reservas.FindAsync(id) is { } reserva ? Results.Ok(reserva) : Results.NotFound());
app.MapGet("/api/existencias/{bodega:int}/{producto:int}", async (int bodega, int producto, InventarioDb db) =>
{
    if (await db.Bodegas.FindAsync(bodega) is null)
        return Results.NotFound("Bodega inexistente.");
    var stock = await db.Existencias
        .Where(e => e.IdBodega == bodega && e.IdProducto == producto)
        .Select(e => new
        {
            e.Stock,
            Reservado = db.Reservas.Where(r => r.IdBodega == bodega && r.IdProducto == producto &&
                r.Estado == "Activa").Sum(r => r.Cantidad)
        }).SingleOrDefaultAsync();
    return Results.Ok(new { idBodega = bodega, idProducto = producto,
        stock = stock?.Stock ?? 0, disponible = (stock?.Stock ?? 0) - (stock?.Reservado ?? 0) });
});

app.MapPost("/api/movimientos", async (NuevoMovimiento entrada, InventarioDb db, IHttpClientFactory http,
    CancellationToken ct) =>
{
    if (entrada.Tipo is not ("Entrada" or "Salida"))
        return Results.BadRequest("Tipo debe ser Entrada o Salida.");
    if (await ValidarReferencia(entrada.IdBodega, entrada.IdProducto, entrada.Cantidad, db, http, ct)
        is { } error)
        return error;

    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var existencia = await BuscarExistencia(db, entrada.IdBodega, entrada.IdProducto, ct);
    var stock = existencia?.Stock ?? 0;
    if (entrada.Tipo == "Salida" &&
        entrada.Cantidad > stock - await Reservado(db, entrada.IdBodega, entrada.IdProducto, ct))
        return Results.Conflict("Stock disponible insuficiente.");
    if (entrada.Tipo == "Entrada" && entrada.Cantidad > int.MaxValue - stock)
        return Results.BadRequest("La entrada supera el stock máximo.");

    var nuevoStock = stock + (entrada.Tipo == "Entrada" ? entrada.Cantidad : -entrada.Cantidad);
    if (existencia is null)
        db.Add(new Existencia(entrada.IdProducto, entrada.IdBodega) { Stock = nuevoStock });
    else
        existencia.Stock = nuevoStock;
    var movimiento = new MovimientoInventario(entrada.IdProducto, entrada.IdBodega,
        entrada.Tipo, entrada.Cantidad, DateTimeOffset.UtcNow);
    db.Add(movimiento);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Created($"/api/movimientos/{movimiento.IdMovimiento}", movimiento);
});
app.MapPost("/api/reservas", async (NuevaReserva entrada, InventarioDb db, IHttpClientFactory http,
    CancellationToken ct) =>
{
    if (await ValidarReferencia(entrada.IdBodega, entrada.IdProducto, entrada.Cantidad, db, http, ct)
        is { } error)
        return error;

    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var existencia = await BuscarExistencia(db, entrada.IdBodega, entrada.IdProducto, ct);
    if (entrada.Cantidad > (existencia?.Stock ?? 0) -
        await Reservado(db, entrada.IdBodega, entrada.IdProducto, ct))
        return Results.Conflict("Stock disponible insuficiente.");
    var reserva = new ReservaStock(entrada.IdProducto, entrada.IdBodega, entrada.Cantidad);
    db.Add(reserva);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Created($"/api/reservas/{reserva.IdReserva}", reserva);
});
app.MapPatch("/api/reservas/{id:int}/liberar", async (int id, InventarioDb db) =>
{
    if (await db.Reservas.FindAsync(id) is not { } reserva)
        return Results.NotFound();
    reserva.Estado = "Liberada";
    await db.SaveChangesAsync();
    return Results.Ok(reserva);
});
app.Run();

static Task<Existencia?> BuscarExistencia(InventarioDb db, int bodega, int producto, CancellationToken ct) =>
    db.Existencias.SingleOrDefaultAsync(e => e.IdBodega == bodega && e.IdProducto == producto, ct);

static Task<int> Reservado(InventarioDb db, int bodega, int producto, CancellationToken ct) =>
    db.Reservas.Where(r => r.IdBodega == bodega && r.IdProducto == producto && r.Estado == "Activa")
        .SumAsync(r => r.Cantidad, ct);

static async Task<IResult?> ValidarReferencia(int bodega, int producto, int cantidad, InventarioDb db,
    IHttpClientFactory http, CancellationToken ct)
{
    if (bodega <= 0 || producto <= 0 || cantidad <= 0)
        return Results.BadRequest("Bodega, producto y cantidad deben ser positivos.");
    if (await db.Bodegas.FindAsync([bodega], ct) is null)
        return Results.NotFound("Bodega inexistente.");
    try
    {
        using var response = await http.CreateClient("catalogo").GetAsync($"api/productos/{producto}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Results.NotFound("Producto inexistente.");
        if (response.IsSuccessStatusCode)
            return null;
    }
    catch (HttpRequestException) { }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    return Results.Problem("Catálogo no disponible.", statusCode: 503);
}

record NuevaBodega(string Nombre, string Ciudad, string Direccion);
record NuevoMovimiento(int IdProducto, int IdBodega, string Tipo, int Cantidad);
record NuevaReserva(int IdProducto, int IdBodega, int Cantidad);
record Bodega(string Nombre, string Ciudad, string Direccion)
{
    [Key] public int IdBodega { get; set; }
}
[Index(nameof(IdBodega), nameof(IdProducto), IsUnique = true)]
record Existencia(int IdProducto, int IdBodega)
{
    [Key] public int IdExistencia { get; set; }
    public int Stock { get; set; }
}
record MovimientoInventario(int IdProducto, int IdBodega, string Tipo, int Cantidad, DateTimeOffset Fecha)
{
    [Key] public int IdMovimiento { get; set; }
}
[Index(nameof(IdBodega), nameof(IdProducto), nameof(Estado))]
record ReservaStock(int IdProducto, int IdBodega, int Cantidad)
{
    [Key] public int IdReserva { get; set; }
    [MaxLength(16)] public string Estado { get; set; } = "Activa";
}

class InventarioDb(DbContextOptions<InventarioDb> options) : DbContext(options)
{
    public DbSet<Bodega> Bodegas => Set<Bodega>();
    public DbSet<Existencia> Existencias => Set<Existencia>();
    public DbSet<MovimientoInventario> Movimientos => Set<MovimientoInventario>();
    public DbSet<ReservaStock> Reservas => Set<ReservaStock>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Bodega>().ToTable("Bodega");
        model.Entity<Existencia>().ToTable("Existencia").HasOne<Bodega>().WithMany()
            .HasForeignKey(e => e.IdBodega).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MovimientoInventario>().ToTable("MovimientoInventario").HasOne<Bodega>().WithMany()
            .HasForeignKey(m => m.IdBodega).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ReservaStock>().ToTable("ReservaStock").HasOne<Bodega>().WithMany()
            .HasForeignKey(r => r.IdBodega).OnDelete(DeleteBehavior.Restrict);
    }
}

