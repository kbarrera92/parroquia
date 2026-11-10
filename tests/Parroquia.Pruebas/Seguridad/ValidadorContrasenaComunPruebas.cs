using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Pruebas.Seguridad;

public class ValidadorContrasenaComunPruebas
{
    [Theory]
    [InlineData("password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("Contraseña2026")]
    [InlineData("administrador")]
    public void Rechaza_contrasenas_comunes_aunque_cambien_mayusculas_o_lleven_digitos_al_final(string password)
    {
        var errores = ValidadorContrasenaComun.Evaluar(password, "mperez");

        errores.Select(e => e.Code).Should().Contain(ValidadorContrasenaComun.CodigoComun);
    }

    [Fact]
    public void Rechaza_contrasena_que_contiene_el_usuario()
    {
        var errores = ValidadorContrasenaComun.Evaluar("xx-MPerez-vela-azul", "mperez");

        errores.Select(e => e.Code).Should().Contain(ValidadorContrasenaComun.CodigoContieneUsuario);
    }

    [Theory]
    [InlineData("la-catedral-es-bonita")]
    [InlineData("vamos a Mazatenango hoy")]
    [InlineData("san Bartolomé apóstol")]
    public void Rechaza_contrasena_con_palabras_de_la_parroquia(string password)
    {
        var errores = ValidadorContrasenaComun.Evaluar(password, "mperez");

        errores.Select(e => e.Code).Should().Contain(ValidadorContrasenaComun.CodigoContieneContexto);
    }

    [Fact]
    public void Rechaza_contrasena_demasiado_larga()
    {
        var errores = ValidadorContrasenaComun.Evaluar(new string('x', PoliticaContrasena.LongitudMaxima + 1), "mperez");

        errores.Select(e => e.Code).Should().Contain(ValidadorContrasenaComun.CodigoMuyLarga);
    }

    [Theory]
    [InlineData("vela azul puente nube")]
    [InlineData("ñandú cruza el río 7 veces")]
    [InlineData("T9k-Rw4m-Qz8p")]
    public void Acepta_frases_y_claves_aleatorias_sin_reglas_de_composicion(string password)
    {
        ValidadorContrasenaComun.Evaluar(password, "mperez").Should().BeEmpty();
    }

    [Fact]
    public void Un_usuario_corto_no_invalida_contrasenas_por_coincidencia_accidental()
    {
        // "ab" aparece en casi cualquier texto: solo se compara el usuario si tiene 3 o más caracteres.
        ValidadorContrasenaComun.Evaluar("vela azul puente nube", "ab").Should().BeEmpty();
    }
}
