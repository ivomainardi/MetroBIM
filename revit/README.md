# MetroSP – Add-in para Revit

Aba **MetroSP** na Ribbon do Revit.

Painéis, na ordem do trabalho: **Entrega Metrô** (1 Conferir Entrega, 2 Projeto e Local, 3 Exportar IFC),
**Classificação** (Aplicar, Verificar), **Dados do modelo**, **Parâmetros e famílias** e **Coordenador BIM**
(Padrões e planilhas, Arquivos IFC, Categorias IFC). Verificar Padrões, Consistência e Renomear Compartilhado não
têm mais botão próprio: abrem pelo Conferir Entrega, pelo Verificar classificação e por Parâmetros Compartilhados.
A tabela abaixo descreve as ferramentas.

| Painel | Botão | O que faz |
|---|---|---|
| Geral | Sobre | Mostra versão do add-in e do Revit |
| Entrega | Conferir Entrega | Uma janela só, com semáforo por etapa, para conferir o que o Metrô pede antes de exportar: arquivos do pacote, parâmetros e mapeamento, classe IFC, dados preenchidos (matriz categoria × campo) e classificação. Seleciona e pinta as pendências na vista e abre a ferramenta que corrige |
| Parâmetros | Renomear Compartilhado | Renomeia um parâmetro compartilhado da família mantendo o GUID |
| Parâmetros | Listar Compartilhados | Duas abas: **Parâmetros do arquivo** (GUID, tipo, categorias e o .txt de origem achado pelo GUID nos arquivos de referência) e **Comparar com .txt** (painel de referências com .txt e pastas, base Metrô por usuário em `%AppData%\MetroSP\referencias.ini`; compatível, nome/tipo diferente, nome em uso, só no modelo/.txt). Coluna **Padrão (base)** contra o .txt do MetroShared (no padrão, nome diferente, duplicado, parecido, fora do padrão, nome repetido). **Verificar uso** (valores, tabelas, filtros; na família: tipos, fórmulas, associações, cotas) e **Deletar sem uso** com confirmação. **Instância ↔ Tipo** troca o vínculo mantendo categorias e grupo, levando os valores junto e avisando onde algum se perderia. Exporta .txt reutilizável no Revit e .csv (visíveis, selecionados ou todos) |
| Parâmetros | Copiar Valores | Copia valores de um parâmetro para outro (seleção, vista ou modelo; filtro por categoria), convertendo entre texto, inteiro, número, medidas e sim/não. Analisa antes por elemento, com impactos e propostas; só preencher vazios ou sobrescrever; uma operação com Ctrl+Z |
| Classificação | Aplicar Classificação | Lê a planilha de classificação (.xlsx ou .csv: Código, Descrição e opcionais Código pai, Categorias, Edição), mostra a árvore com busca e grava `ClassificationCode = Código:Descrição` nos elementos selecionados. Janela aberta enquanto se seleciona no Revit; seleciona por categoria (vista ou modelo, opcionalmente só os sem classificação); cria/vincula o parâmetro nas categorias que não o têm; avisa quando o parâmetro é de tipo |
| Classificação | Verificar Classificação | Confere o preenchido com a planilha: sem classificação, formato inválido, código fora da lista, descrição diferente, categoria não prevista. Propõe correções (escrita da lista, código parecido), aplica as aceitas, exporta .csv. Filtro por categoria, etiquetas clicáveis, coluna com o motivo de bloqueio, criar o parâmetro nas categorias sem ele, Reanalisar |
| Classificação | Consistência Código×Descrição | Agrupa por código e mostra os que têm mais de uma descrição; escolhe-se a correta (a da planilha vem marcada) e unifica. Lista também descrições usadas com códigos diferentes |
| IFC | Verificar Padrões Metrô | Antes de exportar, verifica se parâmetros e mapeamento IFC atendem aos padrões do Metrô (arquivo .ids). Só conta os elementos que vão para o IFC |
| IFC | Exportar IFC | Exporta o IFC escolhendo uma classe instalada com o MetroSP ou indicando o setup .json e o .txt de property sets (computador, rede ou Autodesk Forma pelo Desktop Connector). Edita o .txt em tabela e mostra os parâmetros que não existem no modelo |

## Estrutura

```
MetroSP.sln
src/MetroSP/
  MetroSP.csproj        multi-versão (propriedade RevitVersion)
  MetroSP.addin         manifesto lido pelo Revit
  App.cs                IExternalApplication: cria a aba e os painéis
  Commands/             um IExternalCommand por botão
  Parametros/           lógica e janela da ferramenta de parâmetros
  Resources/            ícones PNG (Nome32.png / Nome16.png), opcionais
```

