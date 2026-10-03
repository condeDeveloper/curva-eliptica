using System.Numerics;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// Os tres jeitos de multiplicar um ponto por um escalar, contra o juiz que
/// soma um a um.
///
/// Eles precisam dar a MESMA resposta sempre, e precisam ter perfis de execucao
/// diferentes. As duas coisas sao testadas, porque a segunda e o motivo de
/// existirem tres.
/// </summary>
public class EscalarTest
{
    /// <summary>
    /// Os tres contra a forca bruta, em TODO ponto da curva pequena e em todo k
    /// de 0 ate passar da ordem. Nao e amostra: e o dominio.
    /// </summary>
    [Fact]
    public void OsTresMetodosConcordamComAForcaBruta()
    {
        var curva = CurvaTest.Pequena();
        foreach (var p in CurvaTest.TodosOsPontos(curva))
            for (var k = 0; k <= 40; k++)
            {
                var esperado = Escalar.ForcaBruta(curva, p, k);
                Assert.Equal(esperado, Escalar.DobrarESomar(curva, p, k));
                Assert.Equal(esperado, Escalar.Escada(curva, p, k));
                Assert.Equal(esperado, Escalar.Wnaf(curva, p, k, 2));
                Assert.Equal(esperado, Escalar.Wnaf(curva, p, k, 4));
                Assert.Equal(esperado, Escalar.Wnaf(curva, p, k, 5));
            }
    }

    /// <summary>
    /// Escalar NEGATIVO: -k*P tem que ser o negativo de k*P. E o caso que some
    /// quando se escreve a multiplicacao pensando so em k positivo.
    /// </summary>
    [Fact]
    public void EscalarNegativoDaOPontoNegado()
    {
        var curva = CurvaTest.Pequena();
        foreach (var p in CurvaTest.TodosOsPontos(curva))
            for (var k = 1; k <= 12; k++)
            {
                var negado = curva.Negar(Escalar.ForcaBruta(curva, p, k));
                Assert.Equal(negado, Escalar.DobrarESomar(curva, p, -k));
                Assert.Equal(negado, Escalar.Escada(curva, p, -k));
                Assert.Equal(negado, Escalar.Wnaf(curva, p, -k));
            }
    }

    /// <summary>
    /// A diferenca que importa: o metodo obvio faz uma soma POR BIT 1 do
    /// escalar, e a escada faz uma soma por bit, ponto.
    ///
    /// O numero de somas do primeiro e, literalmente, a quantidade de bits 1 da
    /// chave privada. Quem mede o tempo da conta, ou o consumo de corrente do
    /// chip, le esse numero. Nao e teoria: medir tempo e consumo ja recuperou
    /// chave de produto no mercado.
    ///
    /// A escada paga por isso: ela faz o DOBRO de somas em media. E esse e o
    /// preco de nao vazar, e ele vale a pena.
    /// </summary>
    [Fact]
    public void AEscadaNaoDeixaOTempoDependerDoEscalar()
    {
        var curva = Secp256k1.Curva;
        var g = curva.Geradora;

        // Dois escalares do mesmo tamanho, um quase todo 1 e outro quase todo 0.
        var cheio = (BigInteger.One << 200) - 1;              // 200 bits, todos 1
        var vazio = BigInteger.One << 199;                    // 200 bits, um 1 so

        Escalar.DobrarESomar(curva, g, cheio, out var somasCheio, out var dobrosCheio);
        Escalar.DobrarESomar(curva, g, vazio, out var somasVazio, out var dobrosVazio);
        Assert.Equal(200, somasCheio);
        Assert.Equal(1, somasVazio);
        Assert.Equal(dobrosCheio, dobrosVazio);

        Escalar.Escada(curva, g, cheio, out var escadaCheio, out var escadaDobrosCheio);
        Escalar.Escada(curva, g, vazio, out var escadaVazio, out var escadaDobrosVazio);
        Assert.Equal(escadaCheio, escadaVazio);
        Assert.Equal(escadaDobrosCheio, escadaDobrosVazio);
        Assert.Equal(200, escadaCheio);
        Assert.Equal(200, escadaDobrosCheio);

        // E ela faz mais trabalho que o metodo obvio no caso tipico. E o preco.
        Assert.True(escadaCheio + escadaDobrosCheio > somasVazio + dobrosVazio);
    }

