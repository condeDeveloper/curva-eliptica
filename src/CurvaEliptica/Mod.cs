using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>
/// Aritmetica modulo um primo, em inteiro grande.
///
/// Tudo na curva acontece num corpo finito, e e esse corpo que faz a criptografia
/// funcionar: nao ha "perto", nao ha aproximacao, e a unica operacao dificil de
/// desfazer e o logaritmo discreto.
///
/// O inverso sai do algoritmo de Euclides estendido e NAO da exponenciacao por
/// Fermat. Os dois funcionam com modulo primo, e o de Euclides custa O(log p)
/// operacoes contra O(log p) MULTIPLICACOES de numero grande, o que na pratica e
/// umas dez vezes mais rapido. Numa curva de 256 bits isso aparece.
/// </summary>
public static class Mod
{
    /// <summary>O resto sempre em [0, p).</summary>
    public static BigInteger Normalizar(BigInteger x, BigInteger p)
    {
        var r = x % p;
        return r.Sign < 0 ? r + p : r;
    }

    public static BigInteger Somar(BigInteger a, BigInteger b, BigInteger p)
    {
        var s = a + b;
        return s >= p ? s - p : s;
    }

    public static BigInteger Subtrair(BigInteger a, BigInteger b, BigInteger p)
    {
        var s = a - b;
        return s.Sign < 0 ? s + p : s;
    }

    public static BigInteger Multiplicar(BigInteger a, BigInteger b, BigInteger p) => a * b % p;

    public static BigInteger Potencia(BigInteger b, BigInteger e, BigInteger p) =>
        BigInteger.ModPow(Normalizar(b, p), e, p);

    /// <summary>
    /// O inverso multiplicativo por Euclides estendido.
    ///
    /// A identidade de Bezout da, para mdc(a,p) = 1, dois numeros x e y com
    /// a*x + p*y = 1. Olhando modulo p, isso e a*x = 1, entao x e o inverso.
    /// </summary>
    public static BigInteger Inverso(BigInteger a, BigInteger p)
    {
        a = Normalizar(a, p);
        if (a.IsZero) throw new DivideByZeroException("zero nao tem inverso");

        BigInteger velhoR = a, r = p;
        BigInteger velhoS = BigInteger.One, s = BigInteger.Zero;
        while (!r.IsZero)
        {
            var q = velhoR / r;
            (velhoR, r) = (r, velhoR - q * r);
            (velhoS, s) = (s, velhoS - q * s);
        }
        if (velhoR != BigInteger.One) throw new ArgumentException($"{a} nao e invertivel modulo {p}", nameof(a));
        return Normalizar(velhoS, p);
    }

    public static BigInteger Dividir(BigInteger a, BigInteger b, BigInteger p) =>
        Multiplicar(Normalizar(a, p), Inverso(b, p), p);

    /// <summary>
    /// A raiz quadrada modular, pelo atalho de Shanks que vale quando p = 3 mod 4:
    /// a raiz e a^((p+1)/4).
    ///
    /// O primo da secp256k1 satisfaz isso, e e por isso que descomprimir um ponto
    /// e barato ali. Para p = 1 mod 4 seria preciso Tonelli-Shanks, que e bem
    /// mais longo; devolver nulo e melhor que fingir que da.
    /// </summary>
    public static BigInteger? RaizQuadrada(BigInteger a, BigInteger p)
    {
        a = Normalizar(a, p);
        if (a.IsZero) return BigInteger.Zero;
        if (p % 4 != 3) throw new NotSupportedException("esta raiz so vale com p = 3 mod 4");
        var r = Potencia(a, (p + 1) / 4, p);
        // A formula sempre devolve algo; so e raiz se o quadrado bater.
        return Multiplicar(r, r, p) == a ? r : null;
    }

    /// <summary>Verdadeiro quando a e um quadrado modulo p, pelo criterio de Euler.</summary>
    public static bool EhQuadrado(BigInteger a, BigInteger p)
    {
        a = Normalizar(a, p);
        if (a.IsZero) return true;
        return Potencia(a, (p - 1) / 2, p).IsOne;
    }
}