## Compilar e testar

Requisitos: SDK .NET 10 no Windows. Revit 2022, 2025, 2026 ou 2027.

```
dotnet build -c Debug -p:RevitVersion=2025
```

Em **Debug**, o build copia `MetroSP.addin` e `MetroSP.dll` para
`%AppData%\Autodesk\Revit\Addins\<versão>\`. É só abrir o Revit.
Na primeira vez o Revit pergunta se confia no add-in: escolha "Sempre carregar".

Revit 2027 usa .NET 10; 2025 e 2026 usam .NET 8; 2022 usa .NET Framework 4.8 (o csproj escolhe sozinho).
As diferenças entre versões (API do Revit e do .NET) ficam em `src/MetroSP/Compat.cs`; no 2022 vão junto
`System.Text.Json` e as DLLs de que ele depende.

## Publicar uma versão

1. Suba a `<Version>` no `src/MetroSP/MetroSP.csproj` e descreva a versão no `NOTAS-DA-VERSAO.md`.
2. Faça o merge na `main`. O GitHub Actions (`.github/workflows/release.yml`) compila para 2022, 2025, 2026 e 2027,
   gera os instaladores e cria a release `v<versão>` com os `.exe` e os `.zip`. O Revit 2022 tem sempre um instalador
   separado (`MetroSP-Revit2022-Setup-<versão>.exe`). Ele só roda quando o csproj muda.

Sem o Actions, dá para publicar pelo PC com `.\publicar.ps1` (precisa do [GitHub CLI](https://cli.github.com/)
logado e de um clone da `main`).

## Renomear parâmetro compartilhado (mantendo GUID)

O Revit não deixa renomear parâmetro compartilhado direto, e cada documento (projeto ou família) guarda
um único registro do nome por GUID: enquanto esse registro existir, o nome antigo volta.

**Numa família:**

1. Troca o parâmetro compartilhado por um parâmetro de família temporário (`ReplaceParameter`)
   e apaga o registro antigo do GUID.
2. Troca de volta para compartilhado usando uma definição com **o mesmo GUID** e o nome novo,
   criada num .txt temporário.
3. Confere o GUID e o nome final; se não conferirem, desfaz tudo.

**No projeto, com "Renomear também em todas as famílias carregadas":**

O Revit só deixa apagar o registro do nome antigo no projeto quando nenhuma família carregada usa mais o GUID.
Por isso, numa única operação (desfeita inteira se o nome final não for o novo):

1. Em cada família (de dentro para fora nas aninhadas), o compartilhado vira parâmetro de família e o registro
   do GUID na família é apagado. Cada família preparada é guardada num .rfa temporário.
2. Guarda os valores do parâmetro em todos os elementos, o vínculo de projeto e os campos das tabelas.
3. Recarrega as famílias preparadas: o projeto deixa de usar o GUID.
4. Apaga o registro antigo do GUID no projeto.
5. Volta o parâmetro a compartilhado com o nome novo em cada família e recarrega de novo.
6. Refaz o vínculo, restaura valores e campos das tabelas e confere o nome.

Opcional: renomeia a linha do GUID no seu arquivo de parâmetros compartilhados (com backup `.bak`).
Tags e tabelas continuam funcionando porque referenciam o GUID. Os .rfa da biblioteca não são alterados.

## Conferir Entrega Metrô

Junta numa janela as conferências que antes ficavam espalhadas (Verificar Padrões Metrô, Classificação, conferência do .txt
no Exportar IFC, Listar Compartilhados), para o projetista ver de uma vez se o modelo está pronto para a entrega.

**Pacote do Metrô.** Os arquivos da entrega ficam num pacote `.metrosp` (texto simples, caminhos relativos à pasta
dele): IDS (opcional), setup de exportação `.json`, `.txt` de property sets, tabela de classes IFC (opcional),
planilha de classificação e o `.txt` do MetroShared. O coordenador BIM monta o pacote uma vez (botão **Pacote... ›
Salvar pacote como...**) numa pasta compartilhada ou no Forma; o projetista só abre o pacote. Sem pacote, a primeira
vez aproveita os arquivos já escolhidos nas outras ferramentas. O pacote em uso fica em `%AppData%\MetroSP\conferencia.ini`
e é repassado ao Exportar IFC, ao Verificar Padrões Metrô, à Classificação e à base do Listar Compartilhados.

**Etapas** (verde = ok, amarelo = aviso que não impede, vermelho = pendência, cinza = não conferido):

1. **Arquivos do Metrô**: acha e lê cada arquivo; avisa tipos desconhecidos no `.txt` (que saem como texto), linhas
   fora de um PropertySet e setup que não exporta os property sets do usuário.
2. **Parâmetros e mapeamento**: o parâmetro de cada campo existe nos elementos de cada categoria, o tipo de dado é
   compatível com o `.txt`, o GUID é o do MetroShared; com IDS, campos exigidos sem linha no `.txt` ou com tipo diferente.
3. **Classe IFC dos elementos**: elementos que saem como `IfcBuildingElementProxy` e elementos de classes às quais
   nenhum campo do Metrô se aplica.
4. **Dados preenchidos**: matriz categoria × campo com a % de elementos com o campo preenchido (e, com IDS, dentro
   dos valores aceitos). Clicar numa célula mostra os tipos e elementos pendentes.
5. **Classificação**: o `ClassificationCode` preenchido, no formato `Código:Descrição` e na planilha (as mesmas regras
   do Verificar Classificação).

Campos exigidos: com IDS no pacote, os requisitos de propriedade obrigatórios do IDS para a classe IFC do elemento;
sem IDS, todas as propriedades do `.txt` que se aplicam à classe. Psets padrão (`Pset_`, `Qto_`) sem linha no `.txt`
e sem o parâmetro no elemento não contam como pendência (o exportador pode calcular).

Elementos conferidos: os de modelo que vão para o IFC (classe IFC resolvida como no Verificar Padrões Metrô), sem linhas,
grupos, vínculos, massas e opções de projeto secundárias; no **modelo inteiro** ou **só na vista ativa**.

Ações: **Selecionar no Revit** e **Pintar na vista** (sobreposição vermelha por elemento na vista ativa;
**Limpar pintura** devolve a aparência que cada elemento tinha). Botões que abrem a ferramenta certa:
Parâmetros de projeto do Revit, Listar Compartilhados, Aplicar Classificação (com os elementos já selecionados),
Verificar Classificação e Detalhes do IDS. Ao fechar uma ferramenta modal, a conferência roda de novo.
**Exportar IFC...** abre o Exportar IFC com o setup e o `.txt` do pacote e avisa antes se ainda há pendência.
**Exportar pendências (.csv)** grava todas as linhas, com os IDs dos elementos. Nada é gravado no modelo, a não
ser a pintura (desfeita por Limpar pintura).

## Verificar mapeamento IDS (antes de exportar)

Entrada: o arquivo `.ids`, opcionalmente o `.txt` de property sets definidos pelo usuário e a
tabela de classes IFC (`.txt` salvo em Opções de IFC). Nada é exportado.

Para cada especificação do IDS:
1. Descobre quais elementos exportam como a entidade pedida (parâmetro "Exportar para IFC como",
   senão a tabela de classes, senão o padrão do exportador).
2. Para cada requisito de propriedade, procura o mapeamento Pset.Propriedade → parâmetro do Revit
   e compara o tipo IFC pedido com o tipo declarado no mapeamento.
3. Confere se o parâmetro existe nos elementos (instância ou tipo) e se o tipo de dado é compatível.
4. Opcional: conta valores vazios e valores fora da enumeração/padrão exigido.
5. Atributos (Name, Description, ObjectType, Tag, LongName) via parâmetros IfcName etc.;
   classificação via parâmetros ClassificationCode; material pelos materiais do elemento.

Status possíveis: OK, Aviso, SemMapeamento, ParametroAusente, TipoIncompativel, ValoresVazios,
ValorInvalido, SemElementos, NaoVerificado. Duplo clique numa linha seleciona os elementos.

Limitações da v1: aplicabilidade só por entidade (outros filtros são ignorados com aviso);
facet partOf não verificado; limites numéricos ficam para a validação do IFC exportado (etapa 2, ifctester).

Exemplos em `docs/exemplos/`.

## Exportar IFC com configurações guardadas

### Classes que vêm com o instalador

O instalador traz configurações prontas, uma por classe, em `MetroSP\IFC\` ao lado da DLL:

```
IFC\
  Setups\<Classe>.json         setup do exportador (opcional)
  PropertySets\<Classe>.txt    property sets definidos pelo usuário
  Mapeamentos\<Classe>.txt     tabela de mapeamento de parâmetros (opcional)
