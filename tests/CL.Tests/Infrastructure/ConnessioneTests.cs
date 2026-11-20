namespace CL.Tests.Infrastructure;

public sealed class ConnessioneTests
{
    [Fact]
    public void Le_connessioni_inattive_si_chiudono_prima_del_pooler()
    {
        var s = CL.Infrastructure.DependencyInjection.ConnessioneRobusta("Host=db.example;Port=5432;Database=postgres;Username=u;Password=p;SSL Mode=Require");
        var b = new Npgsql.NpgsqlConnectionStringBuilder(s);
        Assert.Equal((60, 30, 600), (b.ConnectionIdleLifetime, b.KeepAlive, b.ConnectionLifetime));

        // Se nella stringa di connessione c'è già un valore, resta quello.
        b = new Npgsql.NpgsqlConnectionStringBuilder(CL.Infrastructure.DependencyInjection.ConnessioneRobusta("Host=x;Connection Idle Lifetime=20"));
        Assert.Equal(20, b.ConnectionIdleLifetime);
    }
}
