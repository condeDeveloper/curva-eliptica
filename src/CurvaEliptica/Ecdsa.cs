using System.Numerics;
using System.Security.Cryptography;

namespace Conde.CurvaEliptica;

/// <summary>Uma assinatura ECDSA: o par (r, s).</summary>
public readonly record struct Assinatura(BigInteger R, BigInteger S)
{
    public override string ToString() => $"r={R:x}, s={S:x}";
}

/// <summary>
/// ECDSA: assinar e verificar.
///
/// A conta e curta e cada peça tem um motivo:
///
///   assinar:   k sorteado, R = k*G, r = Rx mod n, s = (h + r*d) / k mod n
///   verificar: u1 = h/s, u2 = r/s, R' = u1*G + u2*Q, confere r = R'x mod n
///
/// O k e o ponto fraco, e nao e opiniao: ele precisa ser IMPREVISIVEL e usado UMA
/// vez so. Reusar o mesmo k em duas assinaturas revela a chave privada por
/// algebra de colegial, e foi exatamente isso que aconteceu com o PlayStation 3 em
/// 2010, quando a Sony usou um k fixo. A funcao
/// <see cref="RecuperarChaveDeKRepetido"/> faz essa conta, para que o estrago
/// nao fique no campo da advertencia.
/// </summary>
public static class Ecdsa
{
    /// <summary>A chave publica de uma chave privada: Q = d*G.</summary>
    public static Ponto ChavePublica(Curva curva, BigInteger privada)
    {
        ArgumentNullException.ThrowIfNull(curva);
        ConferirPrivada(curva, privada);
        return Escalar.Escada(curva, curva.Geradora, privada);
    }

    private static void ConferirPrivada(Curva curva, BigInteger d)
    {
        if (d <= 0 || d >= curva.Ordem)
            throw new ArgumentOutOfRangeException(nameof(d), "a chave privada fica em [1, n-1]");
    }

    /// <summary>
    /// Assina com um k DADO. Existe para os vetores de teste e para a
    /// demonstracao do k repetido; em producao o k vem da RFC 6979.
    /// </summary>
    public static Assinatura AssinarComK(Curva curva, BigInteger privada, BigInteger hash, BigInteger k)
    {
        ArgumentNullException.ThrowIfNull(curva);
        ConferirPrivada(curva, privada);
        var n = curva.Ordem;
        k = Mod.Normalizar(k, n);
        if (k.IsZero) throw new ArgumentOutOfRangeException(nameof(k), "k zero nao assina nada");

        var ponto = Escalar.Escada(curva, curva.Geradora, k);
        if (ponto.Infinito) throw new InvalidOperationException("k*G deu o infinito");
        var r = Mod.Normalizar(ponto.X, n);
        if (r.IsZero) throw new InvalidOperationException("r zero; sorteie outro k");

        var e = Mod.Normalizar(hash, n);
        var s = Mod.Multiplicar(Mod.Inverso(k, n), Mod.Somar(e, Mod.Multiplicar(r, privada, n), n), n);
        if (s.IsZero) throw new InvalidOperationException("s zero; sorteie outro k");
        return new Assinatura(r, s);
    }

    /// <summary>Assina com o k deterministico da RFC 6979.</summary>
    public static Assinatura Assinar(Curva curva, BigInteger privada, byte[] mensagem)
    {
        ArgumentNullException.ThrowIfNull(curva);
        ArgumentNullException.ThrowIfNull(mensagem);
        var hash = new BigInteger(SHA256.HashData(mensagem), isUnsigned: true, isBigEndian: true);
        var k = Rfc6979.GerarK(curva.Ordem, privada, SHA256.HashData(mensagem));
        return AssinarComK(curva, privada, hash, k);
    }

    /// <summary>
    /// Verifica a assinatura.
    ///
    /// As conferencias de faixa no comeco nao sao formalidade: sem elas, um r ou
    /// um s fora de [1, n-1] pode fazer a verificacao aceitar coisa que nao
    /// deveria, e varias bibliotecas ja tiveram CVE exatamente por isso.
    /// </summary>
    public static bool Verificar(Curva curva, Ponto publica, BigInteger hash, Assinatura assinatura)
    {
        ArgumentNullException.ThrowIfNull(curva);
        var n = curva.Ordem;
        if (assinatura.R <= 0 || assinatura.R >= n) return false;
        if (assinatura.S <= 0 || assinatura.S >= n) return false;
        if (publica.Infinito || !curva.Contem(publica)) return false;
        // A chave publica precisa estar no subgrupo de ordem n. Com cofator 1
        // isso e automatico, mas conferir deixa o codigo correto em qualquer curva.
        if (!Escalar.Escada(curva, publica, n).Infinito) return false;

        var e = Mod.Normalizar(hash, n);
        var w = Mod.Inverso(assinatura.S, n);
        var u1 = Mod.Multiplicar(e, w, n);
        var u2 = Mod.Multiplicar(assinatura.R, w, n);
        var ponto = curva.Somar(Escalar.Wnaf(curva, curva.Geradora, u1),
                                Escalar.Wnaf(curva, publica, u2));
        if (ponto.Infinito) return false;
        return Mod.Normalizar(ponto.X, n) == assinatura.R;
    }

    public static bool Verificar(Curva curva, Ponto publica, byte[] mensagem, Assinatura assinatura)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        var hash = new BigInteger(SHA256.HashData(mensagem), isUnsigned: true, isBigEndian: true);
        return Verificar(curva, publica, hash, assinatura);
    }

    /// <summary>
    /// A chave privada, a partir de DUAS assinaturas que usaram o mesmo k.
    ///
    /// A algebra e de colegial. De s1 = (h1 + r*d)/k e s2 = (h2 + r*d)/k sai
    /// k = (h1 - h2)/(s1 - s2), e dai d = (s1*k - h1)/r. Nenhuma forca bruta,
    /// nenhuma tabela, nenhum supercomputador: duas assinaturas e uma divisao.
    ///
    /// Foi assim que a chave de assinatura do PlayStation 3 saiu em 2010, e e por
    /// isso que a RFC 6979 existe.
    /// </summary>
    public static BigInteger RecuperarChaveDeKRepetido(
        Curva curva, BigInteger hash1, Assinatura a1, BigInteger hash2, Assinatura a2)
    {
        ArgumentNullException.ThrowIfNull(curva);
        if (a1.R != a2.R) throw new ArgumentException("os r sao diferentes; nao houve k repetido", nameof(a2));
        if (a1.S == a2.S) throw new ArgumentException("as duas assinaturas sao iguais", nameof(a2));
        var n = curva.Ordem;
        var k = Mod.Dividir(Mod.Subtrair(Mod.Normalizar(hash1, n), Mod.Normalizar(hash2, n), n),
                            Mod.Subtrair(a1.S, a2.S, n), n);
        return Mod.Dividir(Mod.Subtrair(Mod.Multiplicar(a1.S, k, n), Mod.Normalizar(hash1, n), n), a1.R, n);
    }
}
