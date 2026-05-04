using AgentFlow.Domain.Helpers;
using Xunit;

namespace CampaignIntakeTests;

public class PhoneValidatorTests
{
    [Theory]
    [InlineData("60001234", "+50760001234")]                 // celular sin código país
    [InlineData("+507 6000-1234", "+50760001234")]           // formato con prefijo y separadores
    [InlineData("(507) 6000 1234", "+50760001234")]          // paréntesis y espacios
    [InlineData("50760001234", "+50760001234")]              // ya tiene 507 sin '+'
    public void Validate_NormalizaPanama(string raw, string esperado)
    {
        var resultado = PhoneValidator.Validate(raw, "507");
        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData("507507")]                                   // bug TalkIA: doble prefijo sin número
    [InlineData("+507507")]                                  // doble prefijo con '+'
    [InlineData("50750760001234")]                           // doble prefijo + número válido
    [InlineData("+507507 6000-1234")]                        // doble prefijo formateado
    public void Validate_RechazaDobleCodigoPais(string raw)
    {
        var resultado = PhoneValidator.Validate(raw, "507");
        Assert.Null(resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000000000")]                               // todo ceros
    [InlineData("abc")]                                      // sin dígitos
    [InlineData("+1")]                                       // demasiado corto
    public void Validate_RechazaInvalidos(string? raw)
    {
        var resultado = PhoneValidator.Validate(raw, "507");
        Assert.Null(resultado);
    }

    [Fact]
    public void Validate_FuncionaParaOtrosCodigos()
    {
        Assert.Equal("+573001234567", PhoneValidator.Validate("3001234567", "57"));
        Assert.Equal("+5215512345678", PhoneValidator.Validate("5215512345678", "52"));
    }

    [Fact]
    public void Validate_RechazaDobleCodigoConOtroPais()
    {
        Assert.Null(PhoneValidator.Validate("57573001234567", "57"));
    }
}
