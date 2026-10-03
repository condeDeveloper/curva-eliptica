# curva-eliptica

Criptografia de curva elíptica do zero em C# e .NET 8: aritmética modular,
grupo da curva, secp256k1, P-256, ECDSA, ECDH e o `k` determinístico da RFC
6979. O juiz é a implementação do próprio sistema operacional, e os vetores são
os publicados.

```
$ dotnet medidor.dll canal

== o que o tempo conta sobre a chave ==
chave (160 bits)                    bits 1   somas obvio  somas escada
todos os bits em 1                     160           160           160
um bit so                                1             1           160
metade de cada                          80            80           160
alternados                              81            81           160
```

A primeira coluna é a chave privada. A terceira é quantas somas o método óbvio
fez. São o mesmo número.

## O juiz é de fora

Toda suíte de testes de criptografia corre um risco específico: conferir o
código contra ele mesmo. Ida e volta passa, propriedade interna passa, e a
implementação inteira pode estar falando um protocolo que só ela entende.

Aqui o juiz vem de fora, em três níveis.

**O sistema operacional.** A classe `ECDsa` do .NET assina e verifica na curva
P-256, e os testes cruzam as duas pontas: eu assino e ela verifica, ela assina e
eu verifico, e a chave pública que eu calculo é importada por ela e exportada
byte a byte igual. Outra gente, outra linguagem, nenhum olhar neste código. A CI
roda isso em Linux, Windows e macOS, onde o provedor de criptografia é
diferente.

É por isso que a P-256 existe nesta biblioteca. Eu ia implementar só a
secp256k1, que é a do Bitcoin e a mais interessante das duas. Só que o .NET não
a oferece como curva nomeada no Windows, e sem uma curva em comum não há com
quem confrontar. A P-256 entrou para ser o ponto de encontro.

**Os vetores publicados.** O apêndice A.2.5 da RFC 6979 traz chave, mensagem,
`k`, `r` e `s` para a P-256 com SHA-256. Tudo ali passa por HMAC, então não
existe "quase certo": um bit trocado na conversão do hash, na ordem dos campos,
no deslocamento dos bits ou no laço de rejeição muda o `k` inteiro e derruba os
três valores de uma vez.

**Os axiomas.** Numa curva pequena, `y² = x³ + 2x + 3` sobre F(103), todos os
pontos cabem na memória. Associatividade, comutatividade, neutro e inverso são
conferidos no domínio inteiro, e a multiplicação escalar é comparada com a força
bruta que soma o ponto a si mesmo, uma vez por unidade. Axioma não depende de
implementação nenhuma: uma soma que viola a associatividade está errada, e não
há discussão.

## O tempo de execução é parte da chave

A multiplicação escalar é a operação que tudo usa, e é nela que a chave vaza.

O método óbvio, dobrar e somar, soma quando o bit é 1 e não soma quando é 0.
Logo, o número de somas **é** a quantidade de bits 1 do escalar. Quem mede o
tempo da assinatura, ou o consumo de corrente do cartão, lê esse número. Isso
não é hipótese de laboratório: medir tempo e consumo já recuperou chave de
produto no mercado.

A escada de Montgomery faz uma soma e um dobro por bit, sempre, qualquer que
seja o bit. O trabalho não depende do segredo. Ela paga por isso:

```
$ dotnet medidor.dll escalar

== somas e dobros por multiplicacao, escalares de 256 bits, 7 rodadas ==
metodo                           somas    dobros     total   menor tempo
dobrar e somar                   127.7     255.0     382.7       7.67 ms
escada de Montgomery             254.9     254.9     509.8      11.42 ms
wNAF janela 2                     85.4     256.3     341.7      13.38 ms
wNAF janela 3                     66.0     256.3     322.3      19.22 ms
wNAF janela 4                     54.8     255.7     310.5      26.49 ms
wNAF janela 5                     50.1     255.3     305.4      26.41 ms
wNAF janela 6                     51.7     255.3     307.0      25.04 ms
```

Um terço a mais de operações, e em troca o relógio para de contar a chave. Vale
a pena.

