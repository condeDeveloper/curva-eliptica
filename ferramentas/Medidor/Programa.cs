using System.Diagnostics;
using System.Numerics;
using System.Text;
using Conde.CurvaEliptica;

namespace Conde.CurvaEliptica.Medidor;

/// <summary>
/// As medidas. Elas existem para trocar opiniao por numero.
///
/// Nada aqui afirma tempo em teste automatico, porque tempo depende da maquina.
/// O que se mede e CONTAGEM DE OPERACAO, que nao depende, e os tempos aparecem
/// so como referencia ao lado.
/// </summary>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        var qual = argumentos.Length > 0 ? argumentos[0] : "tudo";
        switch (qual)
        {
            case "escalar": Escalares(); break;
            case "canal": CanalLateral(); break;
            case "quebrar": Quebrar(); break;
            case "assinar": Assinar(); break;
            case "tudo": Escalares(); CanalLateral(); Quebrar(); Assinar(); break;
            default:
                Console.Error.WriteLine("medidas: escalar, canal, quebrar, assinar, tudo");
                return 1;
        }
        return 0;
    }

    /// <summary>
    /// Quantas somas e quantos dobros cada metodo faz, em escalares de 256 bits.
    ///
    /// A teoria diz: o metodo obvio faz log(k) dobros e metade disso em somas; o
    /// wNAF com janela w faz log(k)/(w+1) somas de laco mais 2^(w-2)-1 da tabela;
    /// a escada faz log(k) de cada, sempre. A medida confere os tres.
    ///
    /// A contagem e a medida que vale, e o tempo vai junto so como referencia.
    /// Esta separacao nao e preciosismo: a primeira versao desta ferramenta
    /// media uma vez cada metodo, em sequencia, e o tempo CRESCIA conforme a
    /// posicao na lista. A escada, medida em segundo lugar, apareceu com quase
    /// cinco vezes o tempo do metodo obvio, quando ela faz so um terco mais de
    /// operacoes. Medindo a escada PRIMEIRO, ela ficou mais rapida que o metodo
    /// obvio medido depois. O que o relogio estava mostrando era a maquina
    /// baixando a frequencia, nao o algoritmo.
    ///
    /// Por isso aqui sao varias rodadas intercaladas e o tempo relatado e o
    /// MENOR de todas. O menor tempo e o que mais se aproxima do custo real:
    /// ruido so faz um trecho demorar mais, nunca menos.
    /// </summary>
    private static void Escalares()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(1234);
        const int quantos = 50;
        const int rodadas = 7;

        var metodos = new List<(string Nome, Func<Curva, Ponto, BigInteger, (long, long)> F)>
        {
            ("dobrar e somar", (c, g, k) => { Escalar.DobrarESomar(c, g, k, out var s, out var d); return (s, d); }),
            ("escada de Montgomery", (c, g, k) => { Escalar.Escada(c, g, k, out var s, out var d); return (s, d); }),
        };
        for (var janela = 2; janela <= 6; janela++)
        {
            var w = janela;
            metodos.Add(($"wNAF janela {w}",
                (c, g, k) => { Escalar.Wnaf(c, g, k, w, out var s, out var d); return (s, d); }));
        }

        var somas = new double[metodos.Count];
        var dobros = new double[metodos.Count];
        var melhor = new double[metodos.Count];
        Array.Fill(melhor, double.MaxValue);

        // Aquecimento, para ninguem pagar a compilacao em tempo de execucao.
        foreach (var (_, f) in metodos)
            for (var i = 0; i < 10; i++) f(curva, curva.Geradora, Sortear(sorteio, 256) % curva.Ordem);

        // A ordem GIRA a cada rodada. Sem isso, quem mede por ultimo mede sempre
        // com a maquina ja quente, e o ultimo da lista parece lento por posicao.
        for (var rodada = 0; rodada < rodadas; rodada++)
            for (var passo = 0; passo < metodos.Count; passo++)
            {
                var m = (passo + rodada) % metodos.Count;
                long s = 0, d = 0;
                var relogio = Stopwatch.StartNew();
                for (var i = 0; i < quantos; i++)
                {
                    var (sa, da) = metodos[m].F(curva, curva.Geradora, Sortear(sorteio, 256) % curva.Ordem);
                    s += sa; d += da;
                }
                relogio.Stop();
                somas[m] = s / (double)quantos;
                dobros[m] = d / (double)quantos;
                melhor[m] = Math.Min(melhor[m], relogio.Elapsed.TotalMilliseconds / quantos);
            }

        Console.WriteLine($"== somas e dobros por multiplicacao, escalares de 256 bits, {rodadas} rodadas ==");
        Console.WriteLine($"{"metodo",-28}{"somas",10}{"dobros",10}{"total",10}{"menor tempo",14}");
        for (var m = 0; m < metodos.Count; m++)
            Console.WriteLine($"{metodos[m].Nome,-28}{somas[m],10:F1}{dobros[m],10:F1}" +
                              $"{somas[m] + dobros[m],10:F1}{melhor[m],11:F2} ms");
        Console.WriteLine();
        Console.WriteLine("leia a coluna TOTAL, nao a do tempo. Mesmo com rodadas giradas e com o menor");
        Console.WriteLine("tempo de cada uma, nesta maquina o relogio ainda sobe com a posicao na lista:");
        Console.WriteLine("ela baixa a frequencia conforme esquenta, e isso nao e propriedade do");
        Console.WriteLine("algoritmo. A contagem de operacoes da o mesmo numero em qualquer maquina.");
        Console.WriteLine();
    }

    /// <summary>
    /// O vazamento, exibido: o numero de somas do metodo obvio E a quantidade de
    /// bits 1 da chave.
    ///
    /// Quem mede o tempo da operacao, ou o consumo de corrente do chip, le esse
    /// numero. Nao e teoria: medir tempo e consumo ja recuperou chave de produto
    /// no mercado. A escada da sempre o mesmo numero, e e por isso que ela existe.
    /// </summary>
    private static void CanalLateral()
    {
        var curva = Secp256k1.Curva;
        Console.WriteLine("== o que o tempo conta sobre a chave ==");
        Console.WriteLine($"{"chave (160 bits)",-34}{"bits 1",8}{"somas obvio",14}{"somas escada",14}");

        foreach (var (nome, k) in new (string, BigInteger)[]
        {
            ("todos os bits em 1", (BigInteger.One << 160) - 1),
            ("um bit so", BigInteger.One << 159),
            ("metade de cada", ((BigInteger.One << 80) - 1) << 80),
            ("alternados", AlternarBits(160)),
        })
        {
            Escalar.DobrarESomar(curva, curva.Geradora, k, out var somasObvio, out _);
            Escalar.Escada(curva, curva.Geradora, k, out var somasEscada, out _);
            var uns = 0;
            for (var bit = 0; bit < k.GetBitLength(); bit++)
                if (!((k >> bit) & BigInteger.One).IsZero) uns++;
            Console.WriteLine($"{nome,-34}{uns,8}{somasObvio,14}{somasEscada,14}");
        }
        Console.WriteLine("o metodo obvio conta os bits 1 em voz alta; a escada diz sempre o mesmo numero");
        Console.WriteLine();
    }

    /// <summary>
    /// O custo de quebrar, medido numa curva de brinquedo e extrapolado.
    ///
    /// O passo de bebe e passo de gigante custa raiz da ordem. Medindo em ordens
    /// pequenas e vendo a curva, da para ver que o expoente e 1/2 e nao 1, e dai
    /// sai o numero que dimensiona chave de verdade.
    /// </summary>
    private static void Quebrar()
    {
        Console.WriteLine("== quanto custa achar o expoente (passo de bebe e passo de gigante) ==");
        Console.WriteLine($"{"primo",8}{"ordem",10}{"forca bruta",14}{"bebe/gigante",14}{"raiz",8}");

        foreach (var p in new[] { 103, 1019, 10007, 100003 })
        {
            var curva = Montar(p);
            if (curva is null) continue;
            var ordem = Ordem.OrdemDoPonto(curva, curva.Geradora);
            var alvo = Escalar.Escada(curva, curva.Geradora, ordem - 7);
            Ordem.LogaritmoDiscreto(curva, curva.Geradora, alvo, ordem, out var passos);
            Console.WriteLine($"{p,8}{ordem,10}{ordem,14}{passos,14}{Ordem.Raiz(ordem),8}");
        }

        Console.WriteLine();
        Console.WriteLine("extrapolando o mesmo expoente 1/2 para as curvas de verdade:");
        foreach (var (nome, bits) in new[] { ("secp256k1", 256), ("P-256", 256), ("uma curva de 160 bits", 160) })
            Console.WriteLine($"  {nome,-24} ordem ~2^{bits}, passos ~2^{bits / 2}");
        Console.WriteLine("  a chave de 256 bits da 128 bits de seguranca, nao 256: e a RAIZ da ordem");
        Console.WriteLine();
    }

    /// <summary>Quanto custa assinar e verificar, e onde o tempo vai.</summary>
    private static void Assinar()
    {
        var curva = Secp256k1.Curva;
        var privada = BigInteger.Parse("4242424242424242424242424242424242424242");
        var publica = Ecdsa.ChavePublica(curva, privada);
        const int quantas = 100;

        var mensagens = new byte[quantas][];
        for (var i = 0; i < quantas; i++) mensagens[i] = Encoding.UTF8.GetBytes($"mensagem {i}");

        var relogio = Stopwatch.StartNew();
        var assinaturas = new Assinatura[quantas];
        for (var i = 0; i < quantas; i++) assinaturas[i] = Ecdsa.Assinar(curva, privada, mensagens[i]);
        relogio.Stop();
        var assinar = relogio.Elapsed.TotalMilliseconds / quantas;

        relogio.Restart();
        for (var i = 0; i < quantas; i++)
            if (!Ecdsa.Verificar(curva, publica, mensagens[i], assinaturas[i]))
                throw new InvalidOperationException("uma assinatura minha nao verificou");
        relogio.Stop();
        var verificar = relogio.Elapsed.TotalMilliseconds / quantas;

        Console.WriteLine("== assinar e verificar na secp256k1 ==");
        Console.WriteLine($"assinar     {assinar,8:F2} ms   (uma multiplicacao escalar, pela escada)");
        Console.WriteLine($"verificar   {verificar,8:F2} ms   (duas multiplicacoes escalares, pelo wNAF)");
        Console.WriteLine($"razao       {verificar / assinar,8:F2}");
        Console.WriteLine("verificar custa duas multiplicacoes e assinar uma, mas verificar usa o wNAF,");
        Console.WriteLine("que e mais rapido porque ali o escalar nao e segredo e pode vazar no tempo");
        Console.WriteLine();

        // E o tamanho dos formatos, que e o que trafega.
        var qualquer = assinaturas[0];
        Console.WriteLine($"ponto sem comprimir  {Codificacao.EscreverPonto(curva, publica, false).Length,3} bytes");
        Console.WriteLine($"ponto comprimido     {Codificacao.EscreverPonto(curva, publica, true).Length,3} bytes");
        Console.WriteLine($"assinatura em DER    {Codificacao.EscreverAssinatura(qualquer).Length,3} bytes");
        Console.WriteLine();
    }

    private static BigInteger AlternarBits(int quantos)
    {
        var k = BigInteger.Zero;
        for (var i = 0; i < quantos; i += 2) k |= BigInteger.One << i;
        return k | (BigInteger.One << (quantos - 1));
    }

    /// <summary>
    /// Uma curva de brinquedo sobre F(p), com um gerador de ordem grande.
    /// Devolve nulo quando p nao serve (precisa ser 3 mod 4 para a raiz).
    /// </summary>
    private static Curva? Montar(int p)
    {
        if (p % 4 != 3) return null;
        var provisoria = new Curva(p, 2, 3, new Ponto(0, 0), p, 1);
        for (var x = BigInteger.One; x < p; x++)
            for (var y = BigInteger.One; y < p; y++)
            {
                var ponto = new Ponto(x, y);
                if (!provisoria.Contem(ponto)) continue;
                var curva = new Curva(p, 2, 3, ponto, p, 1);
                var ordem = Ordem.OrdemDoPonto(curva, ponto);
                // So serve se a ordem for grande. O primeiro ponto que a
                // varredura acha pode ter ordem 2, e ai nao ha o que medir: a
                // busca continua. Eu tinha escrito isso como "pega o primeiro ou
                // desiste", e as linhas de 1009 e 10007 sumiram da tabela.
                if (ordem > p / 2) return curva;
            }
        return null;
    }

    private static BigInteger Sortear(Random sorteio, int bits)
    {
        var bytes = new byte[bits / 8 + 1];
        sorteio.NextBytes(bytes);
        bytes[0] = 0;
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true) + 1;
    }
}
