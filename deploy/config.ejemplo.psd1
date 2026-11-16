# Copiar a C:\ProgramData\Parroquia\config\deploy.psd1 y ajustar. NO subir la copia real al repositorio.
@{
    Servidor = @{
        Nombre      = 'parroquia.local'   # nombre fijo con el que los clientes abren el sistema (entrada en su archivo hosts)
        Ip          = '192.168.1.50'      # IP fija (o reserva DHCP) de este equipo
        PuertoHttps = 443
        PuertoHttp  = 80
    }
    Rutas = @{
        App   = 'C:\Parroquia\app'              # binarios publicados
        Datos = 'C:\ProgramData\Parroquia'      # config, claves, logs, respaldos
    }
    Servicio = @{
        Nombre = 'ParroquiaWeb'
    }
    PostgreSql = @{
        Bin      = 'C:\Program Files\PostgreSQL\17\bin'
        Servicio = 'postgresql-x64-17'
        Host     = 'localhost'
        Puerto   = 5432
        Base     = 'parroquia'
        Usuario  = 'parroquia_app'
    }
    Respaldo = @{
        Hora      = '22:00'
        Diarios   = 30     # los últimos N respaldos
        Mensuales = 12     # además, el más antiguo de cada uno de los últimos N meses
        Copia2    = ''     # p. ej. 'D:\RespaldosParroquia' (segundo disco o USB). Vacío = sin copia 2
        Copia3    = ''     # carpeta de la copia fuera de sitio (USB rotado, NAS, carpeta sincronizada). Va CIFRADA. Vacío = sin copia 3
    }
}
