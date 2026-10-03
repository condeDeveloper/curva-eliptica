using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// O k deterministico, contra os VETORES PUBLICADOS da RFC 6979.
///
/// Esses numeros nao sao meus. Estao no apendice A.2.5 da RFC, para a curva
/// P-256 com SHA-256, e foram conferidos por quem implementou a norma em
/// linguagem nenhuma parecida com esta. Um k que bate com eles significa que a
/// cadeia inteira esta certa: a conversao do hash em inteiro, o deslocamento dos
/// bits, a inicializacao do HMAC, a ordem dos campos e o laco de rejeicao.
///
/// Qualquer erro em qualquer uma dessas pecas muda o k inteiro, porque tudo
/// passa por HMAC. E por isso que esse teste vale mais do que o tamanho dele
/// sugere: nao existe "quase certo" aqui.
/// </summary>
public class Rfc6979Test
{
    private static BigInteger Hex(string h) =>
        BigInteger.Parse("0" + h.Replace(" ", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>A chave privada dos vetores da RFC para a P-256.</summary>
    private static readonly BigInteger X =
        Hex("C9AFA9D845BA75166B5C215767B1D6934E50C3DB36E89B127B8A622B120F6721");

    /// <summary>
    /// A chave publica publicada bate com a que eu calculo de d*G. E a primeira
    /// linha do vetor, e ela ja testa a multiplicacao escalar na P-256.
    /// </summary>
    [Fact]
    public void AChavePublicaDoVetorBate()
    {
        var publica = Ecdsa.ChavePublica(Nist.Curva, X);
        Assert.Equal(Hex("60FED4BA255A9D31C961EB74C6356D68C049B8923B61FA6CE669622E60F29FB6"), publica.X);
        Assert.Equal(Hex("7903FE1008B8BC99A41AE9E95628BC64F2F1B20C2D7E9F5177A3C294D4462299"), publica.Y);
    }

    /// <summary>
    /// O vetor da mensagem "sample": k, r e s publicados.
    /// </summary>
    [Fact]
    public void OVetorDaMensagemSampleBate()
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes("sample"));
        var k = Rfc6979.GerarK(Nist.N, X, hash);
        Assert.Equal(Hex("A6E3C57DD01ABE90086538398355DD4C3B17AA873382B0F24D6129493D8AAD60"), k);

        var assinatura = Ecdsa.AssinarComK(Nist.Curva, X,
            new BigInteger(hash, isUnsigned: true, isBigEndian: true), k);
        Assert.Equal(Hex("EFD48B2AACB6A8FD1140DD9CD45E81D69D2C877B56AAF991C34D0EA84EAF3716"), assinatura.R);
        Assert.Equal(Hex("F7CB1C942D657C41D436C7A1B6E29F65F3E900DBB9AFF4064DC4AB2F843ACDA8"), assinatura.S);

        // E a assinatura publicada verifica, pela minha conta e pela do sistema.
        var publica = Ecdsa.ChavePublica(Nist.Curva, X);
        Assert.True(Ecdsa.Verificar(Nist.Curva, publica,
            new BigInteger(hash, isUnsigned: true, isBigEndian: true), assinatura));
    }

    /// <summary>
    /// O vetor da mensagem "test", da mesma secao da RFC. Duas mensagens
    /// diferentes com a mesma chave precisam dar k diferentes, e e esse o
    /// proposito do esquema.
    /// </summary>
    [Fact]
    public void OVetorDaMensagemTestBate()
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes("test"));
        var k = Rfc6979.GerarK(Nist.N, X, hash);
        Assert.Equal(Hex("D16B6AE827F17175E040871A1C7EC3500192C4C92677336EC2537ACAEE0008E0"), k);

        var assinatura = Ecdsa.AssinarComK(Nist.Curva, X,
            new BigInteger(hash, isUnsigned: true, isBigEndian: true), k);
        Assert.Equal(Hex("F1ABB023518351CD71D881567B1EA663ED3EFCF6C5132B354F28D3B0B7D38367"), assinatura.R);
        Assert.Equal(Hex("019F4113742A2B14BD25926B49C649155F267E60D3814B4C0CC84250E46F0083"), assinatura.S);
    }

    /// <summary>
    /// Bits2Int e um DESLOCAMENTO, nao um resto.
    ///
    /// Quando o hash tem mais bits que a ordem, a RFC manda pegar os bits mais
    /// SIGNIFICATIVOS. Trocar isso por um resto modulo n passa em todo teste que
    /// so olha ida e volta, e quebra silenciosamente todos os vetores, porque
    /// para a maioria dos hashes os dois coincidem e so as vezes nao.
    /// </summary>
    [Fact]
    public void Bits2IntPegaOsBitsDeCimaENaoOResto()
    {
        // Ordem de 8 bits: de 2 bytes sobra so o primeiro.
        var ordem = new BigInteger(251);
        var bytes = new byte[] { 0xAB, 0xCD };
        Assert.Equal(new BigInteger(0xAB), Rfc6979.Bits2Int(bytes, ordem));
        Assert.NotEqual(Mod.Normalizar(0xABCD, ordem), Rfc6979.Bits2Int(bytes, ordem));

        // Do mesmo tamanho: nao desloca nada.
        Assert.Equal(new BigInteger(0xAB), Rfc6979.Bits2Int([0xAB], new BigInteger(255)));
    }

    /// <summary>
    /// ParaBytes enche com zeros a esquerda ate o tamanho pedido. Um k curto sem
    /// esse preenchimento muda o HMAC e quebra tudo, e e o tipo de defeito que so
    /// aparece em uma chave a cada duzentas e cinquenta e seis.
    /// </summary>
    [Fact]
    public void ParaBytesEnchePelaEsquerda()
    {
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x05 }, Rfc6979.ParaBytes(5, 4));
        Assert.Equal(new byte[] { 0x01, 0x00 }, Rfc6979.ParaBytes(256, 2));
        Assert.Equal(32, Rfc6979.ParaBytes(1, 32).Length);
        Assert.Equal(new byte[] { 0x00 }, Rfc6979.ParaBytes(0, 1));
    }

    /// <summary>
    /// O k sempre cai em [1, n-1], em muitas chaves e mensagens. O laco de
    /// rejeicao nao pode devolver zero nem passar da ordem.
    /// </summary>
    [Fact]
    public void OKSempreCaiNaFaixa()
    {
        var sorteio = new Random(41);
        for (var i = 0; i < 50; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % Secp256k1.N;
            if (privada.IsZero) continue;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"msg {i}"));
            var k = Rfc6979.GerarK(Secp256k1.N, privada, hash);
            Assert.InRange(k, BigInteger.One, Secp256k1.N - 1);
        }
    }

    /// <summary>
    /// O mesmo par (chave, mensagem) da sempre o mesmo k, e um bit mudado na
    /// mensagem da um k completamente diferente.
    /// </summary>
    [Fact]
    public void OKEhDeterministicoESensivel()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("nota fiscal"));
        var outro = SHA256.HashData(Encoding.UTF8.GetBytes("nota fiscal."));
        var k1 = Rfc6979.GerarK(Secp256k1.N, X, hash);
        var k2 = Rfc6979.GerarK(Secp256k1.N, X, hash);
        var k3 = Rfc6979.GerarK(Secp256k1.N, X, outro);
        Assert.Equal(k1, k2);
        Assert.NotEqual(k1, k3);
        // E a chave tambem entra: outra chave, outro k para a mesma mensagem.
        Assert.NotEqual(k1, Rfc6979.GerarK(Secp256k1.N, X + 1, hash));
    }
}
