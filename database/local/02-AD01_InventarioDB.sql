USE [master];
GO
IF DB_ID(N'AD01_InventarioDB') IS NULL
    CREATE DATABASE [AD01_InventarioDB];
GO
USE [AD01_InventarioDB];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Bodega', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bodega (
        IdBodega int IDENTITY PRIMARY KEY,
        Nombre nvarchar(max) NOT NULL,
        Ciudad nvarchar(max) NOT NULL,
        Direccion nvarchar(max) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.Existencia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Existencia (
        IdExistencia int IDENTITY PRIMARY KEY,
        IdProducto int NOT NULL,
        IdBodega int NOT NULL,
        Stock int NOT NULL CHECK (Stock >= 0),
        CONSTRAINT FK_Existencia_Bodega FOREIGN KEY (IdBodega) REFERENCES dbo.Bodega (IdBodega)
    );
    CREATE UNIQUE INDEX IX_Existencia_Bodega_Producto ON dbo.Existencia (IdBodega, IdProducto);
END;

IF OBJECT_ID(N'dbo.MovimientoInventario', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MovimientoInventario (
        IdMovimiento int IDENTITY PRIMARY KEY,
        IdProducto int NOT NULL,
        IdBodega int NOT NULL,
        Tipo nvarchar(16) NOT NULL CHECK (Tipo IN (N'Entrada', N'Salida')),
        Cantidad int NOT NULL CHECK (Cantidad > 0),
        Fecha datetimeoffset NOT NULL,
        CONSTRAINT FK_Movimiento_Bodega FOREIGN KEY (IdBodega) REFERENCES dbo.Bodega (IdBodega)
    );
    CREATE INDEX IX_Movimiento_IdBodega ON dbo.MovimientoInventario (IdBodega);
END;

IF OBJECT_ID(N'dbo.ReservaStock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReservaStock (
        IdReserva int IDENTITY PRIMARY KEY,
        IdProducto int NOT NULL,
        IdBodega int NOT NULL,
        Cantidad int NOT NULL CHECK (Cantidad > 0),
        Estado nvarchar(16) NOT NULL CHECK (Estado IN (N'Activa', N'Liberada')),
        CONSTRAINT FK_Reserva_Bodega FOREIGN KEY (IdBodega) REFERENCES dbo.Bodega (IdBodega)
    );
    CREATE INDEX IX_Reserva_Bodega_Producto_Estado ON dbo.ReservaStock (IdBodega, IdProducto, Estado);
END;

COMMIT TRANSACTION;
GO
