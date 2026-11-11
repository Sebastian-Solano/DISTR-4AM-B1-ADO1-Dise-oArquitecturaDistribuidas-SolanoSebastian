using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CatalogoDb>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CatalogoDB")));
builder.Services.AddOpenApi();
var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "Catalogo.Api v1"));
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<CatalogoDb>().Database.EnsureCreatedAsync();

app.MapGet("/api/categorias", async (CatalogoDb db) => await db.Categorias.ToListAsync());
app.MapPost("/api/categorias", async (Categoria categoria, CatalogoDb db) =>
{
    if (string.IsNullOrWhiteSpace(categoria.Nombre) || categoria.Nombre.Length > 120)
        return Results.BadRequest("Nombre obligatorio de hasta 120 caracteres.");
    categoria.IdCategoria = 0;
    db.Add(categoria);
    await db.SaveChangesAsync();
    return Results.Created($"/api/categorias/{categoria.IdCategoria}", categoria);
});
app.MapGet("/api/categorias/{id:int}", async (int id, CatalogoDb db) =>
    await db.Categorias.FindAsync(id) is { } categoria ? Results.Ok(categoria) : Results.NotFound());

app.MapGet("/api/marcas", async (CatalogoDb db) => await db.Marcas.ToListAsync());
app.MapPost("/api/marcas", async (Marca marca, CatalogoDb db) =>
{
    if (string.IsNullOrWhiteSpace(marca.Nombre) || marca.Nombre.Length > 120)
        return Results.BadRequest("Nombre obligatorio de hasta 120 caracteres.");
    marca.IdMarca = 0;
    db.Add(marca);
    await db.SaveChangesAsync();
    return Results.Created($"/api/marcas/{marca.IdMarca}", marca);
});
app.MapGet("/api/marcas/{id:int}", async (int id, CatalogoDb db) =>
    await db.Marcas.FindAsync(id) is { } marca ? Results.Ok(marca) : Results.NotFound());

app.MapGet("/api/productos", async (CatalogoDb db) => await db.Productos.ToListAsync());
app.MapGet("/api/productos/{id:int}", async (int id, CatalogoDb db) =>
    await db.Productos.FindAsync(id) is { } producto ? Results.Ok(producto) : Results.NotFound());

app.MapPost("/api/productos", async (NuevoProducto entrada, CatalogoDb db) =>
{
    if (string.IsNullOrWhiteSpace(entrada.Nombre) || entrada.Nombre.Length > 120 ||
        string.IsNullOrWhiteSpace(entrada.SKU) || entrada.SKU.Length > 64 ||
        entrada.Precio <= 0 || entrada.Precio > 9999999999.99m ||
        decimal.Round(entrada.Precio, 2) != entrada.Precio)
        return Results.BadRequest("Nombre, SKU y precio de hasta dos decimales son obligatorios.");
    if (await db.Categorias.FindAsync(entrada.IdCategoria) is null ||
        await db.Marcas.FindAsync(entrada.IdMarca) is null)
        return Results.NotFound("Categoría o marca inexistente.");

    var producto = new Producto(entrada.IdCategoria, entrada.IdMarca, entrada.Nombre.Trim(),
        entrada.SKU.Trim().ToUpperInvariant(), entrada.Precio, true);
    db.Add(producto);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
    {
        return Results.Conflict("El SKU ya existe.");
    }
    return Results.Created($"/api/productos/{producto.IdProducto}", producto);
});
app.MapPatch("/api/productos/{id:int}/estado", async (int id, EstadoProducto entrada, CatalogoDb db) =>
{
    if (await db.Productos.FindAsync(id) is not { } producto)
        return Results.NotFound();
    producto.Estado = entrada.Estado;
    await db.SaveChangesAsync();
    return Results.Ok(producto);
});
app.Run();

record EstadoProducto(bool Estado);
record NuevoProducto(int IdCategoria, int IdMarca, string Nombre, string SKU, decimal Precio);
record Categoria(string Nombre, string? Descripcion)
{
    [Key] public int IdCategoria { get; set; }
}
record Marca(string Nombre, string? PaisOrigen)
{
    [Key] public int IdMarca { get; set; }
}
[Index(nameof(SKU), IsUnique = true)]
record Producto(int IdCategoria, int IdMarca, string Nombre, [property: MaxLength(64)] string SKU,
    [property: Precision(12, 2)] decimal Precio, bool Estado)
{
    [Key] public int IdProducto { get; set; }
    public bool Estado { get; set; } = Estado;
}

class CatalogoDb(DbContextOptions<CatalogoDb> options) : DbContext(options)
{
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Marca> Marcas => Set<Marca>();
    public DbSet<Producto> Productos => Set<Producto>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Categoria>().ToTable("Categoria");
        model.Entity<Marca>().ToTable("Marca");
        model.Entity<Producto>().ToTable("Producto").HasOne<Categoria>().WithMany()
            .HasForeignKey(p => p.IdCategoria).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Producto>().HasOne<Marca>().WithMany()
            .HasForeignKey(p => p.IdMarca).OnDelete(DeleteBehavior.Restrict);
    }
}