O wNAF é o caminho oposto: ele é para quando o escalar **não** é segredo, como
na verificação de assinatura. Ele ganha de duas formas. Usando dígitos
negativos, porque subtrair um ponto custa o mesmo que somar (negar é trocar o
sinal do `y`), o que derruba a densidade de dígitos não nulos de 1/2 para
1/(w+1). E com a janela, pré-computando G, 3G, 5G e assim por diante.

## O que as medidas me corrigiram

**A janela maior nem sempre é melhor.** Eu ia escrever que o wNAF melhora com a
janela e parar por aí. A janela 6 faz **52** somas contra **50** da janela 5: ela
é pior. A tabela pré-computada custa 2^(w-2) - 1 somas, que dobram a cada
janela a mais, enquanto o laço só economiza a diferença entre 256/(w+1) e
256/(w+2), que encolhe. Na janela 6 a tabela já gasta 15 somas para economizar
três. O joelho da curva é 5, e eu só soube disso porque medi.

**O relógio desta máquina mede a temperatura, não o algoritmo.** A primeira
versão do medidor rodava um método de cada vez, em sequência. A escada apareceu
com quase cinco vezes o tempo do método óbvio, quando ela faz só um terço mais
de operações. Medindo a escada **primeiro**, ela ficou mais rápida que o método
óbvio medido depois. O que o relógio estava mostrando era o processador baixando
a frequência conforme esquenta. Agora são rodadas intercaladas, com a ordem
girando, e o tempo relatado é o menor de todas; ainda assim a tabela acima sobe
com a posição na lista, e é por isso que a conclusão se apoia na coluna
**total** e nunca na do tempo. Contagem de operação dá o mesmo número em
qualquer máquina.

**O primo da curva de teste não é escolha de gosto.** Eu peguei 97 para a curva
pequena, por ser o primeiro primo de dois dígitos que me veio. O teste de
descompressão estourou na hora: a raiz quadrada modular usada ali é o atalho de
Shanks, `a^((p+1)/4)`, que só vale com p ≡ 3 mod 4, e 97 é 1 mod 4. Troquei por
103. O primo da secp256k1 também é 3 mod 4, e é exatamente por isso que
descomprimir um ponto é barato nela.

**O ponto com y = 0 não tem bit de paridade.** Eu escrevi o teste de
descompressão afirmando que, para cada `x` que tem ponto, existem dois `y`, um
par e um ímpar. Ele quebrou. Quando o lado direito da equação dá zero, a raiz é
zero, e **zero é o próprio negativo**: o ponto (x, 0) é seu próprio inverso,
está sozinho na vertical, e o bit de paridade não tem o que distinguir. Não era
defeito da implementação, era a aritmética; a afirmação errada era minha.

**A busca pelo gerador de teste desistia cedo demais.** O medidor monta uma
curva de brinquedo para cada primo e precisa de um ponto de ordem grande. Eu
escrevi "pega o primeiro ponto que achar ou desiste", e as linhas de 1019 e
10007 simplesmente sumiram da tabela, porque o primeiro ponto da varredura tinha
ordem 2. Uma tabela com buracos parece uma tabela completa.

## A segurança é a raiz quadrada, não o tamanho

O passo de bebê e passo de gigante acha o logaritmo discreto em √n operações, e
não em n. Medido em curvas de brinquedo:

```
$ dotnet medidor.dll quebrar

   primo     ordem   forca bruta  bebe/gigante    raiz
     103        54            54            13       7
    1019       516           516            45      22
   10007      9846          9846           198      99
  100003    100294        100294           633     316
```

A coluna do meio é a ordem; a seguinte é o que custou de verdade. O expoente é
1/2, e ele não muda de escala.

A consequência é a conta que dimensiona chave: uma ordem de 2^256 custa 2^128
passos para quebrar. **A chave de 256 bits da secp256k1 dá 128 bits de
segurança, não 256.** Quem lê o tamanho da chave como se fosse o custo do ataque
erra por um fator que não cabe em palavra nenhuma.

E é por isso que a ordem precisa ser prima. Se ela tivesse fatores pequenos, o
Pohlig-Hellman quebraria o problema em pedaços do tamanho dos fatores, e a raiz
quadrada passaria a ser da maior parte, não do todo. A ordem da secp256k1 é
prima e o cofator é 1: todo ponto da curva, menos o infinito, gera o grupo
inteiro.

## O ataque do PlayStation 3, executado

