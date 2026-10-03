using System.Globalization;
using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>
/// A curva secp256k1, que e a do Bitcoin e a do Ethereum.
///
/// Os parametros nao sao sorteados: eles sao escolhidos com estrutura para que a
/// aritmetica fique rapida. O primo e 2^256 - 2^32 - 977, que esta a menos de
/// 2^33 de uma potencia de dois e permite reducao sem divisao. O coeficiente a e
/// ZERO, o que apaga um termo inteiro das formulas de soma. E o cofator e 1, o
/// que significa que TODO ponto da curva, exceto o infinito, gera o grupo inteiro
/// — e por isso nao existe aqui o ataque de subgrupo pequeno que afeta curvas de
/// cofator maior.
///
/// A escolha de "k1" no nome e a parte interessante: a curva e de Koblitz, com um
/// endomorfismo eficiente. As curvas "r1" da mesma familia, como a P-256, tem
/// parametros derivados de uma semente nao explicada, e e dessa semente que vem a
/// desconfianca publica sobre elas.
/// </summary>
public static class Secp256k1
{
    private static BigInteger Hex(string h) =>
        BigInteger.Parse("0" + h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>2^256 - 2^32 - 977.</summary>
    public static readonly BigInteger P =
        Hex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F");

    public static readonly BigInteger A = BigInteger.Zero;
    public static readonly BigInteger B = new(7);

    public static readonly BigInteger Gx =
        Hex("79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798");

    public static readonly BigInteger Gy =
        Hex("483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8");

    /// <summary>A ordem do subgrupo gerado por G. Ela e PRIMA, e o cofator e 1.</summary>
    public static readonly BigInteger N =
        Hex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141");

    /// <summary>A curva montada.</summary>
    public static readonly Curva Curva = new(P, A, B, new Ponto(Gx, Gy), N, BigInteger.One);

    /// <summary>
    /// Verdadeiro quando 2^256 - 2^32 - 977 de fato da o primo publicado. Parece
    /// bobagem e nao e: a constante foi copiada em hexadecimal, e conferi-la
    /// contra a FORMULA pega um digito trocado.
    /// </summary>
    public static bool PBateComAFormula() =>
        P == BigInteger.Pow(2, 256) - BigInteger.Pow(2, 32) - 977;
}
