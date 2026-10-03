using System.Numerics;
using System.Security.Cryptography;

namespace Conde.CurvaEliptica;

/// <summary>
/// O k deterministico da RFC 6979, de Thomas Pornin (2013).
///
/// A ideia e tirar o sorteio da jogada: em vez de um k aleatorio, deriva-se um k
/// da chave privada e da mensagem, por HMAC. O resultado e que a mesma mensagem
/// assinada com a mesma chave da SEMPRE a mesma assinatura — o que, de quebra,
/// torna a assinatura testavel contra vetores publicados.
///
/// O motivo e defensivo. Um k previsivel entrega a chave, e um k REPETIDO a
/// entrega com duas assinaturas e uma divisao. Gerar aleatoriedade boa e
/// surpreendentemente dificil: o PlayStation 3 usou k fixo, carteiras Android
/// tiveram k repetido por causa de um gerador mal semeado, e nos dois casos o
/// dinheiro foi embora.
///
/// Tirar o gerador do caminho resolve a classe inteira de problemas de uma vez.
/// </summary>
public static class Rfc6979
{
    /// <summary>
    /// O k, derivado da chave privada e do hash da mensagem, por HMAC-SHA256.
    ///
    /// O laco no fim nao e enfeite: se o candidato cair fora de [1, n-1], a RFC
    /// manda continuar a geracao em vez de reduzir modulo n. Reduzir introduziria
    /// VIES, e vies em k e exatamente o que as tecnicas de rede (lattice)
    /// exploram para recuperar a chave com algumas centenas de assinaturas.
    /// </summary>
    public static BigInteger GerarK(BigInteger ordem, BigInteger privada, byte[] hashDaMensagem)
    {
        ArgumentNullException.ThrowIfNull(hashDaMensagem);
        var tamanho = (int)((ordem.GetBitLength() + 7) / 8);

        var x = ParaBytes(privada, tamanho);
        var h1 = Bits2Octets(hashDaMensagem, ordem, tamanho);

        // V = 0x01 repetido, K = 0x00 repetido. Sao os valores da RFC.
        var v = new byte[32];
        var k = new byte[32];
        Array.Fill(v, (byte)0x01);

        k = Hmac(k, [.. v, (byte)0x00, .. x, .. h1]);
        v = Hmac(k, v);
        k = Hmac(k, [.. v, (byte)0x01, .. x, .. h1]);
        v = Hmac(k, v);

        while (true)
        {
            var t = new List<byte>();
            while (t.Count < tamanho)
            {
                v = Hmac(k, v);
                t.AddRange(v);
            }
            var candidato = Bits2Int([.. t], ordem);
            if (candidato > 0 && candidato < ordem) return candidato;
            // Fora da faixa: continua a geracao. Reduzir modulo n introduziria
            // vies, e vies em k e o que as tecnicas de rede exploram.
            k = Hmac(k, [.. v, (byte)0x00]);
            v = Hmac(k, v);
        }
    }

    private static byte[] Hmac(byte[] chave, byte[] dados) => HMACSHA256.HashData(chave, dados);

    /// <summary>
    /// Os bits mais significativos, tantos quantos a ordem tem. Nao e um resto:
    /// e um deslocamento, e confundir os dois muda o k e quebra os vetores.
    /// </summary>
    public static BigInteger Bits2Int(byte[] bytes, BigInteger ordem)
    {
        var valor = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var sobra = bytes.Length * 8 - (int)ordem.GetBitLength();
        return sobra > 0 ? valor >> sobra : valor;
    }

    public static byte[] Bits2Octets(byte[] bytes, BigInteger ordem, int tamanho)
    {
        var z1 = Bits2Int(bytes, ordem);
        var z2 = z1 >= ordem ? z1 - ordem : z1;
        return ParaBytes(z2, tamanho);
    }

    /// <summary>Big-endian, com zeros a esquerda ate o tamanho pedido.</summary>
    public static byte[] ParaBytes(BigInteger x, int tamanho)
    {
        var cru = x.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (cru.Length == tamanho) return cru;
        if (cru.Length > tamanho) return cru[^tamanho..];
        var saida = new byte[tamanho];
        Array.Copy(cru, 0, saida, tamanho - cru.Length, cru.Length);
        return saida;
    }
}