ECDSA assina com `s = (h + r·d)/k`. Se duas assinaturas usam o mesmo `k`, dá
para isolar:

```
k = (h1 - h2) / (s1 - s2)        depois        d = (s1·k - h1) / r
```

Duas assinaturas e uma divisão. Sem força bruta, sem tabela, sem
supercomputador. Em 2010 a Sony usou um `k` fixo para assinar o código do
PlayStation 3, e a chave de assinatura do console saiu por essa conta. Carteiras
Android perderam dinheiro pelo mesmo motivo, com um gerador de aleatórios mal
semeado.

O teste executa isso: assina duas mensagens com o mesmo `k`, recupera a chave
privada e confere que é a mesma. E registra o sinal que denuncia a falha em
qualquer blockchain pública: quando o `k` repete, o **`r` é igual nas duas
assinaturas**, e isso é visível de fora.

A RFC 6979 existe para tirar o sorteio da jogada. O `k` passa a ser derivado da
chave e da mensagem por HMAC, e o gerador de aleatórios, que é a peça que mais
falhou na prática, sai do caminho. De quebra, a assinatura vira determinística, e
é isso que permite testá-la contra vetor publicado.

## O que os parsers aceitam é metade do problema

Quase todo defeito explorado em carteira e em biblioteca de assinatura nos
últimos anos estava na leitura dos bytes, não na matemática. Por isso a
decodificação aqui é estrita, e metade dos testes de formato é sobre o que
precisa ser **recusado**:

- **ponto fora da curva**: é o que impede o ataque de curva inválida. As
  fórmulas de soma nunca olham o coeficiente `b`, então um ponto forjado é
  aceito pela aritmética e vive numa curva diferente, de ordem cheia de fatores
  pequenos. O teste `DhTest` executa o ataque numa curva pequena: sem a
  conferência, um único segredo entrega a chave módulo a ordem pequena; com ela,
  o ponto nem entra. Isso já derrubou implementação de TLS em produção;
- **DER com zero sobrando à esquerda**: a frouxidão aqui deu a maleabilidade de
  transação do Bitcoin;
- **`r` ou `s` fora de [1, n-1]**: já rendeu CVE em mais de uma biblioteca;
- **assinatura com `s` alto**: `(r, s)` e `(r, n-s)` verificam igual, então
  qualquer um no meio do caminho troca uma pela outra sem ter a chave privada.
  Quem identifica uma transação pelo hash da assinatura descobre que o
  identificador pode mudar. A forma canônica de `s` baixo escolhe uma das duas.

## As peças

| arquivo | o que faz |
| --- | --- |
| `Mod.cs` | aritmética módulo primo; inverso por Euclides, raiz por Shanks |
| `Curva.cs` | o grupo: soma, tangente, negação, descompressão |
| `Secp256k1.cs` | a curva do Bitcoin, com o primo conferido contra a fórmula |
| `Nist.cs` | a P-256, que existe para ser o ponto de encontro com o .NET |
| `Escalar.cs` | dobrar e somar, escada de Montgomery, wNAF e a força bruta |
| `Ecdsa.cs` | assinar, verificar e a recuperação de chave de `k` repetido |
| `Rfc6979.cs` | o `k` determinístico por HMAC |
| `Dh.cs` | ECDH e as três conferências da chave que veio de fora |
| `Codificacao.cs` | SEC1 e DER, em leitura estrita |
| `Ordem.cs` | contagem de pontos, ordem, Hasse e o logaritmo discreto |

## Como rodar

```
dotnet test testes/CurvaEliptica.Testes/CurvaEliptica.Testes.csproj -c Release
dotnet run --project ferramentas/Medidor/Medidor.csproj -c Release -- tudo
```

As medidas aceitam `escalar`, `canal`, `quebrar`, `assinar` e `tudo`.

## Aviso

Isto é um estudo. A aritmética é em `BigInteger` e em coordenadas afins, sem
redução de Barrett, sem coordenadas jacobianas e sem inversão em tempo
constante, então a escada de Montgomery aqui é constante em número de operações
e não em tempo de relógio. Para usar de verdade existe `libsecp256k1`, escrita
por gente que passou anos nos detalhes que este repositório explica.

## Licença

MIT.
