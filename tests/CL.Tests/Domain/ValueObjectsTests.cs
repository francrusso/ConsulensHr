using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Tests.Domain;

public class ValueObjectsTests
{
    [Theory]
    [InlineData("RSSMRA85T10A562S")]
    [InlineData("rssmra85t10a562s")]
    [InlineData("VRDLGU90A01H501S")]
    public void CodiceFiscale_valido(string input) =>
        Assert.Equal(input.ToUpperInvariant(), CodiceFiscale.Parse(input).Valore);

    [Theory]
    [InlineData("RSSMRA85T10A562X")] // controllo errato
    [InlineData("RSSMRA85Z10A562S")] // mese inesistente
    [InlineData("RSSMRA85T10A562")]  // troppo corto
    [InlineData("")]
    public void CodiceFiscale_non_valido(string input) =>
        Assert.Throws<DomainException>(() => CodiceFiscale.Parse(input));

    [Theory]
    [InlineData("01234567897")]
    [InlineData("IT12345678903")]
    public void PartitaIva_valida(string input) => Assert.Equal(11, PartitaIva.Parse(input).Valore.Length);

    [Theory]
    [InlineData("01234567890")]
    [InlineData("1234567890")]
    [InlineData("ABCDEFGHIJK")]
    public void PartitaIva_non_valida(string input) =>
        Assert.Throws<DomainException>(() => PartitaIva.Parse(input));

    [Fact]
    public void Iban_valido_con_spazi() =>
        Assert.Equal("IT60X0542811101000000123456", Iban.Parse("it60 x054 2811 1010 0000 0123 456").Valore);

    [Theory]
    [InlineData("IT61X0542811101000000123456")] // check digit errato
    [InlineData("IT60X054281110100000012345")]  // lunghezza IT errata
    public void Iban_non_valido(string input) => Assert.Throws<DomainException>(() => Iban.Parse(input));
}
