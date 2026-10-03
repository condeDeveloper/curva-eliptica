using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>
/// Quantos pontos a curva tem, e qual a ordem de cada ponto.
///
/// Esse numero e a seguranca inteira. Se a ordem do subgrupo onde a chave vive
/// for pequena, ou tiver um fator pequeno, o logaritmo discreto sai por
/// Pohlig-Hellman em pedacos do tamanho dos fatores, e a chave cai sem que nada
/// na curva pareca errado.
///
/// Os metodos daqui sao de FORCA BRUTA e so servem em curva pequena. E
/// exatamente por isso que servem: eles contam de verdade, sem depender da
/// implementacao que esta em teste.
/// </summary>
public static class Ordem
{
    /// <summary>
    /// Todos os pontos da curva, varrendo o corpo inteiro. Custa O(p), entao so
    /// vale em brinquedo.
    /// </summary>
    public static List<Ponto> TodosOsPontos(Curva curva)
    {
        ArgumentNullException.ThrowIfNull(curva);
        var saida = new List<Ponto> { Ponto.NoInfinito };
        for (var x = BigInteger.Zero; x < curva.P; x++)
        {
            var direita = Mod.Somar(Mod.Somar(Mod.Potencia(x, 3, curva.P),
                                              Mod.Multiplicar(curva.A, x, curva.P), curva.P), curva.B, curva.P);
            if (direita.IsZero) { saida.Add(new Ponto(x, BigInteger.Zero)); continue; }
            if (!Mod.EhQuadrado(direita, curva.P)) continue;
            var y = Mod.RaizQuadrada(direita, curva.P)!.Value;
            saida.Add(new Ponto(x, y));
            saida.Add(new Ponto(x, Mod.Subtrair(BigInteger.Zero, y, curva.P)));
        }
        return saida;
    }

    /// <summary>Quantos pontos a curva tem, contando o infinito.</summary>
    public static BigInteger Contar(Curva curva) => TodosOsPontos(curva).Count;

    /// <summary>
    /// A ordem de um ponto: o menor k com k*P = infinito, somando um a um.
    /// </summary>
    public static BigInteger OrdemDoPonto(Curva curva, Ponto p)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (p.Infinito) return BigInteger.One;
        var atual = p;
        for (var k = BigInteger.One; ; k++)
        {
            if (atual.Infinito) return k;
            atual = curva.Somar(atual, p);
            if (k > curva.P * 2 + 2) throw new InvalidOperationException("a ordem passou do limite de Hasse");
        }
    }

    /// <summary>
    /// Verdadeiro quando a contagem cai no intervalo de Hasse:
    /// |#E - (p + 1)| &lt;= 2*raiz(p).
    ///
    /// E um teorema, nao uma heuristica. Uma contagem fora desse intervalo nao e
    /// uma curva estranha: e um defeito na contagem ou na aritmetica, e por isso
    /// ele serve de juiz.
    /// </summary>
    public static bool DentroDeHasse(BigInteger quantos, BigInteger p)
    {
        var desvio = BigInteger.Abs(quantos - (p + 1));
        return desvio * desvio <= 4 * p;
    }

    /// <summary>
    /// Passo de bebe e passo de gigante: o logaritmo discreto em raiz de n
    /// operacoes, em vez de n.
    ///
    /// Escreve-se k = i*m + j com m = teto(raiz(n)). Guarda-se j*P numa tabela
    /// para j &lt; m (os passos de bebe) e anda-se de m em m a partir de Q (os
    /// passos de gigante) ate bater com a tabela.
    ///
    /// Isso e o que dimensiona a chave. Numa curva de 256 bits sao 2^128 passos,
    /// que ninguem faz; numa de 64 bits sao 2^32, que um notebook faz na hora. A
    /// seguranca de 128 bits da secp256k1 e literalmente a raiz quadrada da ordem
    /// dela, e nao o tamanho dela.
    /// </summary>
    public static BigInteger? LogaritmoDiscreto(Curva curva, Ponto g, Ponto q, BigInteger ordem, out long passos)
    {
        ArgumentNullException.ThrowIfNull(curva);
        passos = 0;
        if (q.Infinito) return BigInteger.Zero;

        var m = Raiz(ordem) + 1;
        if (m > 1 << 22) throw new ArgumentOutOfRangeException(nameof(ordem), "ordem grande demais para a tabela");

        var tabela = new Dictionary<Ponto, BigInteger>();
        var atual = Ponto.NoInfinito;
        for (var j = BigInteger.Zero; j < m; j++)
        {
            tabela.TryAdd(atual, j);
            atual = curva.Somar(atual, g);
            passos++;
        }

        // O passo de gigante anda -m*G de cada vez.
        var salto = curva.Negar(Escalar.Escada(curva, g, m));
        var gamma = q;
        for (var i = BigInteger.Zero; i < m; i++)
        {
            if (tabela.TryGetValue(gamma, out var j)) return Mod.Normalizar(i * m + j, ordem);
            gamma = curva.Somar(gamma, salto);
            passos++;
        }
        return null;
    }

    public static BigInteger? LogaritmoDiscreto(Curva curva, Ponto g, Ponto q, BigInteger ordem) =>
        LogaritmoDiscreto(curva, g, q, ordem, out _);

    /// <summary>A raiz quadrada inteira, por Newton em inteiro grande.</summary>
    public static BigInteger Raiz(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n < 2) return n;
        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x) return x;
            x = y;
        }
    }
}
