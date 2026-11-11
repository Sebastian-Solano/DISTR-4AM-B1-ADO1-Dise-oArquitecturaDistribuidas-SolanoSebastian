USE [master];
GO
IF DB_ID(N'PedidosDB') IS NULL
    CREATE DATABASE [PedidosDB];
GO
USE [PedidosDB];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Cliente', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cliente (
        IdCliente int IDENTITY PRIMARY KEY,
        Nombres nvarchar(160) NOT NULL,
        Cedula nvarchar(10) NOT NULL,
        Correo nvarchar(254) NOT NULL
    );
    CREATE UNIQUE INDEX IX_Cliente_Cedula ON dbo.Cliente (Cedula);
END;

IF OBJECT_ID(N'dbo.Pedido', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Pedido (
        IdPedido int IDENTITY PRIMARY KEY,
        IdCliente int NOT NULL,
        IdBodega int NOT NULL,
        Fecha datetimeoffset NOT NULL,
        Total decimal(18,2) NOT NULL CHECK (Total > 0),
        Estado nvarchar(16) NOT NULL CHECK (Estado IN (N'Registrado', N'Cancelado')),
        CONSTRAINT FK_Pedido_Cliente FOREIGN KEY (IdCliente) REFERENCES dbo.Cliente (IdCliente)
    );
    CREATE INDEX IX_Pedido_IdCliente ON dbo.Pedido (IdCliente);
END;

IF OBJECT_ID(N'dbo.DetallePedido', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DetallePedido (
        IdDetalle int IDENTITY PRIMARY KEY,
        IdPedido int NOT NULL,
        IdProducto int NOT NULL,
        Cantidad int NOT NULL CHECK (Cantidad > 0),
        PrecioUnitario decimal(12,2) NOT NULL CHECK (PrecioUnitario > 0),
        CONSTRAINT FK_Detalle_Pedido FOREIGN KEY (IdPedido) REFERENCES dbo.Pedido (IdPedido) ON DELETE CASCADE
    );
    CREATE INDEX IX_Detalle_IdPedido ON dbo.DetallePedido (IdPedido);
END;

COMMIT TRANSACTION;
GO