```

Na janela, basta escolher a **Classe**: os arquivos são preenchidos sozinhos. Para acrescentar ou trocar uma
classe padrão, coloque os arquivos em `src/MetroSP/IFC/` e gere o instalador.

O botão **Editar property sets...** abre o `.txt` em tabela: property set, entidades IFC, propriedade, tipo,
parâmetro do Revit e a situação, com as linhas cujo parâmetro não existe no modelo destacadas (e um filtro para
ver só essas). O parâmetro pode ser trocado por um do modelo, e dá para acrescentar e remover propriedades.
Numa classe instalada, salvar grava uma cópia em `%AppData%\MetroSP\IFC\PropertySets\<Classe>.txt`, que passa
a valer no lugar da padrão só para esse usuário; **Restaurar padrão** volta ao `.txt` instalado. Num arquivo
indicado à mão, o editor pergunta onde salvar.

### Classe pelo nome do modelo

Ao abrir a janela, a classe é escolhida pelo nome do modelo. Um `Nomenclatura.txt` (instalado em `IFC\`, na pasta
do Forma online ou em `%AppData%\MetroSP\IFC\`) traz regras, uma por linha: `<padrão>` + tab (ou `;`) + `<Classe>`.
O padrão usa `*` e `?` sobre o nome sem `.rvt` (ex.: `*-ARQ-*	Arquitetura`); começando com `re:` é uma expressão
regular, e o grupo `(?<classe>...)` pode dar a classe direto. Sem regra que case, vale a classe cujo nome aparece
como trecho do nome do modelo (separado por `.`, `-`, `_` ou espaço). A janela mostra por que a classe foi escolhida.

### Forma online (sem Desktop Connector)

O MetroSP pode ler uma pasta do Autodesk Forma pela internet. Precisa de um app na Autodesk (aps.autodesk.com ›
My Apps, tipo "Desktop, Mobile, Single-Page App", callback `http://localhost:47312/metrosp/`, API Data Management)
liberado pelo admin em Account Admin › Custom Integrations; o Client ID vai em `IFC\forma-online.ini`.

