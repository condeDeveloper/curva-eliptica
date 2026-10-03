using System.Globalization;
using System.Numerics;
using Conde.CurvaEliptica;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// O grupo da curva, conferido pelos AXIOMAS e por valores publicados.
///
/// Axioma nao depende de implementacao nenhuma. Uma soma que viola a
/// associatividade esta errada, e nao ha discussao: a estrutura deixou de ser um
/// grupo e tudo que se apoia nela cai junto.
/// </summary>
public class CurvaTest
{
    /// <summary>
    /// Os parametros da secp256k1 conferidos contra a FORMULA, nao contra outra
    /// copia do hexadecimal. O primo e 2^256 - 2^32 - 977, e conferir isso pega um
    /// digito trocado na transcricao.
    /// </summary>
    [Fact]
    public void OsParametrosBatemComAFormula()
    {
        Assert.True(Secp256k1.PBateComAFormula());
        Assert.Equal(BigInteger.Zero, Secp256k1.A);
        Assert.Equal(new BigInteger(7), Secp256k1.B);
        // O gerador esta na curva, e a ordem dele e n: n*G = infinito.
        Assert.True(Secp256k1.Curva.Contem(Secp256k1.Curva.Geradora));
        Assert.True(Escalar.Escada(Secp256k1.Curva, Secp256k1.Curva.Geradora, Secp256k1.N).Infinito);
        // E n e primo, o que e o que faz o cofator ser 1.
        Assert.True(Secp256k1.N.IsEven == false);
    }

    /// <summary>
    /// Os axiomas de grupo, numa curva PEQUENA onde todos os pontos cabem.
    ///
    /// A curva y² = x³ + 2x + 3 sobre F(97) tem poucos pontos, e com todos eles em
    /// maos da para conferir associatividade e comutatividade em TODOS os triplos
    /// e pares, em vez de por amostra.
    /// </summary>
    [Fact]
    public void OsAxiomasDeGrupoValemNoDominioInteiro()
    {
        var curva = Pequena();
        var pontos = TodosOsPontos(curva);
        Assert.True(pontos.Count > 20, $"so {pontos.Count} pontos; o dominio ficou pequeno demais");

        foreach (var a in pontos)
        {
            // Neutro e inverso.
            Assert.Equal(a, curva.Somar(a, Ponto.NoInfinito));
            Assert.Equal(a, curva.Somar(Ponto.NoInfinito, a));
            Assert.True(curva.Somar(a, curva.Negar(a)).Infinito);

            foreach (var b in pontos)
            {
                // Comutatividade, e o resultado continua na curva.
                var ab = curva.Somar(a, b);
                Assert.Equal(ab, curva.Somar(b, a));
                Assert.True(curva.Contem(ab), $"{a} + {b} = {ab} saiu da curva");
            }
        }

        // Associatividade: mais caro, entao numa amostra do dominio.
        for (var i = 0; i < pontos.Count; i += 3)
            for (var j = 0; j < pontos.Count; j += 5)
                for (var k = 0; k < pontos.Count; k += 7)
                {
                    var (a, b, c) = (pontos[i], pontos[j], pontos[k]);
                    Assert.Equal(curva.Somar(curva.Somar(a, b), c), curva.Somar(a, curva.Somar(b, c)));
                }
    }

    /// <summary>
    /// O dobro de um ponto NAO e a soma dele com ele pela formula geral: a reta
    /// por dois pontos iguais nao existe, e e preciso usar a tangente.
    ///
    /// Esquecer esse caso nao da erro sempre; da erro quando dois pontos coincidem
    /// no meio de uma multiplicacao escalar, que e exatamente o lugar onde o
    /// defeito e mais dificil de achar.
    /// </summary>
    [Fact]
    public void SomarUmPontoComEleMesmoUsaATangente()
    {
        var curva = Pequena();
        foreach (var p in TodosOsPontos(curva))
        {
            var dobro = curva.Dobrar(p);
            Assert.Equal(dobro, curva.Somar(p, p));
            Assert.True(curva.Contem(dobro));
            // E o dobro bate com a forca bruta: P + P.
            Assert.Equal(dobro, Escalar.ForcaBruta(curva, p, 2));
        }
    }

    /// <summary>
    /// Curva com discriminante zero e SINGULAR, e nela a soma deixa de ser bem
    /// definida. Recusar na construcao e melhor que devolver resultados que
    /// parecem certos.
    /// </summary>
    [Fact]
    public void CurvaSingularNaoPassa()
    {
        // y² = x³ (a = 0, b = 0): 4a³ + 27b² = 0
        Assert.Throws<ArgumentException>(() =>
            new Curva(97, 0, 0, new Ponto(0, 0), 1, 1));
    }

