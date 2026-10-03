using System.Numerics;
using System.Text;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// Os formatos de bytes, com o juiz sendo a ida e volta mais a RECUSA.
///
/// Um codificador que so e testado por ida e volta passa aceitando lixo: basta
/// ele aceitar na leitura tudo que ele mesmo nunca escreve. Por isso metade
/// deste arquivo e sobre o que precisa ser RECUSADO.
/// </summary>
public class CodificacaoTest
{
    /// <summary>
    /// Ida e volta de ponto, nas duas formas, em todo ponto da curva pequena e em
    /// duzentos pontos da secp256k1.
    /// </summary>
    [Fact]
    public void PontoVaiEVoltaNasDuasFormas()
    {
        var pequena = CurvaTest.Pequena();
        foreach (var p in CurvaTest.TodosOsPontos(pequena))
        {
            Assert.Equal(p, Codificacao.LerPonto(pequena, Codificacao.EscreverPonto(pequena, p, false)));
            if (p.Infinito) continue;
            // A forma comprimida nao distingue o ponto de ordem 2 do negativo
            // dele, porque ele E o negativo dele; nos outros, ela e exata.
            var voltou = Codificacao.LerPonto(pequena, Codificacao.EscreverPonto(pequena, p, true));
            Assert.Equal(p, voltou);
        }

        var curva = Secp256k1.Curva;
        for (var k = 1; k <= 200; k++)
        {
            var p = Escalar.Escada(curva, curva.Geradora, k);
            var cheio = Codificacao.EscreverPonto(curva, p, false);
            var curto = Codificacao.EscreverPonto(curva, p, true);
            Assert.Equal(65, cheio.Length);
            Assert.Equal(33, curto.Length);
            Assert.Equal(0x04, cheio[0]);
            Assert.True(curto[0] is 0x02 or 0x03);
            Assert.Equal(p, Codificacao.LerPonto(curva, cheio));
            Assert.Equal(p, Codificacao.LerPonto(curva, curto));
        }
    }

    /// <summary>
    /// O gerador da secp256k1 na forma comprimida e exatamente aquele valor que
    /// aparece em toda documentacao de Bitcoin, comecando com 02 79BE667E.
    /// </summary>
    [Fact]
    public void OGeradorComprimidoBateComOValorPublicado()
    {
        var bytes = Codificacao.EscreverPonto(Secp256k1.Curva, Secp256k1.Curva.Geradora, true);
        var hex = Convert.ToHexString(bytes);
        Assert.Equal("0279BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798", hex);
        // O y publicado e PAR, e e por isso que o prefixo e 02.
        Assert.True(Secp256k1.Gy.IsEven);
    }

    /// <summary>
    /// O ponto que NAO esta na curva e recusado na leitura.
    ///
    /// Essa e a conferencia que impede o ataque de curva invalida. Sem ela, a
    /// aritmetica aceita o ponto sem reclamar, porque as formulas de soma nunca
    /// olham o coeficiente b.
    /// </summary>
    [Fact]
    public void PontoForaDaCurvaEhRecusado()
    {
        var curva = Secp256k1.Curva;
        var bom = Escalar.Escada(curva, curva.Geradora, 9);
        var ruim = new Ponto(bom.X, bom.Y + 1);
        Assert.False(curva.Contem(ruim));

        var bytes = (byte[])[0x04, .. Rfc6979.ParaBytes(ruim.X, 32), .. Rfc6979.ParaBytes(ruim.Y, 32)];
        var erro = Assert.Throws<FormatException>(() => Codificacao.LerPonto(curva, bytes));
        Assert.Contains("curva", erro.Message);
    }

