using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>
/// Multiplicacao escalar: k*G. E a operacao que tudo usa, e e nela que os
/// ataques de canal lateral moram.
///
/// Os tres metodos daqui dao a MESMA resposta e tem perfis de execucao bem
/// diferentes, e esse e o assunto:
///
/// - **dobrar e somar** e o obvio e o pior: ele soma quando o bit e 1 e nao soma
///   quando e 0, entao o tempo e o consumo denunciam a chave bit a bit;
/// - **escada de Montgomery** faz UMA soma e UM dobro por bit, sempre, qualquer
///   que seja o bit. O trabalho nao depende do segredo;
/// - **wNAF** e o rapido, para quando o escalar nao e segredo (verificar
///   assinatura, por exemplo).
///
/// A forca bruta, somando G a si mesmo k vezes, esta aqui de juiz.
/// </summary>
public static class Escalar
{
    /// <summary>
    /// Dobrar e somar, da esquerda para a direita. O jeito obvio.
    ///
    /// Custa log(k) dobros e, em media, log(k)/2 somas. E o numero de somas
    /// depende de quantos bits 1 o escalar tem, o que e exatamente a informacao
    /// que nao deveria vazar.
    /// </summary>
    public static Ponto DobrarESomar(Curva curva, Ponto g, BigInteger k, out long somas, out long dobros)
    {
        ArgumentNullException.ThrowIfNull(curva);
        somas = 0; dobros = 0;
        if (k.Sign < 0) { g = curva.Negar(g); k = -k; }
        var r = Ponto.NoInfinito;
        for (var bit = (int)k.GetBitLength() - 1; bit >= 0; bit--)
        {
            r = curva.Dobrar(r); dobros++;
            if (!((k >> bit) & BigInteger.One).IsZero) { r = curva.Somar(r, g); somas++; }
        }
        return r;
    }

    public static Ponto DobrarESomar(Curva curva, Ponto g, BigInteger k) =>
        DobrarESomar(curva, g, k, out _, out _);

    /// <summary>
    /// A escada de Montgomery: a mesma sequencia de operacoes para qualquer
    /// escalar.
    ///
    /// A cada bit mantem-se o par (r0, r1) com r1 = r0 + G, e o bit so decide
    /// QUAL dos dois recebe o dobro. Sao sempre uma soma e um dobro por bit,
    /// entao o tempo nao depende do segredo.
    ///
    /// Isso nao e paranoia academica. Medir o tempo de uma assinatura, ou o
    /// consumo de corrente de um cartao, ja recuperou chave de verdade mais de uma
    /// vez. A defesa nao e "fazer mais rapido": e fazer SEMPRE A MESMA COISA.
    /// </summary>
    public static Ponto Escada(Curva curva, Ponto g, BigInteger k, out long somas, out long dobros)
    {
        ArgumentNullException.ThrowIfNull(curva);
        somas = 0; dobros = 0;
        if (k.Sign < 0) { g = curva.Negar(g); k = -k; }
        if (k.IsZero) return Ponto.NoInfinito;

        var r0 = Ponto.NoInfinito;
        var r1 = g;
        for (var bit = (int)k.GetBitLength() - 1; bit >= 0; bit--)
        {
            if (((k >> bit) & BigInteger.One).IsZero)
            {
                r1 = curva.Somar(r0, r1); somas++;
                r0 = curva.Dobrar(r0); dobros++;
            }
            else
            {
                r0 = curva.Somar(r0, r1); somas++;
                r1 = curva.Dobrar(r1); dobros++;
            }
        }
        return r0;
    }

    public static Ponto Escada(Curva curva, Ponto g, BigInteger k) => Escada(curva, g, k, out _, out _);

