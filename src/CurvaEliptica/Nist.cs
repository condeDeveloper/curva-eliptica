using System.Globalization;
using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>
/// A curva P-256 do NIST, tambem chamada de secp256r1 e de prime256v1.
///
/// Ela esta aqui por um motivo pratico: o .NET sabe assinar e verificar nessa
/// curva, e nao sabe na secp256k1. Com a P-256 da para confrontar esta
/// implementacao com a do sistema operacional, que foi escrita por outra gente,
/// em outra linguagem, sem olhar para este codigo. Um teste que fecha contra ela
/// vale mais que qualquer quantidade de teste que so confere o codigo consigo
/// mesmo.
///
/// A diferenca de desenho em relacao a secp256k1 tambem e interessante. Aqui o
/// coeficiente a e p - 3, nao zero, e os parametros vem de uma semente passada
/// por SHA-1 que nunca foi explicada. Nao ha prova de que isso esconda alguma
/// coisa, e tambem nao ha prova do contrario, e e so dessa falta de explicacao
/// que vem a preferencia de muita gente pela curva de Koblitz.
///
/// O primo tambem e 3 mod 4, entao a raiz quadrada pelo atalho de Shanks vale
/// nas duas.
/// </summary>
public static class Nist
{
    private static BigInteger Hex(string h) =>
        BigInteger.Parse("0" + h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>2^256 - 2^224 + 2^192 + 2^96 - 1.</summary>
    public static readonly BigInteger P =
        Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");

    /// <summary>a = p - 3. Nao e sorteio: com a = -3 a duplicacao em coordenadas
    /// jacobianas economiza duas multiplicacoes, e toda a familia do NIST usa
    /// esse valor pelo mesmo motivo.</summary>
    public static readonly BigInteger A =
        Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFC");

    public static readonly BigInteger B =
        Hex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");

    public static readonly BigInteger Gx =
        Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");

    public static readonly BigInteger Gy =
        Hex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

    public static readonly BigInteger N =
        Hex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");

    public static readonly Curva Curva = new(P, A, B, new Ponto(Gx, Gy), N, BigInteger.One);

    /// <summary>2^256 - 2^224 + 2^192 + 2^96 - 1, conferido contra a formula.</summary>
    public static bool PBateComAFormula() =>
        P == BigInteger.Pow(2, 256) - BigInteger.Pow(2, 224) + BigInteger.Pow(2, 192)
             + BigInteger.Pow(2, 96) - 1;

    /// <summary>Verdadeiro quando a e de fato p - 3.</summary>
    public static bool ABateComMenosTres() => A == P - 3;
}
