using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Conde.CurvaEliptica.Testes;

/// <summary>
/// ECDSA, conferido contra um JUIZ DE VERDADE: a implementacao do proprio
/// sistema operacional, pela classe ECDsa do .NET.
///
/// Esse e o ponto alto da suite. Todo o resto confere este codigo contra ele
/// mesmo ou contra axiomas; aqui ele e confrontado com uma implementacao escrita
/// por outra gente, em outra linguagem, que nunca viu este arquivo. Se as duas
/// assinam e verificam uma a assinatura da outra, as duas estao falando o mesmo
/// protocolo, e nao so sendo consistentes consigo mesmas.
///
/// A curva usada no confronto e a P-256, e nao a secp256k1, por um motivo
/// chato: o .NET so conhece as curvas nomeadas que o sistema oferece, e no
/// Windows a secp256k1 nao esta entre elas. E justamente por isso que a P-256
/// entrou nesta biblioteca.
/// </summary>
public class EcdsaTest
{
    private static byte[] Bytes(BigInteger x) => Rfc6979.ParaBytes(x, 32);

    private static ECDsa DoSistema(BigInteger privada)
    {
        var publica = Ecdsa.ChavePublica(Nist.Curva, privada);
        var parametros = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Bytes(privada),
            Q = new ECPoint { X = Bytes(publica.X), Y = Bytes(publica.Y) },
        };
        // Importar ja valida: se o Q nao batesse com d*G, ou nao estivesse na
        // curva, o proprio sistema recusaria aqui.
        parametros.Validate();
        return ECDsa.Create(parametros);
    }

    /// <summary>
    /// Os parametros da P-256 conferidos contra a formula e contra o grupo, antes
    /// de qualquer assinatura. Um digito trocado na transcricao apareceria aqui.
    /// </summary>
    [Fact]
    public void OsParametrosDaP256Fecham()
    {
        Assert.True(Nist.PBateComAFormula());
        Assert.True(Nist.ABateComMenosTres());
        Assert.Equal(3, (int)(Nist.P % 4));
        Assert.True(Nist.Curva.Contem(Nist.Curva.Geradora));
        Assert.True(Escalar.Escada(Nist.Curva, Nist.Curva.Geradora, Nist.N).Infinito);
    }

    /// <summary>
    /// EU assino, o SISTEMA verifica.
    ///
    /// E o teste que nao da para passar por acaso: o formato, a ordem dos bytes,
    /// a reducao do hash e a conta toda precisam estar certos ao mesmo tempo.
    /// </summary>
    [Fact]
    public void OSistemaAceitaAsMinhasAssinaturas()
    {
        var sorteio = new Random(17);
        for (var i = 0; i < 10; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % Nist.N;
            if (privada.IsZero) continue;
            var mensagem = Encoding.UTF8.GetBytes($"mensagem numero {i}");
            var hash = SHA256.HashData(mensagem);

            var minha = Ecdsa.Assinar(Nist.Curva, privada, mensagem);
            using var sistema = DoSistema(privada);
            // O .NET espera r e s concatenados, com tamanho fixo.
            var bytes = (byte[])[.. Bytes(minha.R), .. Bytes(minha.S)];
            Assert.True(sistema.VerifyHash(hash, bytes), $"o sistema recusou a assinatura {i}");
        }
    }

    /// <summary>
    /// O SISTEMA assina, EU verifico. O caminho inverso, que testa a outra metade
    /// da conta.
    /// </summary>
    [Fact]
    public void EuAceitoAsAssinaturasDoSistema()
    {
        var sorteio = new Random(23);
        for (var i = 0; i < 10; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % Nist.N;
            if (privada.IsZero) continue;
            var mensagem = Encoding.UTF8.GetBytes($"assinada pelo sistema {i}");
            var hash = SHA256.HashData(mensagem);

            using var sistema = DoSistema(privada);
            var bytes = sistema.SignHash(hash);
            Assert.Equal(64, bytes.Length);
            var assinatura = new Assinatura(
                new BigInteger(bytes.AsSpan(0, 32), isUnsigned: true, isBigEndian: true),
                new BigInteger(bytes.AsSpan(32, 32), isUnsigned: true, isBigEndian: true));

            var publica = Ecdsa.ChavePublica(Nist.Curva, privada);
            var h = new BigInteger(hash, isUnsigned: true, isBigEndian: true);
            Assert.True(Ecdsa.Verificar(Nist.Curva, publica, h, assinatura),
                $"eu recusei a assinatura {i} do sistema");
        }
    }

    /// <summary>
    /// A chave publica que eu calculo e a mesma que o sistema calcula, bit a bit.
    /// </summary>
    [Fact]
    public void AChavePublicaBateComADoSistema()
    {
        var sorteio = new Random(29);
        for (var i = 0; i < 10; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % Nist.N;
            if (privada.IsZero) continue;
            using var sistema = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var deles = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = Bytes(privada),
                Q = new ECPoint
                {
                    X = Bytes(Ecdsa.ChavePublica(Nist.Curva, privada).X),
                    Y = Bytes(Ecdsa.ChavePublica(Nist.Curva, privada).Y),
                },
            });
            var q = deles.ExportParameters(false).Q;
            var meu = Ecdsa.ChavePublica(Nist.Curva, privada);
            Assert.Equal(Bytes(meu.X), q.X);
            Assert.Equal(Bytes(meu.Y), q.Y);
        }
    }

    /// <summary>
    /// Assinar duas vezes a mesma mensagem com a mesma chave da a MESMA
    /// assinatura, porque o k e deterministico.
    ///
    /// Isso nao e detalhe de conforto: e o que permite testar assinatura contra
    /// vetor publicado, e e o que tira do caminho o gerador de aleatorios, que e
    /// a peca que mais falhou na pratica.
    /// </summary>
    [Fact]
    public void AssinarDuasVezesDaAMesmaAssinatura()
    {
        var mensagem = Encoding.UTF8.GetBytes("o mesmo texto");
        var privada = BigInteger.Parse("112233445566778899");
        var a = Ecdsa.Assinar(Secp256k1.Curva, privada, mensagem);
        var b = Ecdsa.Assinar(Secp256k1.Curva, privada, mensagem);
        Assert.Equal(a, b);

        // E uma mensagem diferente da outra assinatura.
        var c = Ecdsa.Assinar(Secp256k1.Curva, privada, Encoding.UTF8.GetBytes("outro texto"));
        Assert.NotEqual(a, c);
    }

    /// <summary>
    /// Assinar e verificar na secp256k1, ida e volta, com mensagens e chaves
    /// variadas. Aqui o juiz e o proprio par de operacoes mais a recusa do que
    /// foi adulterado.
    /// </summary>
    [Fact]
    public void AssinarEVerificarNaSecp256k1()
    {
        var curva = Secp256k1.Curva;
        var sorteio = new Random(31);
        for (var i = 0; i < 8; i++)
        {
            var privada = EscalarTest.Sortear(sorteio, 255) % curva.Ordem;
            if (privada.IsZero) continue;
            var publica = Ecdsa.ChavePublica(curva, privada);
            var mensagem = Encoding.UTF8.GetBytes($"transferir {i} reais");
            var assinatura = Ecdsa.Assinar(curva, privada, mensagem);

            Assert.True(Ecdsa.Verificar(curva, publica, mensagem, assinatura));
            // Mensagem trocada: recusa.
            Assert.False(Ecdsa.Verificar(curva, publica, Encoding.UTF8.GetBytes($"transferir {i + 1} reais"), assinatura));
            // Chave trocada: recusa.
            var outra = Ecdsa.ChavePublica(curva, privada + 1);
            Assert.False(Ecdsa.Verificar(curva, outra, mensagem, assinatura));
            // Assinatura mexida: recusa.
            Assert.False(Ecdsa.Verificar(curva, publica, mensagem, new Assinatura(assinatura.R + 1, assinatura.S)));
            Assert.False(Ecdsa.Verificar(curva, publica, mensagem, new Assinatura(assinatura.R, assinatura.S + 1)));
        }
    }

    /// <summary>
    /// r e s fora de [1, n-1] sao recusados ANTES de qualquer conta.
    ///
    /// Sem essa conferencia, varias bibliotecas ja aceitaram assinatura que nao
    /// deveriam, e isso rendeu CVE mais de uma vez. O zero e o caso obvio; o
    /// valor igual a n e o que escapa de quem so testa o zero.
    /// </summary>
    [Fact]
    public void AssinaturaForaDaFaixaEhRecusada()
    {
        var curva = Secp256k1.Curva;
        var privada = BigInteger.Parse("987654321987654321");
        var publica = Ecdsa.ChavePublica(curva, privada);
        var mensagem = Encoding.UTF8.GetBytes("pagar");
        var boa = Ecdsa.Assinar(curva, privada, mensagem);
        Assert.True(Ecdsa.Verificar(curva, publica, mensagem, boa));

        foreach (var ruim in new[]
        {
            new Assinatura(0, boa.S),
            new Assinatura(boa.R, 0),
            new Assinatura(curva.Ordem, boa.S),
            new Assinatura(boa.R, curva.Ordem),
            new Assinatura(curva.Ordem + boa.R, boa.S),
            new Assinatura(-1, boa.S),
        })
            Assert.False(Ecdsa.Verificar(curva, publica, mensagem, ruim), $"aceitou {ruim}");

        // E a chave publica no infinito tambem nao passa.
        Assert.False(Ecdsa.Verificar(curva, Ponto.NoInfinito, mensagem, boa));
    }

    /// <summary>
    /// A MALEABILIDADE: (r, n-s) verifica igual a (r, s).
    ///
    /// As duas sao assinaturas validas da mesma mensagem, e qualquer um no meio
    /// do caminho troca uma pela outra sem ter a chave privada. Quem identifica
    /// uma transacao pelo hash dela descobre que o identificador muda. Foi esse o
    /// problema de maleabilidade do Bitcoin, e a solucao foi passar a exigir o s
    /// baixo.
    /// </summary>
    [Fact]
    public void ANegativaDoSTambemVerifica()
    {
        var curva = Secp256k1.Curva;
        var privada = BigInteger.Parse("424242424242424242");
        var publica = Ecdsa.ChavePublica(curva, privada);
        var mensagem = Encoding.UTF8.GetBytes("maleavel");
        var hash = new BigInteger(SHA256.HashData(mensagem), isUnsigned: true, isBigEndian: true);

        var assinatura = Ecdsa.Assinar(curva, privada, mensagem);
        var gemea = new Assinatura(assinatura.R, curva.Ordem - assinatura.S);
        Assert.NotEqual(assinatura, gemea);
        Assert.True(Ecdsa.Verificar(curva, publica, hash, assinatura));
        Assert.True(Ecdsa.Verificar(curva, publica, hash, gemea));

        // A forma canonica escolhe uma das duas, e escolhe sempre a mesma.
        Assert.Equal(Codificacao.SBaixo(curva, assinatura), Codificacao.SBaixo(curva, gemea));
        Assert.True(Codificacao.SBaixo(curva, assinatura).S <= curva.Ordem / 2);
    }

    /// <summary>
    /// O ataque do PlayStation 3, executado.
    ///
    /// Duas assinaturas com o MESMO k entregam a chave privada por uma divisao.
    /// Sem forca bruta, sem tabela, sem supercomputador. Foi assim que a chave de
    /// assinatura do console saiu em 2010, e e por isso que a RFC 6979 existe.
    /// </summary>
    [Fact]
    public void DuasAssinaturasComOMesmoKEntregamAChave()
    {
        var curva = Secp256k1.Curva;
        var privada = BigInteger.Parse("6054985408914991618199692674183347178543879382739238473290478923");
        var k = BigInteger.Parse("12345678901234567890123456789012345678901234567890");

        var h1 = new BigInteger(SHA256.HashData(Encoding.UTF8.GetBytes("primeira")), isUnsigned: true, isBigEndian: true);
        var h2 = new BigInteger(SHA256.HashData(Encoding.UTF8.GetBytes("segunda")), isUnsigned: true, isBigEndian: true);

        var a1 = Ecdsa.AssinarComK(curva, privada, h1, k);
        var a2 = Ecdsa.AssinarComK(curva, privada, h2, k);
        // O k repetido aparece de longe: o r e o MESMO nas duas. E a assinatura
        // dessa falha, visivel em qualquer blockchain publica.
        Assert.Equal(a1.R, a2.R);

        var recuperada = Ecdsa.RecuperarChaveDeKRepetido(curva, h1, a1, h2, a2);
        Assert.Equal(Mod.Normalizar(privada, curva.Ordem), recuperada);
    }

    /// <summary>
    /// Com k deterministico, o mesmo nao acontece: duas mensagens diferentes dao
    /// r diferente, que e o que impede a conta de cima.
    /// </summary>
    [Fact]
    public void ComKDeterministicoOsRSaoDiferentes()
    {
        var curva = Secp256k1.Curva;
        var privada = BigInteger.Parse("88888888888888888888888888");
        var vistos = new HashSet<BigInteger>();
        for (var i = 0; i < 25; i++)
        {
            var assinatura = Ecdsa.Assinar(curva, privada, Encoding.UTF8.GetBytes($"mensagem {i}"));
            Assert.True(vistos.Add(assinatura.R), $"o r repetiu na mensagem {i}");
        }
    }

    [Fact]
    public void ChavePrivadaForaDaFaixaNaoAssina()
    {
        var curva = Secp256k1.Curva;
        Assert.Throws<ArgumentOutOfRangeException>(() => Ecdsa.ChavePublica(curva, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ecdsa.ChavePublica(curva, curva.Ordem));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ecdsa.ChavePublica(curva, -1));
    }
}
