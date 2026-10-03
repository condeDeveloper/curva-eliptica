using System.Numerics;
using System.Security.Cryptography;

namespace Conde.CurvaEliptica;

/// <summary>
/// Diffie-Hellman na curva: duas pessoas chegam ao mesmo segredo sem nunca
/// mandar o segredo.
///
/// Cada uma tem um d e manda d*G. Quem tem dA calcula dA*(dB*G), quem tem dB
/// calcula dB*(dA*G), e os dois dao dA*dB*G porque multiplicacao de escalar
/// comuta. Quem esta no meio ve dA*G e dB*G e precisaria do logaritmo discreto
/// para tirar dai qualquer coisa.
///
/// A parte que da errado na pratica nao e essa. E aceitar a chave publica da
/// outra ponta SEM CONFERIR, e e isso que a <see cref="Conferir"/> trata.
/// </summary>
public static class Dh
{
    /// <summary>A parte publica: Q = d*G, pela escada, que nao vaza o d no tempo.</summary>
    public static Ponto Publica(Curva curva, BigInteger privada)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (privada <= 0 || privada >= curva.Ordem)
            throw new ArgumentOutOfRangeException(nameof(privada), "a chave privada fica em [1, n-1]");
        return Escalar.Escada(curva, curva.Geradora, privada);
    }

    /// <summary>
    /// O ponto compartilhado, depois de conferir a chave que veio de fora.
    /// </summary>
    public static Ponto Combinar(Curva curva, BigInteger minhaPrivada, Ponto dela)
    {
        ArgumentNullException.ThrowIfNull(curva);
        Conferir(curva, dela);
        var ponto = Escalar.Escada(curva, dela, minhaPrivada);
        if (ponto.Infinito) throw new InvalidOperationException("o segredo deu o infinito");
        return ponto;
    }

    /// <summary>
    /// O segredo em bytes: o x do ponto compartilhado, passado por SHA-256.
    ///
    /// Usar o x cru como chave seria errado por um motivo chato: as coordenadas
    /// de um ponto da curva NAO sao uniformes, e os bits de cima carregam vies.
    /// Toda especificacao seria manda derivar com uma funcao de derivacao em cima
    /// do x; aqui o SHA-256 faz esse papel.
    /// </summary>
    public static byte[] Segredo(Curva curva, BigInteger minhaPrivada, Ponto dela)
    {
        ArgumentNullException.ThrowIfNull(curva);
        var ponto = Combinar(curva, minhaPrivada, dela);
        var tamanho = (int)((curva.P.GetBitLength() + 7) / 8);
        return SHA256.HashData(Rfc6979.ParaBytes(ponto.X, tamanho));
    }

    /// <summary>
    /// As tres conferencias que a chave publica da outra ponta precisa passar.
    ///
    /// 1. Nao pode ser o infinito.
    /// 2. Precisa ESTAR NA CURVA. Essa e a que mata: um ponto fora da curva
    ///    continua funcionando nas formulas de soma, porque elas nunca olham o b,
    ///    e ele cai numa curva diferente, que pode ter ordem cheia de fatores
    ///    pequenos. Mandando varios desses e vendo se o segredo deu certo,
    ///    descobre-se a chave privada modulo cada fator, e o teorema chines
    ///    remonta ela. E o ataque de curva invalida, e ele derrubou produto de
    ///    verdade, inclusive implementacoes de TLS.
    /// 3. Precisa estar no SUBGRUPO de ordem n. Com cofator 1 isso e automatico;
    ///    em curva de cofator maior, nao, e e dai que vem o ataque de subgrupo
    ///    pequeno.
    /// </summary>
    public static void Conferir(Curva curva, Ponto publica)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (publica.Infinito) throw new ArgumentException("a chave publica e o infinito", nameof(publica));
        if (publica.X < 0 || publica.X >= curva.P || publica.Y < 0 || publica.Y >= curva.P)
            throw new ArgumentException("coordenada fora do corpo", nameof(publica));
        if (!curva.Contem(publica))
            throw new ArgumentException("a chave publica nao esta na curva", nameof(publica));
        if (!Escalar.Escada(curva, publica, curva.Ordem).Infinito)
            throw new ArgumentException("a chave publica nao esta no subgrupo de ordem n", nameof(publica));
    }

    /// <summary>
    /// O que acontece quando NAO se confere: o segredo calculado com um ponto de
    /// outra curva.
    ///
    /// Existe para o teste poder exibir o ataque em vez de so avisar dele. Nao
    /// usar para nada.
    /// </summary>
    public static Ponto CombinarSemConferir(Curva curva, BigInteger minhaPrivada, Ponto dela)
    {
        ArgumentNullException.ThrowIfNull(curva);
        return Escalar.Escada(curva, dela, minhaPrivada);
    }
}
