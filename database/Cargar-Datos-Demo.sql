SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @Demo TABLE (Numero int PRIMARY KEY, Categoria nvarchar(120), Marca nvarchar(120), Pais nvarchar(80), Producto nvarchar(120), Precio decimal(12,2), Ciudad nvarchar(80), SKU nvarchar(64), Bodega nvarchar(120), Cedula nvarchar(10), Stock int);
INSERT @Demo VALUES
(1, N'Periféricos', N'Logitech', N'Suiza', N'Teclado USB', 25.5, N'Quito', N'AD01-DEMO-1', N'Bodega AD01 Quito', N'9900000101', 20),
(2, N'Almacenamiento', N'Kingston', N'Estados Unidos', N'SSD 480 GB', 42, N'Guayaquil', N'AD01-DEMO-2', N'Bodega AD01 Guayaquil', N'9900000102', 25),
(3, N'Monitores', N'LG', N'Corea del Sur', N'Monitor 24 pulgadas', 129, N'Cuenca', N'AD01-DEMO-3', N'Bodega AD01 Cuenca', N'9900000103', 30),
(4, N'Redes', N'TP-Link', N'China', N'Router Wi-Fi', 55, N'Ambato', N'AD01-DEMO-4', N'Bodega AD01 Ambato', N'9900000104', 35),
(5, N'Audio', N'Sony', N'Japón', N'Audífonos con micrófono', 39.9, N'Loja', N'AD01-DEMO-5', N'Bodega AD01 Loja', N'9900000105', 40);

-- La carga inicial se puede volver a ejecutar en una base con los datos de ejemplo.
IF (SELECT COUNT(*) FROM @Demo d JOIN CatalogoDB.dbo.Producto p ON p.SKU = d.SKU) = 5
   AND (SELECT COUNT(*) FROM @Demo d JOIN PedidosDB.dbo.Cliente c ON c.Cedula = d.Cedula) = 5
   AND (SELECT COUNT(*) FROM @Demo d JOIN [AD01_InventarioDB].dbo.Bodega b ON b.Nombre = d.Bodega) = 5
BEGIN
    COMMIT TRANSACTION;
    RETURN;
END;

IF EXISTS (SELECT 1 FROM @Demo d JOIN CatalogoDB.dbo.Producto p ON p.SKU = d.SKU)
   OR EXISTS (SELECT 1 FROM @Demo d JOIN PedidosDB.dbo.Cliente c ON c.Cedula = d.Cedula)
   OR EXISTS (SELECT 1 FROM @Demo d JOIN [AD01_InventarioDB].dbo.Bodega b ON b.Nombre = d.Bodega)
    THROW 50001, 'Ya existen identificadores de esta carga de prueba.', 1;

INSERT CatalogoDB.dbo.Categoria (Nombre, Descripcion)
SELECT d.Categoria, N'Categoría de ejemplo AD01' FROM @Demo d
WHERE NOT EXISTS (SELECT 1 FROM CatalogoDB.dbo.Categoria c WHERE c.Nombre = d.Categoria);

DECLARE @Marcas TABLE (IdMarca int, Nombre nvarchar(120));
INSERT CatalogoDB.dbo.Marca (Nombre, PaisOrigen)
OUTPUT inserted.IdMarca, inserted.Nombre INTO @Marcas
SELECT Marca, Pais FROM @Demo;

INSERT CatalogoDB.dbo.Producto (IdCategoria, IdMarca, Nombre, SKU, Precio, Estado)
SELECT cat.IdCategoria, m.IdMarca, d.Producto, d.SKU, d.Precio, 1
FROM @Demo d JOIN @Marcas m ON m.Nombre = d.Marca
CROSS APPLY (SELECT MIN(IdCategoria) AS IdCategoria FROM CatalogoDB.dbo.Categoria WHERE Nombre = d.Categoria) cat;

INSERT [AD01_InventarioDB].dbo.Bodega (Nombre, Ciudad, Direccion)
SELECT Bodega, Ciudad, CONCAT(N'Avenida Central ', Numero * 100) FROM @Demo;

INSERT PedidosDB.dbo.Cliente (Nombres, Cedula, Correo)
SELECT CONCAT(N'Cliente de prueba AD01 ', Numero), Cedula, CONCAT(N'ad01-cliente', Numero, N'@example.com') FROM @Demo;

DECLARE @Ids TABLE (IdProducto int, IdBodega int, IdCliente int, Stock int, Precio decimal(12,2));
INSERT @Ids
SELECT p.IdProducto, b.IdBodega, c.IdCliente, d.Stock, d.Precio
FROM @Demo d JOIN CatalogoDB.dbo.Producto p ON p.SKU = d.SKU
JOIN [AD01_InventarioDB].dbo.Bodega b ON b.Nombre = d.Bodega
JOIN PedidosDB.dbo.Cliente c ON c.Cedula = d.Cedula;

IF (SELECT COUNT(*) FROM @Ids) <> 5
    THROW 50002, 'No se pudieron relacionar los cinco conjuntos de datos.', 1;

DECLARE @Ahora datetimeoffset = SYSUTCDATETIME();
INSERT [AD01_InventarioDB].dbo.Existencia (IdProducto, IdBodega, Stock)
SELECT IdProducto, IdBodega, Stock FROM @Ids;

INSERT [AD01_InventarioDB].dbo.MovimientoInventario (IdProducto, IdBodega, Tipo, Cantidad, Fecha)
SELECT IdProducto, IdBodega, N'Entrada', Stock, @Ahora FROM @Ids;

INSERT [AD01_InventarioDB].dbo.ReservaStock (IdProducto, IdBodega, Cantidad, Estado)
SELECT IdProducto, IdBodega, 2, N'Activa' FROM @Ids;

DECLARE @Pedidos TABLE (IdPedido int, IdCliente int);
INSERT PedidosDB.dbo.Pedido (IdCliente, IdBodega, Fecha, Total, Estado)
OUTPUT inserted.IdPedido, inserted.IdCliente INTO @Pedidos
SELECT IdCliente, IdBodega, @Ahora, Precio * 2, N'Registrado' FROM @Ids;

INSERT PedidosDB.dbo.DetallePedido (IdPedido, IdProducto, Cantidad, PrecioUnitario)
SELECT p.IdPedido, i.IdProducto, 2, i.Precio FROM @Ids i JOIN @Pedidos p ON p.IdCliente = i.IdCliente;

COMMIT TRANSACTION;

SELECT p.IdPedido, i.IdCliente, i.IdProducto, i.IdBodega, i.Stock, i.Stock - 2 AS Disponible, i.Precio * 2 AS Total
FROM @Ids i JOIN @Pedidos p ON p.IdCliente = i.IdCliente ORDER BY p.IdPedido;

