using System.Numerics;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// Diffie-Hellman na curva, e o ataque que ele sofre quando ninguem confere a
/// chave que chegou.
/// </summary>
public class DhTest
{
    /// <summary>
    /// Os dois lados chegam ao mesmo segredo. E chegam porque a multiplicacao
    /// escalar COMUTA, nao por acaso: o teste tambem confere isso direto.
    /// </summary>
    [Fact]
    public void OsDoisLadosChegamAoMesmoSegredo()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(61);
        for (var i = 0; i < 8; i++)
        {
            var da = EscalarTest.Sortear(sorteio, 255) % curva.Ordem;
            var db = EscalarTest.Sortear(sorteio, 255) % curva.Ordem;
            if (da.IsZero || db.IsZero) continue;

            var qa = Dh.Publica(curva, da);
            var qb = Dh.Publica(curva, db);

            Assert.Equal(Dh.Combinar(curva, da, qb), Dh.Combinar(curva, db, qa));
            Assert.Equal(Dh.Segredo(curva, da, qb), Dh.Segredo(curva, db, qa));
            Assert.Equal(32, Dh.Segredo(curva, da, qb).Length);

            // E o segredo e (da*db)*G, que e o que comutar quer dizer.
            Assert.Equal(Escalar.Escada(curva, curva.Geradora, da * db % curva.Ordem),
                         Dh.Combinar(curva, da, qb));
        }
    }

    [Fact]
    public void SegredosDePessoasDiferentesSaoDiferentes()
    {
        var curva = Secp256k1.Curva;
        var vistos = new HashSet<string>();
        for (var d = 2; d < 12; d++)
        {
            var segredo = Convert.ToHexString(Dh.Segredo(curva, 7, Dh.Publica(curva, d)));
            Assert.True(vistos.Add(segredo), $"o segredo repetiu em d = {d}");
        }
    }

    /// <summary>
    /// A chave publica da outra ponta passa por tres conferencias, e cada uma
    /// recusa um caso diferente.
    /// </summary>
    [Fact]
    public void ChavePublicaRuimEhRecusada()
    {
        var curva = Secp256k1.Curva;
        var boa = Dh.Publica(curva, 12345);

        Assert.Throws<ArgumentException>(() => Dh.Combinar(curva, 9, Ponto.NoInfinito));
        Assert.Throws<ArgumentException>(() => Dh.Combinar(curva, 9, new Ponto(boa.X, boa.Y + 1)));
        Assert.Throws<ArgumentException>(() => Dh.Combinar(curva, 9, new Ponto(curva.P + 1, boa.Y)));
        Assert.Throws<ArgumentException>(() => Dh.Combinar(curva, 9, new Ponto(boa.X, curva.P)));
        // A boa passa.
        Assert.True(curva.Contem(Dh.Combinar(curva, 9, boa)));
    }

    [Fact]
    public void ChavePrivadaForaDaFaixaNaoGeraPublica()
    {
        var curva = Secp256k1.Curva;
        Assert.Throws<ArgumentOutOfRangeException>(() => Dh.Publica(curva, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dh.Publica(curva, curva.Ordem));
    }

    /// <summary>
    /// O ATAQUE DE CURVA INVALIDA, executado numa curva pequena.
    ///
    /// A ideia: as formulas de soma nunca olham o coeficiente b, entao um ponto
    /// que nao esta na curva certa ainda assim e aceito pela aritmetica, e ele
    /// vive numa curva DIFERENTE, com outra ordem. Escolhendo uma curva cuja
    /// ordem tem fator pequeno, o atacante manda um ponto de ordem pequena; o
    /// resultado d*P so pode ser um punhado de pontos, e comparando com a lista
    /// ele descobre d modulo aquela ordem pequena.
    ///
    /// Repetindo com varias ordens pequenas e juntando pelo teorema chines dos
    /// restos, sai o d inteiro.
    ///
    /// O teste mostra o passo que importa: com o ponto de fora, UM segredo em
    /// poucos entrega o resto da chave; com a conferencia ligada, nem chega a
    /// acontecer. Esse ataque ja derrubou implementacao de TLS em producao.
    /// </summary>
    [Fact]
    public void OAtaqueDeCurvaInvalidaVazaAChaveQuandoNinguemConfere()
    {
        // y² = x³ + 2x + 3 sobre F(103) e a curva legitima.
        var curva = CurvaTest.Pequena();
        var segredo = new BigInteger(57);

        // Um ponto que NAO esta nela. Varro ate achar um cuja ordem, na curva
        // que ele habita de fato, seja pequena.
        Ponto intruso = default;
        var ordemPequena = BigInteger.Zero;
        for (var x = BigInteger.One; x < curva.P && ordemPequena.IsZero; x++)
            for (var y = BigInteger.One; y < curva.P; y++)
            {
                var p = new Ponto(x, y);
                if (curva.Contem(p)) continue;
                var ordem = OrdemPelaSoma(curva, p);
                if (ordem >= 2 && ordem <= 8) { intruso = p; ordemPequena = ordem; break; }
            }

        Assert.False(ordemPequena.IsZero, "nao achei ponto de ordem pequena fora da curva");
        Assert.False(curva.Contem(intruso));

        // Com a conferencia, o ponto nem entra.
        Assert.Throws<ArgumentException>(() => Dh.Combinar(curva, segredo, intruso));

        // Sem ela, o resultado e um dos poucos multiplos do intruso, e comparar
        // com a lista entrega o segredo modulo a ordem pequena.
        var resultado = Dh.CombinarSemConferir(curva, segredo, intruso);
        BigInteger? descoberto = null;
        for (var i = BigInteger.Zero; i < ordemPequena; i++)
            if (Escalar.ForcaBruta(curva, intruso, (int)i) == resultado) { descoberto = i; break; }

        Assert.NotNull(descoberto);
        Assert.Equal(Mod.Normalizar(segredo, ordemPequena), descoberto!.Value);
        // E isso e informacao de verdade sobre a chave: ela nao e mais qualquer uma.
        Assert.True(ordemPequena > 1);
    }

    /// <summary>A ordem de um ponto somando um a um, sem exigir que ele esteja na curva.</summary>
    private static BigInteger OrdemPelaSoma(Curva curva, Ponto p)
    {
        var atual = p;
        for (var k = BigInteger.One; k <= curva.P * 2 + 2; k++)
        {
            if (atual.Infinito) return k;
            atual = curva.Somar(atual, p);
        }
        return BigInteger.Zero;
    }
}
