# MetroSP – Add-in para Revit

Aba **MetroSP** na Ribbon do Revit.

| Painel | Botão | O que faz |
|---|---|---|
| Geral | Sobre | Mostra versão do add-in e do Revit |
| Parâmetros | Renomear Compartilhado | Renomeia um parâmetro compartilhado da família mantendo o GUID |
| Parâmetros | Listar Compartilhados | Duas abas: **Parâmetros do arquivo** (GUID, tipo, categorias e o .txt de origem achado pelo GUID nos arquivos de referência) e **Comparar com .txt** (painel de referências com .txt e pastas, base Metrô por usuário em `%AppData%\MetroSP\referencias.ini`; compatível, nome/tipo diferente, nome em uso, só no modelo/.txt). Coluna **Padrão (base)** contra o .txt do MetroShared (no padrão, nome diferente, duplicado, parecido, fora do padrão, nome repetido). **Verificar uso** (valores, tabelas, filtros; na família: tipos, fórmulas, associações, cotas) e **Deletar sem uso** com confirmação. Exporta .txt reutilizável no Revit e .csv (visíveis, selecionados ou todos) |
| Parâmetros | Copiar Valores | Copia valores de um parâmetro para outro (seleção, vista ou modelo; filtro por categoria), convertendo entre texto, inteiro, número, medidas e sim/não. Analisa antes por elemento, com impactos e propostas; só preencher vazios ou sobrescrever; uma operação com Ctrl+Z |
| Classificação | Aplicar Classificação | Lê a planilha de classificação (.xlsx ou .csv: Código, Descrição e opcionais Código pai, Categorias, Edição), mostra a árvore com busca e grava `ClassificationCode = Código:Descrição` nos elementos selecionados. Janela aberta enquanto se seleciona no Revit; seleciona por categoria (vista ou modelo, opcionalmente só os sem classificação); cria/vincula o parâmetro nas categorias que não o têm; avisa quando o parâmetro é de tipo |
| Classificação | Verificar Classificação | Confere o preenchido com a planilha: sem classificação, formato inválido, código fora da lista, descrição diferente, categoria não prevista. Propõe correções (escrita da lista, código parecido), aplica as aceitas, exporta .csv. Filtro por categoria, etiquetas clicáveis, coluna com o motivo de bloqueio, criar o parâmetro nas categorias sem ele, Reanalisar |
| Classificação | Consistência Código×Descrição | Agrupa por código e mostra os que têm mais de uma descrição; escolhe-se a correta (a da planilha vem marcada) e unifica. Lista também descrições usadas com códigos diferentes |
| IFC / IDS | Verificar Mapeamento IDS | Antes de exportar, verifica se parâmetros e mapeamento IFC atendem a um IDS |
| IFC / IDS | Exportar IFC | Exporta o IFC com um setup .json do exportador e o .txt de property sets, indicados um a um (computador, rede ou Autodesk Forma pelo Desktop Connector). Confere o .txt contra o modelo antes de exportar |
| Modelagem | Contar Elementos | Exemplo: conta elementos por categoria |

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

Requisitos: SDK .NET 10 no Windows. Revit 2025, 2026 ou 2027.

```
dotnet build -c Debug -p:RevitVersion=2025
```

Em **Debug**, o build copia `MetroSP.addin` e `MetroSP.dll` para
`%AppData%\Autodesk\Revit\Addins\<versão>\`. É só abrir o Revit.
Na primeira vez o Revit pergunta se confia no add-in: escolha "Sempre carregar".

Revit 2027 usa .NET 10; 2025 e 2026 usam .NET 8 (o csproj escolhe sozinho).

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

A janela pede cada arquivo separadamente, com um **Procurar...** para cada um. Eles podem estar em
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

Antes de exportar, o `.txt` é lido com o mesmo leitor do Verificar IDS e a janela avisa quais parâmetros
do mapeamento não existem no modelo. Escolhe-se a vista 3D (ou o modelo inteiro), a pasta e o nome do arquivo.
As escolhas ficam em `%AppData%\MetroSP\ifc_export.ini`.

Cada propriedade do `.json` vai para o exportador como opção, do mesmo jeito que a janela de exportação do Revit
faz. Diferenças: o modelo de mapeamento guardado no .rvt (Revit 2025+) é ignorado para valer o `.txt`;
vínculos não são exportados; a classificação e o endereço do projeto usados são os salvos no próprio modelo.