    [Fact]
    public void FormatosErradosDePontoSaoRecusados()
    {
        var curva = Secp256k1.Curva;
        var bom = Codificacao.EscreverPonto(curva, curva.Geradora, false);

        // Prefixo que nao existe.
        Assert.Throws<FormatException>(() => Codificacao.LerPonto(curva, [0x05, .. bom[1..]]));
        // Tamanho errado.
        Assert.Throws<FormatException>(() => Codificacao.LerPonto(curva, bom[..^1]));
        Assert.Throws<FormatException>(() => Codificacao.LerPonto(curva, [.. bom, 0x00]));
        // Comprimido com um x que nao tem ponto nenhum. Em metade dos x o lado
        // direito nao e quadrado, entao algum desses cai.
        var achou = false;
        for (var x = 1; x < 40 && !achou; x++)
            if (curva.Descomprimir(x, false) is null)
            {
                achou = true;
                Assert.Throws<FormatException>(() =>
                    Codificacao.LerPonto(curva, [0x02, .. Rfc6979.ParaBytes(x, 32)]));
            }
        Assert.True(achou, "nenhum x sem ponto nos primeiros quarenta; o teste nao exercitou nada");
    }

    /// <summary>
    /// Ida e volta de assinatura em DER, incluindo o caso do byte alto, que e o
    /// que mais quebra implementacao.
    /// </summary>
    [Fact]
    public void AssinaturaVaiEVoltaEmDer()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(53);
        for (var i = 0; i < 40; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % curva.Ordem;
            if (privada.IsZero) continue;
            var assinatura = Ecdsa.Assinar(curva, privada, Encoding.UTF8.GetBytes($"der {i}"));
            var bytes = Codificacao.EscreverAssinatura(assinatura);
            Assert.Equal(0x30, bytes[0]);
            Assert.Equal(bytes.Length - 2, bytes[1]);
            Assert.Equal(assinatura, Codificacao.LerAssinatura(bytes));
        }
    }

    /// <summary>
    /// O numero cujo primeiro byte passa de 0x7f ganha um 0x00 na frente, porque
    /// o INTEGER do DER e com sinal. Sem esse zero o leitor do outro lado le um
    /// numero negativo.
    /// </summary>
    [Fact]
    public void OZeroDeSinalEntraQuandoOBytePassaDe7f()
    {
        var alto = BigInteger.Parse("200");            // 0xC8, primeiro bit ligado
        var baixo = BigInteger.Parse("100");           // 0x64
        var bytes = Codificacao.EscreverAssinatura(new Assinatura(alto, baixo));
        // 30 len 02 02 00 C8 02 01 64
        Assert.Equal("3007020200C8020164", Convert.ToHexString(bytes));
        Assert.Equal(new Assinatura(alto, baixo), Codificacao.LerAssinatura(bytes));
    }

    /// <summary>
    /// DER nao canonico e recusado. E exatamente a frouxidao aqui que deu a
    /// maleabilidade de transacao do Bitcoin.
    /// </summary>
    [Fact]
    public void DerNaoCanonicoEhRecusado()
    {
        // 30 09 02 03 00 00 C8 02 01 64: dois zeros a esquerda, um a mais.
        Assert.Throws<FormatException>(() =>
            Codificacao.LerAssinatura(Convert.FromHexString("300802030000C8020164")));
        // Nao comeca com SEQUENCE.
        Assert.Throws<FormatException>(() =>
            Codificacao.LerAssinatura(Convert.FromHexString("3107020200C8020164")));
        // Comprimento declarado errado.
        Assert.Throws<FormatException>(() =>
            Codificacao.LerAssinatura(Convert.FromHexString("3006020200C8020164")));
        // Sobra byte no fim.
        Assert.Throws<FormatException>(() =>
            Codificacao.LerAssinatura(Convert.FromHexString("3008020200C802016400")));
    }

    /// <summary>
    /// A forma de s baixo e idempotente e escolhe sempre a mesma das duas
    /// assinaturas gemeas.
    /// </summary>
    [Fact]
    public void SBaixoEhEstavel()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(59);
        for (var i = 0; i < 30; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % curva.Ordem;
            if (privada.IsZero) continue;
            var a = Ecdsa.Assinar(curva, privada, Encoding.UTF8.GetBytes($"baixo {i}"));
            var baixa = Codificacao.SBaixo(curva, a);
            Assert.True(baixa.S <= curva.Ordem / 2);
            Assert.Equal(baixa, Codificacao.SBaixo(curva, baixa));
            Assert.Equal(baixa, Codificacao.SBaixo(curva, new Assinatura(a.R, curva.Ordem - a.S)));
        }
    }
}
