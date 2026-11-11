using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PedidosDb>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PedidosDB")));
foreach (var servicio in new[] { "Catalogo", "Inventario" })
{
    builder.Services.AddHttpClient(servicio, client =>
    {
        client.BaseAddress = new Uri(builder.Configuration[$"Servicios:{servicio}"]!);
        client.Timeout = TimeSpan.FromSeconds(5);
    });
}
builder.Services.AddOpenApi();
var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "Pedidos.Api v1"));
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<PedidosDb>().Database.EnsureCreatedAsync();

app.MapGet("/api/clientes", async (PedidosDb db) => await db.Clientes.ToListAsync());
app.MapGet("/api/clientes/{id:int}", async (int id, PedidosDb db) =>
    await db.Clientes.FindAsync(id) is { } cliente ? Results.Ok(cliente) : Results.NotFound());
app.MapPost("/api/clientes", async (NuevoCliente entrada, PedidosDb db) =>
{
    if (string.IsNullOrWhiteSpace(entrada.Nombres) || entrada.Nombres.Length > 160 ||
        entrada.Cedula is not { Length: 10 } || entrada.Cedula.Any(c => !char.IsAsciiDigit(c)) ||
        string.IsNullOrWhiteSpace(entrada.Correo) || entrada.Correo.Length > 254 ||
        !new EmailAddressAttribute().IsValid(entrada.Correo))
        return Results.BadRequest("Indique nombres, cédula de diez dígitos y correo válido.");
    var cliente = new Cliente(entrada.Nombres.Trim(), entrada.Cedula, entrada.Correo.Trim());
    db.Add(cliente);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
    {
        return Results.Conflict("La cédula ya está registrada.");
    }
    return Results.Created($"/api/clientes/{cliente.IdCliente}", cliente);
});

app.MapGet("/api/pedidos", async (PedidosDb db) =>
    await db.Pedidos.Include(p => p.Detalles).ToListAsync());
app.MapGet("/api/pedidos/{id:int}", async (int id, PedidosDb db) =>
    await db.Pedidos.Include(p => p.Detalles).SingleOrDefaultAsync(p => p.IdPedido == id)
        is { } pedido ? Results.Ok(pedido) : Results.NotFound());
app.MapPost("/api/pedidos", async (NuevoPedido entrada, PedidosDb db, IHttpClientFactory http,
    CancellationToken ct) =>
{
    if (entrada.IdCliente <= 0 || entrada.IdBodega <= 0 || entrada.Detalles is not { Count: > 0 and <= 50 } ||
        entrada.Detalles.Any(d => d is null || d.IdProducto <= 0 || d.Cantidad is <= 0 or > 10000) ||
        entrada.Detalles.Select(d => d.IdProducto).Distinct().Count() != entrada.Detalles.Count)
        return Results.BadRequest("Indique cliente, bodega y entre 1 y 50 productos distintos con cantidades de 1 a 10000.");
    if (await db.Clientes.FindAsync([entrada.IdCliente], ct) is null)
        return Results.NotFound("Cliente inexistente.");

    var catalogo = http.CreateClient("Catalogo");
    var inventario = http.CreateClient("Inventario");
    var detalles = new List<DetallePedido>();
    try
    {
        foreach (var linea in entrada.Detalles)
        {
            using var producto = await catalogo.GetAsync($"api/productos/{linea.IdProducto}", ct);
            if (producto.StatusCode == HttpStatusCode.NotFound)
                return Results.NotFound($"Producto {linea.IdProducto} inexistente.");
            producto.EnsureSuccessStatusCode();
            var ficha = await producto.Content.ReadFromJsonAsync<FichaProducto>(ct);
            if (ficha is null || ficha.Precio <= 0 || ficha.Precio > 9999999999.99m ||
                decimal.Round(ficha.Precio, 2) != ficha.Precio)
                return Results.Problem("Precio inválido en catálogo.", statusCode: 502);
            if (!ficha.Estado)
                return Results.Conflict($"Producto {linea.IdProducto} inactivo.");

            using var existencia = await inventario.GetAsync(
                $"api/existencias/{entrada.IdBodega}/{linea.IdProducto}", ct);
            if (existencia.StatusCode == HttpStatusCode.NotFound)
                return Results.NotFound("Bodega inexistente.");
            existencia.EnsureSuccessStatusCode();
            var stock = await existencia.Content.ReadFromJsonAsync<StockDisponible>(ct);
            if (stock is null)
                return Results.Problem("Respuesta inválida de inventario.", statusCode: 502);
            if (stock.Disponible < linea.Cantidad)
                return Results.Conflict($"Stock disponible insuficiente para el producto {linea.IdProducto}.");
            detalles.Add(new DetallePedido(linea.IdProducto, linea.Cantidad, ficha.Precio));
        }
    }
    catch (JsonException)
    {
        return Results.Problem("Respuesta inválida de catálogo o inventario.", statusCode: 502);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Catálogo o inventario no disponible.", statusCode: 503);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.Problem("Catálogo o inventario no respondió a tiempo.", statusCode: 503);
    }

    var pedido = new Pedido(entrada.IdCliente, entrada.IdBodega, DateTimeOffset.UtcNow,
        detalles.Sum(d => d.Cantidad * d.PrecioUnitario)) { Detalles = detalles };
    db.Add(pedido);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/pedidos/{pedido.IdPedido}", pedido);
});
app.MapPatch("/api/pedidos/{id:int}/cancelar", async (int id, PedidosDb db) =>
{
    var pedido = await db.Pedidos.Include(p => p.Detalles).SingleOrDefaultAsync(p => p.IdPedido == id);
    if (pedido is null)
        return Results.NotFound();
    pedido.Estado = "Cancelado";
    await db.SaveChangesAsync();
    return Results.Ok(pedido);
});
app.Run();

record NuevoCliente(string Nombres, string Cedula, string Correo);
record NuevoPedido(int IdCliente, int IdBodega, List<NuevaLinea> Detalles);
record NuevaLinea(int IdProducto, int Cantidad);
record FichaProducto(decimal Precio, bool Estado);
record StockDisponible(int Disponible);

[Index(nameof(Cedula), IsUnique = true)]
record Cliente(string Nombres, [property: MaxLength(10)] string Cedula, string Correo)
{
    [Key] public int IdCliente { get; set; }
}
record Pedido(int IdCliente, int IdBodega, DateTimeOffset Fecha, [property: Precision(18, 2)] decimal Total)
{
    [Key] public int IdPedido { get; set; }
    public string Estado { get; set; } = "Registrado";
    public List<DetallePedido> Detalles { get; set; } = [];
}
record DetallePedido(int IdProducto, int Cantidad, [property: Precision(12, 2)] decimal PrecioUnitario)
{
    [Key] public int IdDetalle { get; set; }
    public int IdPedido { get; set; }
}

class PedidosDb(DbContextOptions<PedidosDb> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Cliente>().ToTable("Cliente");
        model.Entity<Pedido>().ToTable("Pedido").HasOne<Cliente>().WithMany()
            .HasForeignKey(p => p.IdCliente).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Pedido>().HasMany(p => p.Detalles).WithOne()
            .HasForeignKey(d => d.IdPedido).OnDelete(DeleteBehavior.Cascade);
        model.Entity<DetallePedido>().ToTable("DetallePedido");
    }
}