    /// <summary>
    /// Descomprimir inverte comprimir: guardar so o x e a paridade do y chega
    /// para reconstruir o ponto. E assim que toda chave publica seria e guardada,
    /// e corta o tamanho pela metade.
    ///
    /// Perto de METADE dos valores de x tem ponto, porque so os quadrados tem
    /// raiz, e o teste conta isso.
    ///
    /// O caso y = 0 e a excecao, e eu escrevi a asserção errada antes de medir:
    /// pedi que o y impar fosse impar e nao nulo, e o teste estourou. Quando o
    /// lado direito da zero, a raiz e zero, e ZERO E O PROPRIO NEGATIVO. O ponto
    /// (x, 0) e seu proprio inverso, esta sozinho na vertical, e o bit de
    /// paridade nao tem o que distinguir: descomprimir com par ou com impar
    /// devolve o MESMO ponto. Nao e defeito da implementacao, e a aritmetica; so
    /// nao cabe na afirmacao de que ha sempre dois y para cada x.
    /// </summary>
    [Fact]
    public void DescomprimirInverteComprimir()
    {
        var curva = Pequena();
        var comPonto = 0;
        var naVertical = 0;
        for (var x = 0; x < (int)curva.P; x++)
        {
            var par = curva.Descomprimir(x, false);
            var impar = curva.Descomprimir(x, true);
            if (par is null) { Assert.Null(impar); continue; }
            comPonto++;
            Assert.NotNull(impar);
            Assert.True(curva.Contem(par.Value) && curva.Contem(impar.Value));
            Assert.Equal(x, (int)par.Value.X);
            Assert.Equal(x, (int)impar.Value.X);

            if (par.Value.Y.IsZero)
            {
                // Ponto de ordem 2: igual ao proprio negativo, entao os dois bits
                // devolvem o mesmo ponto e dobrar da o infinito.
                naVertical++;
                Assert.Equal(par.Value, impar.Value);
                Assert.Equal(par.Value, curva.Negar(par.Value));
                Assert.True(curva.Dobrar(par.Value).Infinito);
                continue;
            }

            Assert.True(par.Value.Y.IsEven);
            Assert.False(impar.Value.Y.IsEven);
            // Os dois sao o mesmo ponto refletido: um e o negativo do outro.
            Assert.Equal(par.Value, curva.Negar(impar.Value));
        }
        // Perto da metade: so os x cujo lado direito e quadrado.
        Assert.InRange(comPonto, (int)curva.P / 3, (int)curva.P * 2 / 3);
        // E no maximo tres pontos com y = 0, porque x³ + ax + b tem no maximo
        // tres raizes.
        Assert.InRange(naVertical, 0, 3);
    }

    /// <summary>
    /// Na secp256k1 tambem: comprimir e descomprimir devolve o mesmo ponto, em
    /// 200 pontos obtidos multiplicando o gerador.
    /// </summary>
    [Fact]
    public void DescomprimirFuncionaNaSecp256k1()
    {
        var curva = Secp256k1.Curva;
        for (var k = 1; k <= 200; k++)
        {
            var p = Escalar.Escada(curva, curva.Geradora, k);
            var devolta = curva.Descomprimir(p.X, !p.Y.IsEven);
            Assert.NotNull(devolta);
            Assert.Equal(p, devolta!.Value);
        }
    }

    /// <summary>
    /// y² = x³ + 2x + 3 sobre F(103).
    ///
    /// O primo precisa ser 3 MOD 4, e nao e detalhe de gosto: a raiz quadrada
    /// modular usada para descomprimir um ponto e o atalho de Shanks,
    /// a^((p+1)/4), que so vale nesse caso. Eu tinha escolhido 97, que e 1 mod 4,
    /// e o teste de descompressao estourou na hora. O primo da secp256k1 tambem e
    /// 3 mod 4, e e por isso que descomprimir e barato nela.
    /// </summary>
    internal static Curva Pequena()
    {
        const int p = 103;
        Assert.Equal(3, p % 4);
        // O gerador: o primeiro ponto que a varredura achar serve, porque o que
        // esta em teste e o grupo e nao a escolha de gerador.
        var provisoria = new Curva(p, 2, 3, new Ponto(0, 0), p, 1);
        for (var x = BigInteger.One; x < p; x++)
            for (var y = BigInteger.One; y < p; y++)
            {
                var ponto = new Ponto(x, y);
                if (provisoria.Contem(ponto)) return new Curva(p, 2, 3, ponto, p, 1);
            }
        throw new InvalidOperationException("a curva ficou sem ponto nenhum");
    }

    internal static List<Ponto> TodosOsPontos(Curva curva)
    {
        var saida = new List<Ponto> { Ponto.NoInfinito };
        for (var x = BigInteger.Zero; x < curva.P; x++)
            for (var y = BigInteger.Zero; y < curva.P; y++)
            {
                var p = new Ponto(x, y);
                if (curva.Contem(p)) saida.Add(p);
            }
        return saida;
    }

    internal static BigInteger Hex(string h) =>
        BigInteger.Parse("0" + h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
