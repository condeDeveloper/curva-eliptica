using System.Numerics;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// A aritmetica do corpo, conferida por VARREDURA.
///
/// Com p = 103 da para percorrer o corpo inteiro, e um inverso conferido em
/// todos os 102 elementos nao nulos nao deixa canto para um caso especial se
/// esconder. A forca bruta aqui nao e preguica: e o juiz.
/// </summary>
public class ModTest
{
    private const int P = 103;

    [Fact]
    public void NormalizarSempreCaiNoIntervalo()
    {
        for (var x = -500; x <= 500; x++)
        {
            var r = Mod.Normalizar(x, P);
            Assert.InRange((int)r, 0, P - 1);
            Assert.Equal(0, (int)Mod.Normalizar(x - r, P));
        }
    }

    /// <summary>
    /// O inverso de Euclides contra a definicao: a*x = 1. Em todos os elementos.
    /// </summary>
    [Fact]
    public void InversoBateComADefinicaoNoCorpoInteiro()
    {
        for (var a = 1; a < P; a++)
        {
            var inv = Mod.Inverso(a, P);
            Assert.InRange((int)inv, 1, P - 1);
            Assert.Equal(BigInteger.One, Mod.Multiplicar(a, inv, P));
            // E ele coincide com o de Fermat, a^(p-2), que e o outro caminho.
            Assert.Equal(inv, Mod.Potencia(a, P - 2, P));
        }
    }

    [Fact]
    public void ZeroNaoTemInverso()
    {
        Assert.Throws<DivideByZeroException>(() => Mod.Inverso(0, P));
        Assert.Throws<DivideByZeroException>(() => Mod.Inverso(P, P));
        Assert.Throws<DivideByZeroException>(() => Mod.Inverso(-P, P));
    }

    /// <summary>
    /// Potencia modular contra a multiplicacao repetida. A exponenciacao rapida
    /// erra justo nos expoentes pequenos e no zero, que e onde a repetida e
    /// trivialmente certa.
    /// </summary>
    [Fact]
    public void PotenciaBateComAMultiplicacaoRepetida()
    {
        for (var b = 0; b < P; b++)
        {
            var acumulado = BigInteger.One;
            for (var e = 0; e < 40; e++)
            {
                Assert.Equal(acumulado, Mod.Potencia(b, e, P));
                acumulado = Mod.Multiplicar(acumulado, b, P);
            }
        }
    }

    /// <summary>
    /// A raiz quadrada modular contra a varredura: o que ela devolve eleva ao
    /// quadrado e da o numero, e o que ela recusa nao e quadrado de ninguem.
    /// </summary>
    [Fact]
    public void RaizQuadradaBateComAVarredura()
    {
        var quadrados = new HashSet<int>();
        for (var y = 0; y < P; y++) quadrados.Add((int)Mod.Multiplicar(y, y, P));

        for (var a = 0; a < P; a++)
        {
            var raiz = Mod.RaizQuadrada(a, P);
            if (quadrados.Contains(a))
            {
                Assert.NotNull(raiz);
                Assert.Equal((BigInteger)a, Mod.Multiplicar(raiz!.Value, raiz.Value, P));
            }
            else
            {
                Assert.Null(raiz);
            }
            Assert.Equal(quadrados.Contains(a), Mod.EhQuadrado(a, P));
        }

        // Metade dos nao nulos sao quadrados: (p-1)/2, mais o zero.
        Assert.Equal((P - 1) / 2 + 1, quadrados.Count);
    }

    /// <summary>
    /// Com p = 1 mod 4 o atalho de Shanks NAO vale, e a funcao recusa em vez de
    /// devolver lixo.
    ///
    /// Isso me custou um teste: eu tinha escolhido 97 para a curva pequena, que e
    /// 1 mod 4, e a descompressao estourou. Recusar foi o comportamento certo;
    /// se ela tivesse devolvido a^((p+1)/4) assim mesmo, o erro teria aparecido
    /// como ponto errado la na frente, longe da causa.
    /// </summary>
    [Fact]
    public void RaizQuadradaRecusaPrimoDeRestoUm()
    {
        Assert.Equal(1, 97 % 4);
        Assert.Throws<NotSupportedException>(() => Mod.RaizQuadrada(5, 97));
        // Zero sai antes da conferencia, porque a raiz de zero e zero em qualquer p.
        Assert.Equal(BigInteger.Zero, Mod.RaizQuadrada(0, 97));
    }

    /// <summary>
    /// Somar e Subtrair assumem entrada ja reduzida e trabalham sem divisao. O
    /// teste confere isso contra o resto de verdade, no corpo inteiro.
    /// </summary>
    [Fact]
    public void SomarESubtrairBatemComOResto()
    {
        for (var a = 0; a < P; a++)
            for (var b = 0; b < P; b++)
            {
                Assert.Equal(Mod.Normalizar(a + b, P), Mod.Somar(a, b, P));
                Assert.Equal(Mod.Normalizar(a - b, P), Mod.Subtrair(a, b, P));
                Assert.Equal(Mod.Normalizar(a * b, P), Mod.Multiplicar(a, b, P));
                if (b != 0) Assert.Equal((BigInteger)a, Mod.Multiplicar(Mod.Dividir(a, b, P), b, P));
            }
    }
}
