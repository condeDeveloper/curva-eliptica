using System.Numerics;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// A contagem de pontos e o logaritmo discreto.
///
/// E aqui que a seguranca vira NUMERO. A pergunta "quantos passos custa quebrar"
/// tem resposta, ela e a raiz quadrada da ordem, e dentro deste arquivo da para
/// quebrar de verdade uma curva pequena para ver a forma da conta.
/// </summary>
public class OrdemTest
{
    /// <summary>
    /// A contagem por varredura bate com a lista de pontos montada do outro
    /// jeito, e cai dentro do intervalo de Hasse.
    ///
    /// Hasse e teorema: |#E - (p+1)| &lt;= 2*raiz(p). Uma contagem fora disso nao
    /// e curva exotica, e defeito, e por isso ele serve de juiz.
    /// </summary>
    [Fact]
    public void AContagemCaiNoIntervaloDeHasse()
    {
        var curva = CurvaTest.Pequena();
        var porVarredura = Ordem.Contar(curva);
        var pelaOutraLista = CurvaTest.TodosOsPontos(curva).Count;
        Assert.Equal(pelaOutraLista, (int)porVarredura);
        Assert.True(Ordem.DentroDeHasse(porVarredura, curva.P),
            $"{porVarredura} pontos sobre F({curva.P}) esta fora de Hasse");
    }

    /// <summary>
    /// O teorema de Lagrange: a ordem de todo ponto DIVIDE a ordem do grupo. Em
    /// todos os pontos, nao por amostra.
    /// </summary>
    [Fact]
    public void AOrdemDeCadaPontoDivideAOrdemDoGrupo()
    {
        var curva = CurvaTest.Pequena();
        var total = Ordem.Contar(curva);
        foreach (var p in CurvaTest.TodosOsPontos(curva))
        {
            var ordem = Ordem.OrdemDoPonto(curva, p);
            Assert.True(BigInteger.Remainder(total, ordem).IsZero,
                $"a ordem {ordem} do ponto {p} nao divide {total}");
            Assert.True(Escalar.Escada(curva, p, ordem).Infinito);
        }
    }

    /// <summary>
    /// O passo de bebe e passo de gigante contra a forca bruta: ele acha o mesmo
    /// k, em muito menos passos.
    /// </summary>
    [Fact]
    public void OLogaritmoDiscretoAchaOExpoente()
    {
        var curva = CurvaTest.Pequena();
        var g = curva.Geradora;
        var ordem = Ordem.OrdemDoPonto(curva, g);

        for (var k = BigInteger.Zero; k < ordem; k++)
        {
            var q = Escalar.Escada(curva, g, k);
            var achado = Ordem.LogaritmoDiscreto(curva, g, q, ordem);
            Assert.NotNull(achado);
            Assert.Equal(q, Escalar.Escada(curva, g, achado!.Value));
        }
    }

    /// <summary>
    /// O custo MEDIDO: a raiz quadrada da ordem, e nao a ordem.
    ///
    /// E essa a conta que dimensiona chave. Numa ordem de 2^256, raiz da 2^128,
    /// que ninguem faz; numa de 2^40, da 2^20, que cabe num piscar. A seguranca
    /// de 128 bits da secp256k1 e literalmente a METADE do tamanho dela, e quem
    /// acha que 256 bits de chave dao 256 bits de seguranca esta errado por um
    /// fator que nao cabe em palavra nenhuma.
    /// </summary>
    [Fact]
    public void OCustoEhARaizDaOrdemENaoAOrdem()
    {
        var curva = CurvaTest.Pequena();
        var g = curva.Geradora;
        var ordem = Ordem.OrdemDoPonto(curva, g);

        Ordem.LogaritmoDiscreto(curva, g, Escalar.Escada(curva, g, ordem - 1), ordem, out var passos);
        var raiz = (long)Ordem.Raiz(ordem);
        // No maximo uns tres por raiz da ordem, contra a ordem inteira da forca
        // bruta.
        Assert.True(passos <= 3 * (raiz + 1), $"{passos} passos para ordem {ordem}, raiz {raiz}");
        Assert.True(passos < (long)ordem, "nao economizou nada em relacao a forca bruta");
    }

    /// <summary>
    /// A raiz quadrada inteira, contra a definicao: r² &lt;= n &lt; (r+1)².
    /// </summary>
    [Fact]
    public void ARaizInteiraEhExata()
    {
        for (var n = 0; n < 2000; n++)
        {
            var r = Ordem.Raiz(n);
            Assert.True(r * r <= n && (r + 1) * (r + 1) > n, $"raiz de {n} deu {r}");
        }
        // E em numero grande tambem: a raiz de um quadrado perfeito e exata.
        var grande = BigInteger.Pow(10, 40) + 12345;
        Assert.Equal(grande, Ordem.Raiz(grande * grande));
        Assert.Equal(grande - 1, Ordem.Raiz(grande * grande - 1));
    }

    /// <summary>
    /// A ordem do grupo da secp256k1 e PRIMA, e o cofator e 1.
    ///
    /// Isso tem consequencia pratica: todo ponto da curva, menos o infinito, gera
    /// o grupo inteiro. Nao existe subgrupo pequeno para onde empurrar a conta, e
    /// por isso a classe de ataque de subgrupo pequeno simplesmente nao se aplica
    /// a ela. Em curva de cofator 8, aplica.
    /// </summary>
    [Fact]
    public void AOrdemDaSecp256k1EhPrima()
    {
        Assert.True(EhPrimo(Secp256k1.N), "n nao passou no teste de primalidade");
        Assert.Equal(BigInteger.One, Secp256k1.Curva.Cofator);
        // E ela e menor que p, o que e o caso tipico.
        Assert.True(Secp256k1.N < Secp256k1.P);
        // E a diferenca entre as duas cabe em Hasse.
        Assert.True(Ordem.DentroDeHasse(Secp256k1.N, Secp256k1.P));
    }

    /// <summary>Miller-Rabin com as bases que decidem ate 3.3e24, mais algumas.</summary>
    private static bool EhPrimo(BigInteger n)
    {
        if (n < 2) return false;
        foreach (var p in new BigInteger[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
        {
            if (n == p) return true;
            if (BigInteger.Remainder(n, p).IsZero) return false;
        }

        var d = n - 1;
        var r = 0;
        while (d.IsEven) { d >>= 1; r++; }

        foreach (var a in new BigInteger[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 })
        {
            var x = BigInteger.ModPow(a, d, n);
            if (x.IsOne || x == n - 1) continue;
            var passou = false;
            for (var i = 1; i < r; i++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1) { passou = true; break; }
            }
            if (!passou) return false;
        }
        return true;
    }
}