    /// <summary>
    /// O numero de somas do metodo obvio E a contagem de bits 1 do escalar,
    /// conferida bit a bit em cem escalares.
    /// </summary>
    [Fact]
    public void AsSomasDoMetodoObvioContamOsBitsUm()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            var k = Sortear(sorteio, 64);
            Escalar.DobrarESomar(curva, curva.Geradora, k, out var somas, out var dobros);
            var uns = 0;
            for (var bit = 0; bit < k.GetBitLength(); bit++)
                if (!((k >> bit) & BigInteger.One).IsZero) uns++;
            Assert.Equal(uns, somas);
            Assert.Equal((int)k.GetBitLength(), dobros);
        }
    }

    /// <summary>
    /// Os digitos da forma nao adjacente, contra a definicao:
    ///
    /// - eles reconstroem o numero;
    /// - cada digito nao nulo e IMPAR e cabe na janela;
    /// - entre dois nao nulos ha pelo menos janela-1 zeros.
    ///
    /// E dessa ultima propriedade que vem a economia: a densidade de digitos nao
    /// nulos cai de 1/2 para 1/(janela+1).
    /// </summary>
    [Fact]
    public void OsDigitosNaoAdjacentesObedecemADefinicao()
    {
        var sorteio = new Random(11);
        for (var janela = 2; janela <= 6; janela++)
            for (var i = 0; i < 200; i++)
            {
                var k = Sortear(sorteio, 32);
                var digitos = Escalar.FormaNaoAdjacente(k, janela);

                BigInteger soma = 0;
                for (var j = digitos.Count - 1; j >= 0; j--) soma = soma * 2 + digitos[j];
                Assert.Equal(k, soma);

                var limite = 1 << (janela - 1);
                var ultimoNaoNulo = -100;
                for (var j = 0; j < digitos.Count; j++)
                {
                    if (digitos[j] == 0) continue;
                    Assert.True(Math.Abs(digitos[j]) % 2 == 1, $"digito {digitos[j]} nao e impar");
                    Assert.InRange(digitos[j], -limite + 1, limite - 1);
                    Assert.True(j - ultimoNaoNulo >= janela,
                        $"digitos nao nulos em {ultimoNaoNulo} e {j}, com janela {janela}");
                    ultimoNaoNulo = j;
                }
            }
    }

    /// <summary>
    /// O ganho do wNAF, MEDIDO.
    ///
    /// Eu ia escrever que a economia vem da janela. Vem das duas coisas, e a
    /// conta mostra qual pesa: com janela 4 e escalar de 256 bits, o metodo
    /// obvio faz umas 128 somas e o wNAF faz umas 60, incluindo as 7 da tabela
    /// pre-computada. A densidade de 1/(w+1) prevista pela teoria da 256/5 = 51
    /// somas de laco, e e isso que aparece.
    /// </summary>
    [Fact]
    public void OWnafFazMenosSomasQueOMetodoObvio()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(3);
        long totalObvio = 0, totalWnaf = 0;

        for (var i = 0; i < 20; i++)
        {
            var k = Sortear(sorteio, 256) % curva.Ordem;
            var pelaWnaf = Escalar.Wnaf(curva, curva.Geradora, k, 4, out var somasWnaf, out _);
            var peloObvio = Escalar.DobrarESomar(curva, curva.Geradora, k, out var somasObvio, out _);
            Assert.Equal(peloObvio, pelaWnaf);
            totalObvio += somasObvio;
            totalWnaf += somasWnaf;
        }

        var medioObvio = totalObvio / 20.0;
        var medioWnaf = totalWnaf / 20.0;
        Assert.InRange(medioObvio, 110, 145);
        Assert.InRange(medioWnaf, 45, 75);
        Assert.True(medioWnaf < medioObvio * 0.65,
            $"wNAF fez {medioWnaf:F1} somas contra {medioObvio:F1}; a economia sumiu");
    }

    /// <summary>
    /// Os tres metodos na secp256k1 de verdade, em escalares grandes. Aqui nao
    /// ha forca bruta possivel, entao o juiz e a concordancia entre caminhos
    /// independentes.
    /// </summary>
    [Fact]
    public void OsTresConcordamNaCurvaGrande()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(5);
        for (var i = 0; i < 12; i++)
        {
            var k = Sortear(sorteio, 256) % curva.Ordem;
            var escada = Escalar.Escada(curva, curva.Geradora, k);
            Assert.Equal(escada, Escalar.DobrarESomar(curva, curva.Geradora, k));
            Assert.Equal(escada, Escalar.Wnaf(curva, curva.Geradora, k, 4));
            Assert.Equal(escada, Escalar.Wnaf(curva, curva.Geradora, k, 6));
            Assert.True(curva.Contem(escada));
        }
    }

    /// <summary>
    /// A distributividade: (a + b)*P = a*P + b*P, inclusive quando a + b passa da
    /// ordem e volta.
    /// </summary>
    [Fact]
    public void MultiplicacaoEscalarDistribui()
    {
        var curva = CurvaTest.Pequena();
        var pontos = CurvaTest.TodosOsPontos(curva);
        foreach (var p in pontos)
            for (var a = 0; a <= 10; a++)
                for (var b = 0; b <= 10; b++)
                    Assert.Equal(Escalar.Escada(curva, p, a + b),
                                 curva.Somar(Escalar.Escada(curva, p, a), Escalar.Escada(curva, p, b)));
    }

    internal static BigInteger Sortear(Random sorteio, int bits)
    {
        var bytes = new byte[bits / 8 + 1];
        sorteio.NextBytes(bytes);
        bytes[^1] = 0;
        var x = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        return x % (BigInteger.One << bits) + 1;
    }
}
