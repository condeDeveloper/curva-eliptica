using System.Numerics;

namespace Conde.CurvaEliptica;

/// <summary>Um ponto da curva. X e Y nulos quando e o ponto no infinito.</summary>
public readonly struct Ponto : IEquatable<Ponto>
{
    public BigInteger X { get; }
    public BigInteger Y { get; }
    public bool Infinito { get; }

    private Ponto(BigInteger x, BigInteger y, bool infinito) { X = x; Y = y; Infinito = infinito; }

    public Ponto(BigInteger x, BigInteger y) : this(x, y, false) { }

    /// <summary>
    /// O elemento NEUTRO do grupo. Ele nao tem coordenada, e e por isso que ele
    /// existe como caso separado em vez de como um par de numeros: a curva e um
    /// grupo, e todo grupo precisa de um neutro; na forma de Weierstrass ele fica
    /// "no infinito", onde as retas verticais se encontram.
    /// </summary>
    public static readonly Ponto NoInfinito = new(BigInteger.Zero, BigInteger.Zero, true);

    public bool Equals(Ponto o) => Infinito ? o.Infinito : !o.Infinito && X == o.X && Y == o.Y;
    public override bool Equals(object? o) => o is Ponto p && Equals(p);
    public override int GetHashCode() => Infinito ? 0 : HashCode.Combine(X, Y);
    public static bool operator ==(Ponto a, Ponto b) => a.Equals(b);
    public static bool operator !=(Ponto a, Ponto b) => !a.Equals(b);

    public override string ToString() => Infinito ? "O" : $"({X:x}, {Y:x})";
}

/// <summary>
/// Uma curva eliptica na forma de Weierstrass: y² = x³ + ax + b, sobre o corpo
/// de p elementos.
///
/// Os pontos formam um GRUPO, e isso e tudo o que a criptografia usa. A soma e
/// definida geometricamente — a reta por dois pontos corta a curva num terceiro,
/// e o resultado e o reflexo dele — e as formulas abaixo sao essa construcao
/// escrita em coordenadas.
///
/// A segurança vem do logaritmo discreto: dado G e k*G, achar k. Multiplicar e
/// barato, desfazer nao se sabe fazer em tempo razoavel, e e so dessa assimetria
/// que tudo depende.
/// </summary>
public sealed class Curva
{
    public Curva(BigInteger p, BigInteger a, BigInteger b, Ponto geradora, BigInteger ordem, BigInteger cofator)
    {
        if (p <= 3) throw new ArgumentOutOfRangeException(nameof(p));
        P = p; A = Mod.Normalizar(a, p); B = Mod.Normalizar(b, p);
        Geradora = geradora; Ordem = ordem; Cofator = cofator;

        // O discriminante 4a³ + 27b² precisa ser nao nulo, senao a curva e
        // SINGULAR: ela tem um ponto com duas tangentes ou uma cuspide, a soma
        // deixa de ser bem definida, e o logaritmo discreto vira facil. E a
        // conferencia que separa uma curva de um desenho parecido com uma curva.
        var disc = Mod.Somar(Mod.Multiplicar(4, Mod.Potencia(A, 3, p), p),
                             Mod.Multiplicar(27, Mod.Multiplicar(B, B, p), p), p);
        if (disc.IsZero) throw new ArgumentException("4a^3 + 27b^2 = 0: a curva e singular", nameof(a));
    }

    public BigInteger P { get; }
    public BigInteger A { get; }
    public BigInteger B { get; }
    public Ponto Geradora { get; }

    /// <summary>Quantos pontos o subgrupo gerado por G tem. n*G = O.</summary>
    public BigInteger Ordem { get; }

    /// <summary>Quantas vezes a ordem do subgrupo cabe na da curva inteira.</summary>
    public BigInteger Cofator { get; }

    /// <summary>Verdadeiro quando o ponto satisfaz a equacao da curva.</summary>
    public bool Contem(Ponto ponto)
    {
        if (ponto.Infinito) return true;
        var esquerda = Mod.Multiplicar(ponto.Y, ponto.Y, P);
        var direita = Mod.Somar(Mod.Somar(Mod.Potencia(ponto.X, 3, P),
                                          Mod.Multiplicar(A, ponto.X, P), P), B, P);
        return esquerda == direita;
    }

