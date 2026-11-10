using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;
using Parroquia.Pruebas.Infraestructura;
using Xunit.Abstractions;

namespace Parroquia.Pruebas.Rendimiento;

/// <summary>Solo corre con <c>PARROQUIA_MEDIR=1</c>: siembra datos sintéticos y tarda minutos.</summary>
public sealed class MedirFactAttribute : FactAttribute
{
    public MedirFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PARROQUIA_MEDIR") != "1")
            Skip = "Medición de rendimiento: defina PARROQUIA_MEDIR=1 para ejecutarla.";
    }
}

/// <summary>
/// RF-06: la búsqueda global debe responder en menos de 1 s con ≥100 000 registros. Siembra 50 000 filas sintéticas en cada
/// una de las 4 tablas (200 000) con índices ya creados y mide <see cref="BusquedaGlobal"/> de punta a punta (módulos + EF).
/// </summary>
public class BusquedaRendimientoPruebas(ServidorPostgresFixture servidor, ITestOutputHelper salida)
    : IClassFixture<ServidorPostgresFixture>
{
    private const int FilasPorTabla = 50_000;
    private const int Repeticiones = 20;
    private const double LimiteMs = 1000;

    private static readonly string[] Nombres =
    [
        "María", "José", "Juan Carlos", "Ana Lucía", "Luis", "Carmen", "Pedro", "Rosa María", "Miguel Ángel", "Sofía",
        "Jorge", "Marta", "Carlos", "Elena", "Fernando", "Gloria Inés", "Héctor", "Julia", "Óscar", "Patricia",
        "Rafael", "Silvia", "Tomás", "Verónica", "Andrés", "Beatriz", "César", "Diana", "Ernesto", "Flor de María",
    ];

    private static readonly string[] Comunes =
    [
        "López", "Pérez", "García", "Hernández", "Martínez", "González", "Rodríguez", "Morales", "Ramírez", "Castillo",
        "Méndez", "Ruiz", "Díaz", "Cruz", "Reyes", "Flores", "Gómez", "Sánchez", "Torres", "Álvarez",
        "Vásquez", "Ortiz", "Jiménez", "de León", "Chávez", "Aguilar", "Barrios", "Estrada", "Cifuentes", "Monzón",
    ];

    /// <summary>Apellidos raros sintéticos: sílabas combinadas, sin relación con personas reales.</summary>
    private static readonly string[] Raros = GenerarRaros(3000);

    private static string[] GenerarRaros(int cantidad)
    {
        string[] a = ["ta", "ri", "mon", "ve", "lo", "qui", "zar", "bel", "cor", "du", "fa", "gu", "hi", "ju", "ka"];
        string[] b = ["mar", "tol", "ven", "sal", "dor", "nel", "pia", "ras", "tur", "gal", "lim", "nos", "pen", "ron", "sil"];
        string[] c = ["a", "o", "es", "ez", "is", "ia", "an", "ul", "ar", "ito", "ero", "ano"];
        var lista = new List<string>();
        foreach (var x in a) foreach (var y in b) foreach (var z in c)
        {
            var palabra = x + y + z;
            lista.Add(char.ToUpperInvariant(palabra[0]) + palabra[1..]);
            if (lista.Count == cantidad) return [.. lista];
        }
        return [.. lista];
    }

    private sealed record Escenario(string Nombre, FiltroGlobal Filtro);

    private sealed record Medida(string Escenario, int Filas, double P50, double P95, double Max);

    [MedirFact]
    public async Task Busqueda_global_con_200000_filas_responde_en_menos_de_un_segundo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var reloj = Stopwatch.StartNew();
        await SembrarAsync(ctx);
        salida.WriteLine($"Siembra de {4 * FilasPorTabla:N0} filas + ANALYZE: {reloj.Elapsed.TotalSeconds:N1} s");

        var global = new BusquedaGlobal(
        [
            new ModuloBautismo(new ServicioBautismo(ctx, bd.Reloj)),
            new ModuloConfirmacion(new ServicioConfirmacion(ctx, bd.Reloj)),
            new ModuloComunion(new ServicioComunion(ctx, bd.Reloj)),
            new ModuloMatrimonio(new ServicioMatrimonio(ctx, bd.Reloj)),
        ]);

        var raro = Raros[17];
        var escenarios = new Escenario[]
        {
            new("Nombre + apellido raro (acentos)", new FiltroGlobal($"{Nombres[1]} {raro}")),
            new("Solo apellido raro", new FiltroGlobal(raro.ToUpperInvariant())),
            new("Apellido común (~1,7 % de cada libro)", new FiltroGlobal("lopez")),
            new("Dos palabras comunes", new FiltroGlobal("maria lopez")),
            new("Nombre común + años 1990-1995", new FiltroGlobal("maria", AnioDesde: 1990, AnioHasta: 1995)),
            new("Token de 2 letras (sin trigram)", new FiltroGlobal("jo")),
            new("Apellido común + sacramento + libro", new FiltroGlobal("garcia", Sacramento: "bautismo", Libro: "7")),
            new("Apellido común + folio", new FiltroGlobal("perez", Folio: "12")),
        };

        var medidas = new List<Medida>();
        foreach (var e in escenarios)
        {
            var primera = await global.BuscarAsync(e.Filtro); // calentamiento (plan, conexión, JIT)
            var tiempos = new List<double>();
            for (var i = 0; i < Repeticiones; i++)
            {
                var t = Stopwatch.StartNew();
                await global.BuscarAsync(e.Filtro);
                tiempos.Add(t.Elapsed.TotalMilliseconds);
            }
            tiempos.Sort();
            medidas.Add(new Medida(e.Nombre, primera.Items.Count, Percentil(tiempos, 0.5), Percentil(tiempos, 0.95), tiempos[^1]));
        }

        salida.WriteLine("");
        salida.WriteLine($"{"Escenario",-44} {"Filas",6} {"p50 ms",8} {"p95 ms",8} {"máx ms",8}");
        foreach (var m in medidas)
            salida.WriteLine($"{m.Escenario,-44} {m.Filas,6} {m.P50,8:N1} {m.P95,8:N1} {m.Max,8:N1}");

        await ExplicarAsync(ctx, "lopez");
        await ExplicarAsync(ctx, "jo");

        medidas.Where(m => m.P95 >= LimiteMs).Select(m => m.Escenario)
            .Should().BeEmpty("RF-06 pide menos de 1 s (p95)");
    }

    /// <summary>
    /// Listados de cada libro (pantalla Libros → X): COUNT + ORDER BY apellidos/nombres + OFFSET/LIMIT 25, sin texto de búsqueda,
    /// que es donde el orden pesa más (con texto, el trigram ya reduce las filas).
    /// </summary>
    [MedirFact]
    public async Task Listados_con_50000_filas_por_libro_responden_en_menos_de_un_segundo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        await SembrarAsync(ctx);

        var comunion = new ServicioComunion(ctx, bd.Reloj);
        var bautismo = new ServicioBautismo(ctx, bd.Reloj);
        var confirmacion = new ServicioConfirmacion(ctx, bd.Reloj);
        var matrimonio = new ServicioMatrimonio(ctx, bd.Reloj);

        var escenarios = new List<(string Nombre, Func<Task> Accion)>();
        void Agregar(string libro, string filtro, Func<Task> accion) => escenarios.Add(($"{libro}: {filtro}", accion));

        Agregar("Comunión", "sin filtro, pág. 1", () => comunion.BuscarAsync(new FiltroComunion(), 1));
        Agregar("Comunión", "sin filtro, pág. 1000", () => comunion.BuscarAsync(new FiltroComunion(), 1000));
        Agregar("Comunión", "libro 7", () => comunion.BuscarAsync(new FiltroComunion(Libro: "7"), 1));
        Agregar("Comunión", "años 1990-1995", () => comunion.BuscarAsync(new FiltroComunion(AnioDesde: 1990, AnioHasta: 1995), 1));

        Agregar("Bautismo", "sin filtro, pág. 1", () => bautismo.BuscarAsync(new FiltroBautismo(), 1));
        Agregar("Bautismo", "sin filtro, pág. 1000", () => bautismo.BuscarAsync(new FiltroBautismo(), 1000));
        Agregar("Bautismo", "libro 7", () => bautismo.BuscarAsync(new FiltroBautismo(Libro: "7"), 1));
        Agregar("Bautismo", "años 1990-1995", () => bautismo.BuscarAsync(new FiltroBautismo(AnioDesde: 1990, AnioHasta: 1995), 1));

        Agregar("Confirmación", "sin filtro, pág. 1", () => confirmacion.BuscarAsync(new FiltroConfirmacion(), 1));
        Agregar("Confirmación", "sin filtro, pág. 1000", () => confirmacion.BuscarAsync(new FiltroConfirmacion(), 1000));
        Agregar("Confirmación", "libro 7", () => confirmacion.BuscarAsync(new FiltroConfirmacion(Libro: "7"), 1));
        Agregar("Confirmación", "años 1990-1995", () => confirmacion.BuscarAsync(new FiltroConfirmacion(AnioDesde: 1990, AnioHasta: 1995), 1));

        Agregar("Matrimonio", "sin filtro, pág. 1", () => matrimonio.BuscarAsync(new FiltroMatrimonio(), 1));
        Agregar("Matrimonio", "sin filtro, pág. 1000", () => matrimonio.BuscarAsync(new FiltroMatrimonio(), 1000));
        Agregar("Matrimonio", "libro 7", () => matrimonio.BuscarAsync(new FiltroMatrimonio(Libro: "7"), 1));
        Agregar("Matrimonio", "años 1990-1995", () => matrimonio.BuscarAsync(new FiltroMatrimonio(AnioDesde: 1990, AnioHasta: 1995), 1));

        async Task<List<Medida>> MedirAsync(string titulo)
        {
            var medidas = new List<Medida>();
            foreach (var (nombre, accion) in escenarios)
            {
                await accion(); // calentamiento
                var tiempos = new List<double>();
                for (var i = 0; i < Repeticiones; i++)
                {
                    var t = Stopwatch.StartNew();
                    await accion();
                    tiempos.Add(t.Elapsed.TotalMilliseconds);
                }
                tiempos.Sort();
                medidas.Add(new Medida(nombre, 25, Percentil(tiempos, 0.5), Percentil(tiempos, 0.95), tiempos[^1]));
            }

            salida.WriteLine("");
            salida.WriteLine(titulo);
            salida.WriteLine($"{"Listado",-44} {"p50 ms",8} {"p95 ms",8} {"máx ms",8}");
            foreach (var m in medidas)
                salida.WriteLine($"{m.Escenario,-44} {m.P50,8:N1} {m.P95,8:N1} {m.Max,8:N1}");
            return medidas;
        }

        // La base de pruebas se crea con EnsureCreated (sin migraciones), así que el índice de la migración
        // IndiceOrdenMatrimonio no existe: se mide sin él y luego se crea con el mismo SQL.
        var sinIndice = await MedirAsync("SIN índice de orden en matrimonio");
        await ExplicarListadoMatrimonioAsync(ctx);

        await EjecutarAsync(ctx,
            "CREATE INDEX ix_matrimonio_esposo_apellidos_esposo_nombres ON matrimonio (esposo_apellidos, esposo_nombres)");
        await EjecutarAsync(ctx, "ANALYZE matrimonio");

        var conIndice = await MedirAsync("CON índice de orden en matrimonio");
        await ExplicarListadoMatrimonioAsync(ctx);

        sinIndice.Concat(conIndice).Where(m => m.P95 >= LimiteMs).Select(m => m.Escenario)
            .Should().BeEmpty("RF-06 pide menos de 1 s (p95)");
    }

    private static async Task EjecutarAsync(ParroquiaDbContext ctx, string sql)
    {
        var conexion = ctx.Database.GetDbConnection();
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 600;
        await cmd.ExecuteNonQueryAsync();
        await conexion.CloseAsync();
    }

    private async Task ExplicarListadoMatrimonioAsync(ParroquiaDbContext ctx)
    {
        var conexion = ctx.Database.GetDbConnection();
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            "EXPLAIN (ANALYZE, BUFFERS, COSTS OFF) SELECT id FROM matrimonio WHERE NOT anulado " +
            "ORDER BY esposo_apellidos, esposo_nombres, libro_orden, folio_orden, partida_orden, id LIMIT 25";
        salida.WriteLine("");
        salida.WriteLine("EXPLAIN ANALYZE listado de matrimonio sin filtro (página 1):");
        await using var lector = await cmd.ExecuteReaderAsync();
        while (await lector.ReadAsync()) salida.WriteLine("  " + lector.GetString(0));
        await conexion.CloseAsync();
    }

    private static double Percentil(List<double> ordenados, double p) =>
        ordenados[Math.Min(ordenados.Count - 1, (int)Math.Ceiling(p * ordenados.Count) - 1)];

    private async Task ExplicarAsync(ParroquiaDbContext ctx, string token)
    {
        var conexion = ctx.Database.GetDbConnection();
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            "EXPLAIN (ANALYZE, BUFFERS, COSTS OFF) SELECT id, nombres, apellidos FROM comunion " +
            $"WHERE NOT anulado AND busqueda_normalizada LIKE '%{token}%' ORDER BY apellidos, nombres LIMIT 50";
        salida.WriteLine("");
        salida.WriteLine($"EXPLAIN ANALYZE comunion LIKE '%{token}%' (consulta equivalente a la del servicio):");
        await using var lector = await cmd.ExecuteReaderAsync();
        while (await lector.ReadAsync()) salida.WriteLine("  " + lector.GetString(0));
        await conexion.CloseAsync();
    }

    private async Task SembrarAsync(ParroquiaDbContext ctx)
    {
        var apellidos = Comunes.Concat(Raros).ToArray();
        var conexion = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conexion.OpenAsync();

        string[] Orden(int hasta) => [.. Enumerable.Range(1, hasta).Select(i => ClaveOrdenNatural.De(i.ToString()))];
        string[] Norm(IEnumerable<string> textos) => [.. textos.Select(t => NormalizadorBusqueda.Normalizar(t))];

        // Común a todas las tablas. random() con semilla fija: la misma distribución en cada corrida.
        const string Comun = """
            WITH s AS (
              SELECT g,
                (1 + (g * 7919) % @nn)::int AS ni, (1 + (g * 104729) % @nn)::int AS ni2,
                CASE WHEN random() < 0.5 THEN 1 + floor(random() * @nc)::int
                     ELSE @nc + 1 + floor(random() * @nr)::int END AS ai,
                CASE WHEN random() < 0.5 THEN 1 + floor(random() * @nc)::int
                     ELSE @nc + 1 + floor(random() * @nr)::int END AS ai2,
                (1 + g % 100)::int AS lo, (1 + (g / 100) % 300)::int AS fo, (1 + g % 20)::int AS po,
                (1950 + g % 75)::int AS anio, (1 + g % 12)::int AS mes, (1 + g % 28)::int AS dia
              FROM generate_series(1::bigint, @filas::bigint) AS g)
            """;

        string Insertar(string tabla, string fecha, string extraCols, string extraVals) => $"""
            {Comun}
            INSERT INTO {tabla} (id, nombres, apellidos, {fecha}_anio, {fecha}_mes, {fecha}_dia, creado_en,
              libro, folio, partida, libro_orden, folio_orden, partida_orden, busqueda_normalizada, revisar, anulado{extraCols})
            SELECT g, (@nombres)[ni], (@apellidos)[ai], anio, mes, dia, now(),
              lo::text, fo::text, po::text, (@lordenes)[lo], (@fordenes)[fo], (@pordenes)[po],
              (@nnorm)[ni] || ' ' || (@anorm)[ai], false, false{extraVals}
            FROM s
            """;

        var matrimonio = $"""
            {Comun}
            INSERT INTO matrimonio (id, esposo_nombres, esposo_apellidos, esposa_nombres, esposa_apellidos,
              fecha_matrimonio_anio, fecha_matrimonio_mes, fecha_matrimonio_dia, creado_en, lugar_celebracion, testigos,
              busqueda_esposo, busqueda_esposa, libro, folio, partida, libro_orden, folio_orden, partida_orden,
              busqueda_normalizada, revisar, anulado)
            SELECT g, (@nombres)[ni], (@apellidos)[ai], (@nombres)[ni2], (@apellidos)[ai2],
              anio, mes, dia, now(), 'Iglesia de Mazatenango, Suchitepéquez', ARRAY[]::text[],
              (@nnorm)[ni] || ' ' || (@anorm)[ai], (@nnorm)[ni2] || ' ' || (@anorm)[ai2],
              lo::text, fo::text, po::text, (@lordenes)[lo], (@fordenes)[fo], (@pordenes)[po],
              (@nnorm)[ni] || ' ' || (@anorm)[ai] || ' ' || (@nnorm)[ni2] || ' ' || (@anorm)[ai2], false, false
            FROM s
            """;

        var sentencias = new[]
        {
            Insertar("comunion", "fecha_comunion", "", ""),
            Insertar("bautismo", "fecha_bautismo", ", padrinos", ", '{}'"),
            Insertar("confirmacion", "fecha_confirmacion", ", padrinos", ", '{}'"),
            matrimonio,
        };

        await using (var semilla = new NpgsqlCommand("SELECT setseed(0.42)", conexion)) await semilla.ExecuteScalarAsync();

        foreach (var sql in sentencias)
        {
            await using var cmd = new NpgsqlCommand(sql, conexion) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("filas", FilasPorTabla);
            cmd.Parameters.AddWithValue("nn", Nombres.Length);
            cmd.Parameters.AddWithValue("nc", Comunes.Length);
            cmd.Parameters.AddWithValue("nr", Raros.Length);
            cmd.Parameters.AddWithValue("nombres", Nombres);
            cmd.Parameters.AddWithValue("apellidos", apellidos);
            cmd.Parameters.AddWithValue("nnorm", Norm(Nombres));
            cmd.Parameters.AddWithValue("anorm", Norm(apellidos));
            cmd.Parameters.AddWithValue("lordenes", Orden(100));
            cmd.Parameters.AddWithValue("fordenes", Orden(300));
            cmd.Parameters.AddWithValue("pordenes", Orden(20));
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var analizar = new NpgsqlCommand("ANALYZE", conexion) { CommandTimeout = 600 })
            await analizar.ExecuteNonQueryAsync();
        await conexion.CloseAsync();
    }
}
