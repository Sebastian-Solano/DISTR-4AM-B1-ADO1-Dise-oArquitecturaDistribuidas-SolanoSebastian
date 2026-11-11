# AD01 Comercio multibodega

La solución contiene tres APIs REST en ASP.NET Core (.NET 10) y tres bases de datos de SQL Server Express LocalDB.

| API | Base de datos | Swagger | Consulta de ejemplo |
| --- | --- | --- | --- |
| Catálogo | `CatalogoDB` | [Puerto 5101](http://localhost:5101/swagger/) | `GET /api/productos` |
| Inventario | `AD01_InventarioDB` | [Puerto 5102](http://localhost:5102/swagger/) | `GET /api/existencias` |
| Pedidos | `PedidosDB` | [Puerto 5103](http://localhost:5103/swagger/) | `GET /api/pedidos` |

## 1. Preparar el entorno

- Usa Windows, el SDK de .NET 10 y SQL Server Express LocalDB.
- Para ejecutar desde Visual Studio, instala la carga de trabajo **ASP.NET y desarrollo web**.
- Clona o descarga el repositorio y abre en PowerShell la carpeta que contiene `ComercioMultibodega.sln`.
- Ejecuta `dotnet --list-sdks` para comprobar que aparece una versión 10.0 y `sqllocaldb info` para comprobar que está disponible LocalDB.

## 2. Crear las bases y cargar los datos

Elige PowerShell o SQL Server Management Studio (SSMS). Ambas opciones usan `(localdb)\MSSQLLocalDB` con autenticación de Windows y crean las bases de la tabla anterior. Inventario guarda los datos de la actividad en `AD01_InventarioDB`.

### Opción A. Usar PowerShell

1. Comprueba que el cliente SQL esté en el PATH con `Get-Command sqlcmd`.
2. Desde la carpeta de la solución, ejecuta:

   ```powershell
   .\database\Preparar-SqlServer.ps1 -ConDatosDemo
   ```

3. Espera los mensajes `CatalogoDB, AD01_InventarioDB y PedidosDB listas` y `Datos de demostracion disponibles`. El script inicia LocalDB, crea el esquema de cada base y carga los datos relacionados. El parámetro `-ConDatosDemo` activa esa carga.

### Opción B. Usar SQL Server Management Studio (SSMS)

1. Abre SSMS y elige **Conectar → Motor de base de datos**. En **Nombre del servidor**, escribe `(localdb)\MSSQLLocalDB`. Selecciona **Autenticación de Windows** y pulsa **Conectar**.
2. Usa **Archivo → Abrir → Archivo** o **Ctrl+O**. Abre los siguientes archivos en el orden de la tabla. En cada pestaña, comprueba que la consulta esté conectada a `MSSQLLocalDB`, pulsa **F5** para ejecutar el archivo completo y revisa la pestaña **Mensajes** antes de pasar al siguiente.

   | Orden | Archivo | Resultado |
   | ---: | --- | --- |
   | 1 | [01-CatalogoDB.sql](database/01-CatalogoDB.sql) | Crea `CatalogoDB` y las tablas de categorías, marcas y productos. |
   | 2 | [02-AD01_InventarioDB.sql](database/local/02-AD01_InventarioDB.sql) | Crea `AD01_InventarioDB` y las tablas de bodegas, existencias, movimientos y reservas. |
   | 3 | [03-PedidosDB.sql](database/03-PedidosDB.sql) | Crea `PedidosDB` y las tablas de clientes, pedidos y detalles. |
   | 4 | [Cargar-Datos-Demo.sql](database/Cargar-Datos-Demo.sql) | Carga cinco registros de ejemplo en cada tabla y muestra los cinco pedidos vinculados con sus clientes, productos y bodegas. |

3. En el **Explorador de objetos**, haz clic derecho en **Bases de datos → Actualizar**. Expande **Tablas** en cada base. Debes ver 3 tablas en `CatalogoDB`, 4 en `AD01_InventarioDB` y 3 en `PedidosDB`. Para revisar los registros, haz clic derecho sobre una tabla y elige **Seleccionar las primeras 1000 filas**.

Si falla la conexión a LocalDB, ejecuta `sqllocaldb start MSSQLLocalDB` en PowerShell con el mismo usuario de Windows y vuelve a intentar la conexión.

## 3. Iniciar las tres APIs

Abre `ComercioMultibodega.sln` en Visual Studio y espera a que se restauren los paquetes NuGet. En el desplegable de proyectos de inicio, selecciona **Comercio multibodega** y pulsa **Ctrl+F5**. Si el perfil no aparece, cierra y vuelve a abrir la solución.

También puedes usar PowerShell. Desde la carpeta de la solución, ejecuta cada comando en una terminal distinta y déjalas abiertas:

```powershell
dotnet run --project Catalogo.Api
dotnet run --project Inventario.Api
dotnet run --project Pedidos.Api
```

## 4. Comprobar los datos en Swagger

Abre el enlace de Swagger de cada API en la tabla inicial. Expande la consulta, pulsa **Try it out** y después **Execute**. Con una base recién creada y los datos de demostración cargados, estas consultas devuelven **HTTP 200** y cinco registros:

- `GET /api/productos` en Catálogo muestra los cinco productos. Guarda el `idProducto` de **Teclado USB**.
- `GET /api/existencias` en Inventario muestra el stock de cada producto por bodega. Busca la existencia del teclado y guarda su `idBodega`. Consulta `GET /api/existencias/{bodega}/{producto}` con ambos IDs para ver **20 unidades en stock y 18 disponibles**.
- `GET /api/pedidos` en Pedidos muestra los cinco pedidos con sus detalles. Busca el que contiene el `idProducto` del teclado. Debe indicar **dos unidades**, estado `Registrado` y un total de **51.00**.

Si un `GET` devuelve `[]`, comprueba que se haya ejecutado `Cargar-Datos-Demo.sql` en la misma instancia a la que se conectan las APIs.


## Probar el flujo completo

Importa `ComercioMultibodega.postman_collection.json` en Postman y ejecuta sus 17 solicitudes en orden. La colección crea sus propios datos de prueba y comprueba el stock, el total, la cancelación y las respuestas de error.

Los pedidos consultan la disponibilidad en Inventario y guardan el precio obtenido de Catálogo. Las reservas se crean con una operación de Inventario.

## Entrega en GitHub

Sube la solución, los scripts SQL y la colección Postman. El repositorio excluye `bin`, `obj`, `.vs` y los archivos físicos de SQL Server. Las bases se reconstruyen con los scripts.

Usa el nombre `DISTR-[NIVEL/PARALELO]-B1-AD01-DisenioArquitecturasDistribuidas-[ApellidoNombre]`. El flujo **Compilar solución** de GitHub Actions comprueba la compilación. La prueba de Swagger y Postman se realiza con las APIs ejecutándose en Windows. Adjunta la declaración de uso de IA en PDF según la plantilla institucional.