    /// <summary>O ponto refletido no eixo x, que e o inverso no grupo.</summary>
    public Ponto Negar(Ponto ponto) =>
        ponto.Infinito ? ponto : new Ponto(ponto.X, Mod.Subtrair(BigInteger.Zero, ponto.Y, P));

    /// <summary>
    /// A soma do grupo.
    ///
    /// Sao tres casos, e os tres saem da mesma construcao geometrica:
    ///
    /// - com o neutro, devolve o outro;
    /// - com dois pontos de mesmo x e y oposto, a reta e VERTICAL e nao corta a
    ///   curva em lugar nenhum: o resultado e o infinito;
    /// - com dois pontos iguais, nao ha "reta por dois pontos" e usa-se a
    ///   TANGENTE, cujo coeficiente angular sai derivando a equacao.
    ///
    /// O caso da tangente e o que se esquece, e esquece-lo nao da erro: da uma
    /// divisao por zero so as vezes, ou pior, uma resposta errada quando os dois
    /// pontos sao iguais por acaso no meio de uma multiplicacao escalar.
    /// </summary>
    public Ponto Somar(Ponto p1, Ponto p2)
    {
        if (p1.Infinito) return p2;
        if (p2.Infinito) return p1;

        if (p1.X == p2.X)
        {
            // Mesmo x: ou sao opostos (reta vertical), ou sao o mesmo ponto.
            if (p1.Y != p2.Y || p1.Y.IsZero) return Ponto.NoInfinito;
            return Dobrar(p1);
        }

        var m = Mod.Dividir(Mod.Subtrair(p2.Y, p1.Y, P), Mod.Subtrair(p2.X, p1.X, P), P);
        var x = Mod.Subtrair(Mod.Subtrair(Mod.Multiplicar(m, m, P), p1.X, P), p2.X, P);
        var y = Mod.Subtrair(Mod.Multiplicar(m, Mod.Subtrair(p1.X, x, P), P), p1.Y, P);
        return new Ponto(x, y);
    }

    /// <summary>O dobro de um ponto, pela tangente.</summary>
    public Ponto Dobrar(Ponto ponto)
    {
        if (ponto.Infinito || ponto.Y.IsZero) return Ponto.NoInfinito;
        // Derivando y² = x³ + ax + b: 2y dy = (3x² + a) dx
        var m = Mod.Dividir(Mod.Somar(Mod.Multiplicar(3, Mod.Multiplicar(ponto.X, ponto.X, P), P), A, P),
                            Mod.Multiplicar(2, ponto.Y, P), P);
        var x = Mod.Subtrair(Mod.Multiplicar(m, m, P), Mod.Multiplicar(2, ponto.X, P), P);
        var y = Mod.Subtrair(Mod.Multiplicar(m, Mod.Subtrair(ponto.X, x, P), P), ponto.Y, P);
        return new Ponto(x, y);
    }

    public Ponto Subtrair(Ponto a, Ponto b) => Somar(a, Negar(b));

    /// <summary>
    /// O ponto da curva com aquele x, escolhendo o y pela paridade.
    ///
    /// E a descompressao: guardar so o x e um bit de paridade corta o tamanho da
    /// chave publica pela metade, e e assim que todo formato serio guarda ponto.
    /// Devolve nulo quando nao existe ponto com aquele x, o que acontece em
    /// METADE dos valores: so os quadrados tem raiz.
    /// </summary>
    public Ponto? Descomprimir(BigInteger x, bool yImpar)
    {
        var direita = Mod.Somar(Mod.Somar(Mod.Potencia(x, 3, P), Mod.Multiplicar(A, x, P), P), B, P);
        var y = Mod.RaizQuadrada(direita, P);
        if (y is null) return null;
        var v = y.Value;
        if (v.IsEven == yImpar) v = Mod.Subtrair(BigInteger.Zero, v, P);
        return new Ponto(x, v);
    }
}
