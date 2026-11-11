[CmdletBinding()]
param(
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    [switch]$ConDatosDemo
)

$ErrorActionPreference = 'Stop'
$sqlcmd = Get-Command sqlcmd -CommandType Application -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    throw 'Instala sqlcmd para SQL Server y agrega su carpeta al PATH.'
}

$instancia = $null
if ($Servidor -match '^\(localdb\)\\(.+)$') {
    $instancia = $Matches[1]
    $localdb = Get-Command SqlLocalDB -CommandType Application -ErrorAction SilentlyContinue
    if (-not $localdb) {
        throw 'Instala SQL Server Express LocalDB o indica una instancia existente con -Servidor.'
    }

    $instancias = & $localdb.Source info
    if ($LASTEXITCODE -ne 0) {
        throw 'No se pudieron listar las instancias de LocalDB.'
    }
    if ($instancia -notin @($instancias | ForEach-Object { $_.Trim() })) {
        & $localdb.Source create $instancia
        if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la instancia $instancia." }
    }
    & $localdb.Source start $instancia
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo iniciar LocalDB ($instancia). Revisa el registro de la instancia."
    }
}

# Algunas versiones de SqlLocalDB devuelven 0 aunque la instancia no arranque.
# Confirma una conexión antes de intentar ejecutar los scripts.
& $sqlcmd.Source -S $Servidor -E -C -b -l 15 -Q 'SET NOCOUNT ON; SELECT 1;' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo conectar a $Servidor con la cuenta de Windows actual."
}

if ($instancia) {
    # Guarda las bases de esta actividad junto a master en su propia instancia LocalDB.
    # Esto evita colisiones con archivos de otras instancias o proyectos.
    foreach ($base in @('CatalogoDB', 'AD01_InventarioDB', 'PedidosDB')) {
        $crearBase = @"
SET NOCOUNT ON;
DECLARE @master nvarchar(260) = (
    SELECT physical_name FROM sys.master_files WHERE database_id = 1 AND file_id = 1
);
DECLARE @dir nvarchar(520) = REPLACE(
    LEFT(@master, LEN(@master) - CHARINDEX(N'\', REVERSE(@master)) + 1), N'''', N''''''
);
IF DB_ID(N'$base') IS NULL
BEGIN
    DECLARE @sql nvarchar(max) =
        N'CREATE DATABASE [$base] ON PRIMARY (NAME = N''$base'', FILENAME = N'''
        + @dir + N'${base}.mdf'') LOG ON (NAME = N''${base}_log'', FILENAME = N'''
        + @dir + N'${base}_log.ldf'');';
    EXEC (@sql);
END;
"@
        & $sqlcmd.Source -S $Servidor -E -C -b -l 15 -d master -Q $crearBase
        if ($LASTEXITCODE -ne 0) { throw "No se pudo crear $base en $Servidor." }
    }
}

$archivos = @(
    '01-CatalogoDB.sql'
    'local/02-AD01_InventarioDB.sql'
    '03-PedidosDB.sql'
)
foreach ($ruta in $archivos) {
    $archivo = Get-Item -LiteralPath (Join-Path $PSScriptRoot $ruta)
    Write-Host "Ejecutando $($archivo.Name)"
    & $sqlcmd.Source -S $Servidor -E -C -b -l 15 -i $archivo.FullName
    if ($LASTEXITCODE -ne 0) { throw "Falló $($archivo.Name) en $Servidor." }
}
Write-Host "CatalogoDB, AD01_InventarioDB y PedidosDB listas en $Servidor."

if ($ConDatosDemo) {
    $demo = Join-Path $PSScriptRoot 'Cargar-Datos-Demo.sql'
    & $sqlcmd.Source -S $Servidor -E -C -b -l 15 -f 65001 -i $demo
    if ($LASTEXITCODE -ne 0) { throw "Fallo la carga de datos de demostracion en $Servidor." }
    Write-Host 'Datos de demostracion disponibles.'
}


