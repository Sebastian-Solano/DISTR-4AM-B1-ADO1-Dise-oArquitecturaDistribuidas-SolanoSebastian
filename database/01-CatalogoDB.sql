USE [master];
GO
IF DB_ID(N'CatalogoDB') IS NULL
    CREATE DATABASE [CatalogoDB];
GO
USE [CatalogoDB];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Categoria', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categoria (
        IdCategoria int IDENTITY PRIMARY KEY,
        Nombre nvarchar(120) NOT NULL,
        Descripcion nvarchar(max) NULL
    );
END;

IF OBJECT_ID(N'dbo.Marca', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Marca (
        IdMarca int IDENTITY PRIMARY KEY,
        Nombre nvarchar(120) NOT NULL,
        PaisOrigen nvarchar(max) NULL
    );
END;

IF OBJECT_ID(N'dbo.Producto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Producto (
        IdProducto int IDENTITY PRIMARY KEY,
        IdCategoria int NOT NULL,
        IdMarca int NOT NULL,
        Nombre nvarchar(120) NOT NULL,
        SKU nvarchar(64) NOT NULL,
        Precio decimal(12,2) NOT NULL CHECK (Precio > 0),
        Estado bit NOT NULL,
        CONSTRAINT FK_Producto_Categoria FOREIGN KEY (IdCategoria) REFERENCES dbo.Categoria (IdCategoria),
        CONSTRAINT FK_Producto_Marca FOREIGN KEY (IdMarca) REFERENCES dbo.Marca (IdMarca)
    );
    CREATE UNIQUE INDEX IX_Producto_SKU ON dbo.Producto (SKU);
    CREATE INDEX IX_Producto_IdCategoria ON dbo.Producto (IdCategoria);
    CREATE INDEX IX_Producto_IdMarca ON dbo.Producto (IdMarca);
END;

COMMIT TRANSACTION;
GO
