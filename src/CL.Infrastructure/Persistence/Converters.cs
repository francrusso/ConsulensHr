using CL.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CL.Infrastructure.Persistence;

// I value object sono salvati come stringa normalizzata e rivalidati in lettura.
internal sealed class CodiceFiscaleConverter() : ValueConverter<CodiceFiscale, string>(v => v.Valore, s => CodiceFiscale.Parse(s));
internal sealed class PartitaIvaConverter() : ValueConverter<PartitaIva, string>(v => v.Valore, s => PartitaIva.Parse(s));
internal sealed class IbanConverter() : ValueConverter<Iban, string>(v => v.Valore, s => Iban.Parse(s));