    /// <summary>
    /// Forma nao adjacente com janela (wNAF): o escalar vira digitos impares com
    /// sinal, e nunca dois digitos nao nulos seguidos.
    ///
    /// O ganho vem de duas coisas. Primeiro, usar digitos NEGATIVOS: subtrair um
    /// ponto custa o mesmo que somar, porque negar e so trocar o sinal do y, e
    /// isso derruba o numero de digitos nao nulos de metade para um terco. Segundo,
    /// a janela: pre-computando G, 3G, 5G..., cada digito custa uma soma so.
    ///
    /// Medido em escalares de 256 bits: 128 somas no metodo obvio, 85 com janela
    /// 2, 55 com janela 4 e 50 com janela 5.
    ///
    /// A janela 6 piora: 52. E a medida que mostra onde esta o joelho da curva.
    /// A tabela pre-computada custa 2^(w-2) - 1 somas, que dobram a cada janela a
    /// mais, enquanto o laco so economiza a diferenca entre 256/(w+1) e
    /// 256/(w+2), que encolhe. Na janela 6 a tabela ja custa 15 somas para
    /// economizar tres. Maior nem sempre e melhor, e o ponto de virada aqui e 5.
    /// </summary>
    public static Ponto Wnaf(Curva curva, Ponto g, BigInteger k, int janela, out long somas, out long dobros)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (janela is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(janela));
        somas = 0; dobros = 0;
        if (k.Sign < 0) { g = curva.Negar(g); k = -k; }
        if (k.IsZero) return Ponto.NoInfinito;

        var digitos = FormaNaoAdjacente(k, janela);

        // Pre-computa G, 3G, 5G, ... ate 2^(janela-1) - 1 vezes G.
        var quantos = 1 << (janela - 2);
        var tabela = new Ponto[quantos];
        tabela[0] = g;
        var doisG = curva.Dobrar(g); dobros++;
        for (var i = 1; i < quantos; i++) { tabela[i] = curva.Somar(tabela[i - 1], doisG); somas++; }

        var r = Ponto.NoInfinito;
        for (var i = digitos.Count - 1; i >= 0; i--)
        {
            r = curva.Dobrar(r); dobros++;
            var d = digitos[i];
            if (d == 0) continue;
            var ponto = tabela[(Math.Abs(d) - 1) / 2];
            r = d > 0 ? curva.Somar(r, ponto) : curva.Subtrair(r, ponto);
            somas++;
        }
        return r;
    }

    public static Ponto Wnaf(Curva curva, Ponto g, BigInteger k, int janela = 4) =>
        Wnaf(curva, g, k, janela, out _, out _);

    /// <summary>
    /// Os digitos da forma nao adjacente com janela. Cada digito nao nulo e IMPAR
    /// e cabe em [-(2^(w-1) - 1), 2^(w-1) - 1], e entre dois nao nulos ha ao menos
    /// w-1 zeros.
    /// </summary>
    public static List<int> FormaNaoAdjacente(BigInteger k, int janela)
    {
        if (k.Sign < 0) throw new ArgumentOutOfRangeException(nameof(k));
        var digitos = new List<int>();
        var limite = BigInteger.One << janela;
        var metade = BigInteger.One << (janela - 1);
        while (k.Sign > 0)
        {
            if (k.IsEven) digitos.Add(0);
            else
            {
                var d = k % limite;
                // O digito fica no intervalo centrado em zero: assim ele e pequeno,
                // a tabela pre-computada e metade do tamanho, e subtrair custa o
                // mesmo que somar porque negar um ponto e so trocar o sinal do y.
                if (d >= metade) d -= limite;
                digitos.Add((int)d);
                k -= d;
            }
            k >>= 1;
        }
        return digitos;
    }

    /// <summary>
    /// O juiz: soma G a si mesmo k vezes, sem nenhum atalho. So serve para k
    /// pequeno, e e justamente por nao ter atalho que ele serve.
    /// </summary>
    public static Ponto ForcaBruta(Curva curva, Ponto g, int k)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (k < 0) { g = curva.Negar(g); k = -k; }
        var r = Ponto.NoInfinito;
        for (var i = 0; i < k; i++) r = curva.Somar(r, g);
        return r;
    }
}