Na janela, **Entrar...** abre o navegador para o login Autodesk (uma vez; o acesso fica guardado criptografado para o
usuário do Windows) e **Escolher pasta...** navega por hubs, projetos e pastas. A pasta pode ter `Setups\`,
`PropertySets\` e `Mapeamentos\`, ou os `.json` e `.txt` soltos, e um `Nomenclatura.txt`. Ela é copiada para
`%LocalAppData%\MetroSP\FormaOnline\` e, a cada vez que o Exportar IFC abre, só o que tem versão nova no Forma é
baixado. Sem internet, vale a última cópia. As classes da pasta valem sobre as instaladas.

### Arquivos indicados à mão

Com a classe em "Outros arquivos", a janela pede cada arquivo separadamente, com um **Procurar...** para cada um. Eles podem estar em
qualquer pasta: no computador, na rede ou no Autodesk Forma pelo Desktop Connector
(`%USERPROFILE%\DC\ACCDocs\<Hub>\<Projeto>`). Se nada foi escolhido ainda e o modelo está na nuvem,
o Procurar começa na pasta do projeto dele.

1. **Setup de exportação** (`.json` salvo em Exportar › IFC › Modificar configuração › Salvar configuração).
2. **Property sets definidos pelo usuário** (`.txt`).
3. **Tabela de mapeamento de parâmetros** (`.txt`, opcional).

Ao escolher um setup, os `.txt` que ele indica preenchem os campos 2 e 3, se forem achados nesta máquina:
o caminho gravado no `.json` (que é da máquina de quem salvou), um arquivo com o mesmo nome perto do setup,
ou, se o setup estiver numa pasta `Setups\`, o `.txt` com o mesmo nome em `PropertySets\` e `Mapeamentos\`
ao lado dela. Dá para trocar qualquer um. As três escolhas ficam lembradas para a próxima exportação.

Antes de exportar, o `.txt` é lido com o mesmo leitor do Verificar Padrões Metrô e a janela avisa quais parâmetros
do mapeamento não existem no modelo. Escolhe-se a vista 3D (ou o modelo inteiro), a pasta e o nome do arquivo.
As escolhas ficam em `%AppData%\MetroSP\ifc_export.ini`.

No Revit 2027 o exportador lê os property sets do usuário guardados no modelo, e não o `.txt`. Por isso o `.txt`
escolhido é carregado no modelo logo antes de exportar, substituindo os que estavam lá, dentro da mesma transação
da exportação (desfeita no fim, a não ser que o setup grave o GUID IFC).

Cada propriedade do `.json` vai para o exportador como opção, do mesmo jeito que a janela de exportação do Revit
faz. Diferenças: o modelo de mapeamento guardado no .rvt (Revit 2025+) é ignorado para valer o `.txt`;
vínculos não são exportados; a classificação e o endereço do projeto usados são os salvos no próprio modelo.
