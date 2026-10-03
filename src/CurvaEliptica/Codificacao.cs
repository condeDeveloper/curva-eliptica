using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>
/// Como um ponto e uma assinatura viram bytes.
///
/// Parece burocracia e nao e: quase todo defeito explorado em carteira e em
/// biblioteca de assinatura nos ultimos anos estava no PARSER, nao na
/// matematica. Aceitar um numero com zero sobrando na frente, aceitar um
/// comprimento que nao bate, aceitar um ponto que nao esta na curva: cada uma
/// dessas e uma falha publicada.
///
/// Por isso a decodificacao aqui e estrita. Ela recusa o que nao esta na forma
/// canonica, mesmo quando daria para adivinhar o que a outra ponta queria dizer.
/// </summary>
public static class Codificacao
{
    /// <summary>
    /// O ponto no formato SEC1: 0x04 seguido de x e y, ou 0x02/0x03 seguido so
    /// do x, com o byte dizendo a paridade do y.
    ///
    /// A forma comprimida custa metade do tamanho e um exponencial modular para
    /// desfazer. Em blockchain, onde cada byte e pago por todo mundo para sempre,
    /// essa troca e obvia.
    /// </summary>
    public static byte[] EscreverPonto(Curva curva, Ponto ponto, bool comprimido)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (ponto.Infinito) return [0x00];
        var tamanho = (int)((curva.P.GetBitLength() + 7) / 8);
        var x = Rfc6979.ParaBytes(ponto.X, tamanho);
        if (!comprimido) return [0x04, .. x, .. Rfc6979.ParaBytes(ponto.Y, tamanho)];
        return [(byte)(ponto.Y.IsEven ? 0x02 : 0x03), .. x];
    }

    /// <summary>
    /// O ponto de volta, recusando tudo que nao esta na forma exata.
    ///
    /// A ultima conferencia e a que mais importa: o ponto precisa ESTAR NA CURVA.
    /// Sem ela, uma chave publica forjada fora da curva leva a aritmetica para um
    /// grupo de ordem pequena, e o segredo compartilhado vaza em poucas trocas.
    /// E o ataque de curva invalida, e ele ja funcionou contra produto de
    /// verdade.
    /// </summary>
    public static Ponto LerPonto(Curva curva, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(curva);
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 1 && bytes[0] == 0x00) return CurvaEliptica.Ponto.NoInfinito;
        var tamanho = (int)((curva.P.GetBitLength() + 7) / 8);

        Ponto ponto;
        switch (bytes[0])
        {
            case 0x04 when bytes.Length == 1 + 2 * tamanho:
                ponto = new Ponto(Ler(bytes, 1, tamanho), Ler(bytes, 1 + tamanho, tamanho));
                break;
            case 0x02 or 0x03 when bytes.Length == 1 + tamanho:
                var x = Ler(bytes, 1, tamanho);
                if (x >= curva.P) throw new FormatException("o x nao cabe no corpo");
                var achado = curva.Descomprimir(x, bytes[0] == 0x03)
                    ?? throw new FormatException("nao existe ponto com esse x");
                ponto = achado;
                break;
            default:
                throw new FormatException($"prefixo 0x{bytes[0]:x2} com {bytes.Length} bytes nao e ponto");
        }

        if (ponto.X >= curva.P || ponto.Y >= curva.P) throw new FormatException("coordenada fora do corpo");
        if (!curva.Contem(ponto)) throw new FormatException("o ponto nao esta na curva");
        return ponto;
    }

    private static BigInteger Ler(byte[] bytes, int inicio, int quantos) =>
        new(bytes.AsSpan(inicio, quantos), isUnsigned: true, isBigEndian: true);

    /// <summary>
    /// A assinatura em DER: SEQUENCE { INTEGER r, INTEGER s }.
    ///
    /// O INTEGER do DER e COM SINAL, entao um numero cujo primeiro byte passa de
    /// 0x7f precisa de um 0x00 na frente. Esquecer isso gera uma assinatura que
    /// metade do mundo aceita e a outra metade recusa, e o bug so aparece em
    /// algumas chaves.
    /// </summary>
    public static byte[] EscreverAssinatura(Assinatura assinatura)
    {
        var r = Inteiro(assinatura.R);
        var s = Inteiro(assinatura.S);
        var corpo = new List<byte>(r.Length + s.Length);
        corpo.AddRange(r);
        corpo.AddRange(s);
        if (corpo.Count > 127) throw new InvalidOperationException("assinatura longa demais para esta forma");
        return [0x30, (byte)corpo.Count, .. corpo];
    }

    private static byte[] Inteiro(BigInteger x)
    {
        if (x.Sign < 0) throw new ArgumentOutOfRangeException(nameof(x), "DER aqui so leva numero nao negativo");
        byte[] cru = x.IsZero ? [0x00] : x.ToByteArray(isUnsigned: true, isBigEndian: true);
        // Zeros a esquerda sao proibidos no DER, menos o que e preciso para o
        // numero nao parecer negativo.
        var i = 0;
        while (i + 1 < cru.Length && cru[i] == 0x00 && cru[i + 1] < 0x80) i++;
        var corte = cru[i..];
        byte[] conteudo = corte[0] >= 0x80 ? [0x00, .. corte] : corte;
        return [0x02, (byte)conteudo.Length, .. conteudo];
    }

    /// <summary>
    /// A assinatura de volta, em leitura estrita.
    ///
    /// Aceitar DER frouxo foi o que deu a maleabilidade de transacao do Bitcoin:
    /// a mesma assinatura reescrita de outro jeito continuava valida e mudava o
    /// identificador da transacao. Levou anos e uma mudanca de consenso para
    /// fechar.
    /// </summary>
    public static Assinatura LerAssinatura(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var i = 0;
        if (bytes.Length < 8 || bytes[i++] != 0x30) throw new FormatException("nao comeca com SEQUENCE");
        int total = bytes[i++];
        if (total != bytes.Length - 2) throw new FormatException("o comprimento declarado nao bate com o buffer");
        var r = LerInteiro(bytes, ref i);
        var s = LerInteiro(bytes, ref i);
        if (i != bytes.Length) throw new FormatException("sobrou byte depois do s");
        return new Assinatura(r, s);
    }

    private static BigInteger LerInteiro(byte[] bytes, ref int i)
    {
        if (i + 2 > bytes.Length || bytes[i++] != 0x02) throw new FormatException("esperava INTEGER");
        int tamanho = bytes[i++];
        if (tamanho == 0 || i + tamanho > bytes.Length) throw new FormatException("INTEGER com tamanho invalido");
        var corpo = bytes.AsSpan(i, tamanho);
        if ((corpo[0] & 0x80) != 0) throw new FormatException("INTEGER negativo");
        if (corpo[0] == 0x00 && tamanho > 1 && (corpo[1] & 0x80) == 0)
            throw new FormatException("zero sobrando a esquerda: nao e DER canonico");
        i += tamanho;
        return new BigInteger(corpo, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// A assinatura na forma de s baixo: se s passa de n/2, troca por n - s.
    ///
    /// As duas sao validas, porque (r, s) e (r, n - s) verificam igual: a conta
    /// chega ao mesmo x por caminhos refletidos. Isso e MALEABILIDADE, e quem
    /// identifica uma transacao pelo hash da assinatura descobre que o
    /// identificador pode ser mudado por qualquer um no meio do caminho sem a
    /// chave privada. A regra de so aceitar o s baixo, que o Bitcoin adotou na
    /// BIP 62, escolhe uma das duas e acaba com a ambiguidade.
    /// </summary>
    public static Assinatura SBaixo(Curva curva, Assinatura assinatura)
    {
        ArgumentNullException.ThrowIfNull(curva);
        var n = curva.Ordem;
        return assinatura.S > n / 2 ? new Assinatura(assinatura.R, n - assinatura.S) : assinatura;
    }
}
