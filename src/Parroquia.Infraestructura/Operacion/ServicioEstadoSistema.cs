using System.Globalization;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Operacion;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Operacion;

/// <summary>
/// Reúne el estado del servidor para la página del Administrador. Los respaldos y las pruebas de restauración los hacen
/// scripts de PowerShell (deploy/); dejan un marcador JSON en la carpeta de datos que esta clase lee y evalúa.
/// Nunca lanza por un marcador dañado o un dato no disponible: la página debe abrir justo cuando algo falla.
/// </summary>
public sealed class ServicioEstadoSistema(
    ParroquiaDbContext db,
    IEnumerable<IModuloSacramental> modulos,
    CarpetaDatos carpeta,
    IConfiguration config,
    IHostEnvironment entorno,
    TimeProvider reloj) : IEstadoSistema
{
    private static readonly DateTimeOffset InicioProceso = DateTimeOffset.UtcNow;
    private static readonly JsonSerializerOptions OpcionesJson = new() { PropertyNameCaseInsensitive = true };

    public async Task<EstadoSistema> ObtenerAsync(CancellationToken ct = default)
    {
        var version = await ConsultarAsync<string>("SELECT version() AS \"Value\"", ct) ?? "desconocida";
        var tamano = await ConsultarAsync<long>("SELECT pg_database_size(current_database()) AS \"Value\"", ct);
        var migraciones = await db.Database.GetAppliedMigrationsAsync(ct);

        var registros = new List<ConteoLibro>();
        foreach (var m in modulos.OrderBy(m => m.Orden))
        {
            // El listado sin texto devuelve el total del libro (sin anulados): no hace falta ampliar IModuloSacramental.
            var r = await m.BuscarAsync(new ConsultaBusqueda(null), 1, ct);
            registros.Add(new ConteoLibro(m.Nombre, r.Total));
        }

        return new EstadoSistema(
            Version: VersionApp(),
            Entorno: entorno.EnvironmentName,
            Inicio: InicioProceso,
            VersionPostgres: PrimerasPalabras(version),
            BytesBaseDatos: tamano,
            UltimaMigracion: migraciones.LastOrDefault() ?? "ninguna",
            Registros: registros,
            Respaldo: EvaluarRespaldo(),
            Restauracion: EvaluarRestauracion(),
            Disco: EvaluarDisco(),
            Certificado: EvaluarCertificado());
    }

    private async Task<T?> ConsultarAsync<T>(string sql, CancellationToken ct)
    {
        try { return (await db.Database.SqlQueryRaw<T>(sql).ToListAsync(ct)).FirstOrDefault(); }
        catch (Exception) { return default; }
    }

    private static string VersionApp()
    {
        var informativa = typeof(ServicioEstadoSistema).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "desconocida";
        // El SDK agrega "+<commit>"; se deja corto pero visible.
        var mas = informativa.IndexOf('+');
        return mas > 0 && informativa.Length > mas + 8 ? informativa[..(mas + 8)] : informativa;
    }

    private static string PrimerasPalabras(string version)
    {
        // "PostgreSQL 17.2 (Debian ...) on x86_64..." -> "PostgreSQL 17.2"
        var partes = version.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length >= 2 ? $"{partes[0]} {partes[1]}" : version;
    }

    // ---- Marcadores de los scripts -------------------------------------------------------------------------------

    private sealed class Marcador
    {
        public DateTimeOffset? Fecha { get; set; }
        public string? Resultado { get; set; }
        public string? Mensaje { get; set; }
        public string? Archivo { get; set; }
        public long? Bytes { get; set; }
        public bool? Copia2 { get; set; }
        public bool? Copia3 { get; set; }
    }

    private Marcador? Leer(string nombre, out string? problema)
    {
        problema = null;
        var ruta = Path.Combine(carpeta.Respaldos, nombre);
        if (!File.Exists(ruta)) return null;
        try
        {
            var m = JsonSerializer.Deserialize<Marcador>(File.ReadAllText(ruta), OpcionesJson);
            if (m?.Fecha is null) problema = $"{nombre} no tiene una fecha válida.";
            return m;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problema = $"No se pudo leer {nombre}.";
            return null;
        }
    }

    private static string Hora(DateTimeOffset f) =>
        HoraGuatemala.Local(f).ToString("d 'de' MMMM 'de' yyyy, HH:mm", CultureInfo.GetCultureInfo("es-GT"));

    private ElementoEstado EvaluarRespaldo()
    {
        var m = Leer("ultimo.json", out var problema);
        if (problema is not null || m is null)
            return new(NivelEstado.Aviso, "No hay registro de ningún respaldo.",
                problema ?? "Revise que la tarea programada «Parroquia - Respaldo diario» exista y esté habilitada (docs/operacion.md).");

        var edad = reloj.GetUtcNow() - m.Fecha!.Value;
        var cuando = Hora(m.Fecha.Value);
        if (!string.Equals(m.Resultado, "ok", StringComparison.OrdinalIgnoreCase))
            return new(NivelEstado.Aviso, $"El último respaldo ({cuando}) falló.", m.Mensaje);
        if (edad > UmbralesEstado.RespaldoMaximo)
            return new(NivelEstado.Aviso,
                $"El último respaldo correcto es de hace más de {(int)UmbralesEstado.RespaldoMaximo.TotalHours} horas ({cuando}).",
                "Revise la tarea programada y el espacio en disco.");

        var copias = new List<string>();
        if (m.Copia2 == true) copias.Add("copia 2");
        if (m.Copia3 == true) copias.Add("copia 3 (fuera de sitio)");
        var detalle = $"{m.Archivo} · {Tamano(m.Bytes ?? 0)}" + (copias.Count > 0 ? $" · {string.Join(" y ", copias)}" : " · sin copias adicionales");
        return new(NivelEstado.Correcto, $"Último respaldo correcto: {cuando}.", detalle);
    }

    private ElementoEstado EvaluarRestauracion()
    {
        var m = Leer("ultima-restauracion.json", out var problema);
        if (problema is not null || m is null)
            return new(NivelEstado.Aviso, "Aún no se ha probado restaurar un respaldo.",
                problema ?? "Ejecute deploy\\Probar-Restauracion.ps1: un respaldo que nunca se restauró no es una garantía.");

        var cuando = Hora(m.Fecha!.Value);
        if (!string.Equals(m.Resultado, "ok", StringComparison.OrdinalIgnoreCase))
            return new(NivelEstado.Aviso, $"La última prueba de restauración ({cuando}) falló.", m.Mensaje);
        if (reloj.GetUtcNow() - m.Fecha.Value > UmbralesEstado.RestauracionMaxima)
            return new(NivelEstado.Aviso, $"La última prueba de restauración es de hace más de {(int)UmbralesEstado.RestauracionMaxima.TotalDays} días ({cuando}).",
                "Se recomienda probar la restauración cada trimestre.");
        return new(NivelEstado.Correcto, $"Última prueba de restauración correcta: {cuando}.", m.Archivo);
    }

    // ---- Disco y certificado -------------------------------------------------------------------------------------

    private ElementoEstado EvaluarDisco()
    {
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(carpeta.Ruta))!;
            var disco = new DriveInfo(raiz);
            var libre = disco.AvailableFreeSpace;
            var texto = $"{Tamano(libre)} libres de {Tamano(disco.TotalSize)} en {raiz}";
            var poco = libre < UmbralesEstado.BytesLibresMinimos || (double)libre / disco.TotalSize < UmbralesEstado.FraccionLibreMinima;
            return poco
                ? new(NivelEstado.Aviso, "Queda poco espacio en el disco.", texto)
                : new(NivelEstado.Correcto, texto);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return new(NivelEstado.Aviso, "No se pudo consultar el disco.");
        }
    }

    private ElementoEstado EvaluarCertificado()
    {
        var seccion = config.GetSection("Kestrel:Endpoints:Https:Certificate");
        var subject = seccion["Subject"];
        if (string.IsNullOrWhiteSpace(subject))
            return new(NivelEstado.Correcto, "HTTPS no está configurado en esta instalación.",
                entorno.IsDevelopment() ? "Normal en desarrollo." : "Revise appsettings.Production.json.");

        try
        {
            var nombreAlmacen = seccion["Store"] ?? "My";
            var ubicacion = Enum.TryParse<StoreLocation>(seccion["Location"], true, out var u) ? u : StoreLocation.LocalMachine;
            using var almacen = new X509Store(nombreAlmacen, ubicacion);
            almacen.Open(OpenFlags.ReadOnly);
            var cert = almacen.Certificates.Find(X509FindType.FindBySubjectName, subject, validOnly: false)
                .OrderByDescending(c => c.NotAfter).FirstOrDefault();
            if (cert is null)
                return new(NivelEstado.Aviso, $"No se encontró el certificado «{subject}» en el almacén.");

            var dias = (int)(cert.NotAfter.ToUniversalTime() - reloj.GetUtcNow()).TotalDays;
            var vence = cert.NotAfter.ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-GT"));
            if (dias < 0) return new(NivelEstado.Aviso, $"El certificado HTTPS venció el {vence}.", "Renueve con deploy\\Nuevo-CertificadoServidor.ps1.");
            if (dias < UmbralesEstado.DiasAvisoCertificado)
                return new(NivelEstado.Aviso, $"El certificado HTTPS vence en {dias} días ({vence}).", "Renueve con deploy\\Nuevo-CertificadoServidor.ps1.");
            return new(NivelEstado.Correcto, $"Certificado HTTPS vigente hasta el {vence}.");
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or ArgumentException or PlatformNotSupportedException)
        {
            return new(NivelEstado.Aviso, "No se pudo consultar el certificado HTTPS.");
        }
    }

    private static string Tamano(long bytes)
    {
        string[] unidades = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < unidades.Length - 1) { v /= 1024; i++; }
        return string.Create(CultureInfo.GetCultureInfo("es-GT"), $"{v:0.#} {unidades[i]}");
    }
}
