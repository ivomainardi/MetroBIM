# MetroSP 0.2.7

Revit 2025, 2026 e 2027.

## Exportar IFC: classes prontas no instalador

- O instalador traz os .txt de property sets (e, quando houver, o setup .json e o mapeamento) de cada classe.
  Na janela, basta escolher a **Classe** e exportar.
- Continua dando para trocar qualquer arquivo pelo **Procurar...** (a classe passa para "Outros arquivos").
- Novo **Editar property sets...**: mostra o .txt em tabela, com as propriedades cujo parâmetro não existe no
  modelo destacadas, sugere os parâmetros do modelo, e permite acrescentar e remover propriedades. A edição de
  uma classe instalada fica salva só para o usuário, com **Restaurar padrão** para voltar ao original.
  Os selos do topo do editor filtram a tabela: o de property sets abre a escolha dos psets, o de parâmetros não
  encontrados e o de tipos não aceitos mostram só essas linhas (novo clique desliga), e o de propriedades limpa
  os filtros.
- **Classes do MetroSP no instalador:** B2, B3, B5, B7, I1, J2, K9, M1, N3 e S5 (property sets MNEPI-L20).
- **Conferência pelo padrão do exportador da Autodesk:** todo .txt incluído (classe, Procurar ou Forma) e toda
  edição manual são conferidos pelas mesmas regras do leitor do exportador IFC da Autodesk: tipo de dado
  reconhecido (senão sai como Text), cabeçalho com I/T e entidades, colunas a mais, linhas fora de um property set,
  nomes Pset_ reservados. Ao exportar com problemas, a janela oferece abrir o editor; ao salvar no editor, os tipos
  com grafia conhecida ("Lenght", "Number", "Wattage", "Percent"...) são corrigidos na hora.
- Os .txt das classes B2, B3, M1, N3 e S5 foram corrigidos nesse padrão (revisão registrada no cabeçalho de cada um).
- **Classe pelo nome do modelo:** a janela já abre com a classe indicada pelos dois últimos caracteres do nome do
  modelo (ex.: `...B3.rvt` → B3), regra que fica no `Nomenclatura.txt` e pode ser mudada.
- **Forma online:** dá para entrar com a conta Autodesk e escolher uma pasta do Forma direto pela internet, sem o
  Desktop Connector. Os arquivos dela são baixados e, a cada exportação, só o que mudou no Forma é atualizado.
  O **Client ID** do app do MetroSP na Autodesk é informado no próprio plugin (botão "Client ID..." na janela
  Exportar IFC), sem editar arquivo.

## Correção: Revit 2027 só exportava o primeiro bloco de cada property set

No 2027 os property sets do .txt são carregados no modelo, onde cada um é guardado pelo nome. Nos .txt com um
bloco "Metro" (ou MetroLoc, MetroQto...) por entidade, só um bloco sobrevivia, e as outras entidades saíam sem
esses psets (no J2, só as paredes recebiam Metro/MetroLoc/MetroQto). Agora os blocos de mesmo nome são juntados
antes de carregar, com as entidades unidas. O .txt original não muda. No 2025 e no 2026 nada muda.

No modelo, cada propriedade aponta para um só parâmetro. Quando o .txt tira a mesma propriedade de parâmetros
diferentes conforme a entidade (MetroLoc.Nivel = Reference Level na viga e Level na laje), ou quando ela só existe
em parte dos blocos, a exportação cria um parâmetro auxiliar temporário (MetroSP_<Pset>_<Propriedade>), preenche
em cada elemento com o parâmetro certo para a entidade dele e o remove no fim. Assim lajes, sapatas, paredes e
lances de escada voltam a receber o nível, o desvio, o material e as quantidades do bloco deles, e nenhuma entidade
recebe propriedade de bloco de outra.
J2: o MetroLoc dos aparelhos de apoio passou para IfcMechanicalFastener (estava IfcDiscreteAccessory).

## Categorias IFC: categoria do Revit → classe IFC, em IFC4 e IFC 4.3

- Novo botão **Categorias IFC** na aba MetroSP: uma tabela para IFC4 (4.0.2.1, o que o Revit grava) e outra
  para IFC 4.3, com a lista de classes de cada esquema (o 4.3 traz IfcBuiltElement, IfcRail, IfcCourse,
  IfcEarthworksFill, IfcGeotechnicalStratum...). Por padrão aparecem só as categorias usadas no modelo, com a
  contagem; as linhas diferentes entre os dois esquemas ficam destacadas.
- Sem tabela salva, começa pelo mapeamento que o modelo usa hoje. Salvar grava a sua cópia em
  %AppData%\MetroSP\IFC\Categorias (IFC4.txt e IFC4x3.txt); também dá para importar, exportar e copiar de um
  esquema para o outro.
- Na exportação, a tabela do esquema do setup é gravada no modelo como modelo de mapeamento do Revit
  ("MetroSP IFC4" ou "MetroSP IFC4x3") e fica ativa. Sem "gravar GUID", o modelo volta como estava; com ele, o
  modelo de mapeamento MetroSP fica salvo e ativo no modelo.

## Arquivos IFC: gestão dos setups, property sets e mapeamentos

- Novo botão **Arquivos IFC** na aba MetroSP (e o link **Gerenciar arquivos...** na janela Exportar IFC).
  Uma aba para cada tipo de arquivo, com as classes à esquerda e de onde vem cada arquivo: seu, do Forma online
  ou instalado. Os seus valem no lugar dos outros.
- **Vários de uma vez**: arraste (ou escolha) quantos .json e .txt quiser. O MetroSP reconhece o tipo pelo
  conteúdo e a classe pelo nome do arquivo, mostra uma lista para conferir e trocar, e avisa o que substitui.
- **Setup**: as opções principais aparecem abertas e editáveis (versão IFC, fase, space boundaries, divisão por
  nível, só visíveis, quantidades, GUID e psets padrão, do Revit e de material). Salvar um setup instalado ou do
  Forma grava uma cópia sua. Coordenadas e EPSG continuam definidos pelo MetroSP na exportação.
- **Property sets**: cada classe é conferida no modelo aberto: parâmetros não encontrados e propriedades sem
  valor em parte dos elementos, por property set. "Editar..." (ou clique duplo num property set) abre o editor.
- Voltar ao padrão, apagar, mudar a classe e salvar uma cópia. Toda versão substituída ou apagada fica guardada em
  %AppData%\MetroSP\IFC\Anteriores.

## Editor de property sets: verificar e corrigir no mesmo lugar

- Árvore à esquerda com cada property set e as entidades IFC dele; escolher um item filtra a grade, e o número
  laranja mostra quantas linhas têm problema.
- Nova coluna **No modelo**: em quantos elementos da entidade o parâmetro tem valor (ex.: "212 de 240").
  Linhas sem valor em parte dos elementos também ficam destacadas, com um selo próprio que filtra.
- Ao escolher uma linha com problema, um painel explica o motivo e sugere parâmetros de nome parecido que existem
  no modelo, com a contagem; um clique troca.

## Exportar IFC: EPSG em branco

- Nova opção "Exportar com o EPSG em branco" na seção Localização, ligada por padrão. Com ela ligada, o IFC sai sem EPSG (sem IfcMapConversion/IfcProjectedCRS) e a janela não cobra mais o dado GIS do Location.
- Desligada, o IFC mantém o EPSG do setup ou o que o exportador do Revit gerar.

## Interface mais limpa

- **Exportar IFC** reorganizada: classe numa linha (com o motivo da escolha e o Forma online como link ao lado),
  setup, property sets e mapeamento numa lista com o nome do arquivo, um selo verde ou laranja e o botão de ação
  (o caminho completo aparece ao passar o mouse; em "Outros arquivos" os caminhos ficam editáveis), localização
  só conferindo com o botão Localização IFC..., pasta e nome do .ifc num campo só e os avisos somados no rodapé.
- O mesmo padrão nas outras janelas: títulos de seção e rótulos em maiúsculas pequenas, arquivos como lista com
  selos no Verificar mapeamento IDS (com resumo no rodapé) e textos de ajuda movidos para as dicas.

## Sobre

- Clicar na logo Ação BIM · Metrô SP, no fim da aba MetroSP, abre a janela **Sobre** (o botão Sobre do painel
  Geral abre a mesma): versão, data da compilação, Revit em uso, ferramentas por painel e pasta de instalação,
  com **Copiar informações** para mandar ao suporte.

# MetroSP 0.2.6 (anterior)

Revit 2025, 2026 e 2027.

## Correção: Exportar IFC no Revit 2027 sem os property sets do usuário

No Revit 2027 o exportador IFC passou a ler os property sets definidos pelo usuário do próprio modelo
(os que a janela nativa cria ao importar o .txt) e ignora o .txt passado pelo plugin. Por isso os psets só
saíam depois de configurados na janela nativa.

Agora, no 2027, o .txt escolhido é carregado no modelo logo antes de exportar, no lugar dos psets que já
estavam lá. Se o setup não grava o GUID IFC, isso é desfeito no fim e o modelo fica como estava; se grava,
os psets do .txt ficam salvos no modelo (o relatório avisa). No 2025 e no 2026 nada muda.

# MetroSP 0.2.5 (anterior)

Revit 2025, 2026 e 2027.

## Correção: Renomear Compartilhado com famílias

Na 0.2.4 o comando parava com "O Revit não deixou liberar o nome antigo no projeto". O Revit só deixa apagar
o registro do nome antigo quando nenhuma família carregada usa mais o parâmetro, e a ferramenta tentava apagar
com as famílias ainda usando. Agora são duas passagens:

1. em cada família (e nas aninhadas), o parâmetro vira parâmetro de família, com valores e fórmulas, e ela é
   recarregada assim: o projeto deixa de usar o parâmetro;
2. o nome antigo é apagado no projeto;
3. em cada família o parâmetro volta a ser compartilhado, com o mesmo GUID e o nome novo, e ela é recarregada de novo;
4. o vínculo do projeto e os valores são refeitos, e o nome é conferido.

Também corrigido o "Loaded Family Editing failed" em aninhadas: as famílias preparadas ficavam abertas, e o Revit
não abre a mesma família duas vezes (acontecia com aninhadas compartilhadas, que também estão no projeto).
Agora cada família é guardada num arquivo temporário e reaberta só quando precisa.

Se ainda assim o nome antigo não puder ser liberado, a mensagem diz quais famílias continuam usando o parâmetro.

# MetroSP 0.2.4

Revit 2025, 2026 e 2027.

## Renomear Compartilhado mais rápido com famílias aninhadas

Com "Incluir famílias aninhadas" marcado, a 0.2.3 abria todas as famílias aninhadas de todas as famílias,
em todos os níveis, mesmo as que não tinham nada a ver com o parâmetro. Agora só abre as aninhadas de uma
família quando o parâmetro está registrado nela (o que acontece quando alguma aninhada o usa). Nas famílias
sem o parâmetro, ela é aberta, conferida e fechada na hora. Em modelos com muitas famílias aninhadas, o número
de aberturas cai muito; a contagem aparece no relatório final ("N abertas").

## Listar Compartilhados: base MetroShared e deletar parâmetros sem uso

**Base MetroShared.** O botão **Base** (acima da tabela, na aba "Parâmetros do arquivo") escolhe o .txt do
MetroShared. É a mesma base que as outras ferramentas já usam (Aplicar Classificação, por exemplo), guardada em
`%AppData%\MetroSP\referencias.ini`; quem já tinha definido a base em "Comparar com .txt" não precisa fazer nada.
A nova coluna **Padrão (base)** mostra, para cada parâmetro:

- **No padrão**: mesmo GUID e mesmo nome do MetroShared;
- **Nome diferente do padrão**: mesmo GUID, outro nome;
- **Duplicado**: o MetroShared tem um parâmetro com esse nome, mas com outro GUID (ou o do arquivo não é compartilhado);
- **Parecido com o padrão**: o nome só difere em acentos, maiúsculas, espaços ou pontuação (ex.: "Classificacao");
- **Fora do padrão**: não existe no MetroShared;
- **nome repetido no arquivo**: dois parâmetros do arquivo com o mesmo nome (ou parecido) e GUIDs diferentes.

Os contadores no topo e a cor da linha acompanham. A coluna também sai no .csv.

**Verificar uso e Deletar sem uso**, no rodapé. *Verificar uso* preenche a coluna **Uso**:

- no projeto: valor preenchido em algum elemento (ou tipo, para vínculo de tipo) das categorias vinculadas,
  campo de alguma tabela, regra de algum filtro;
- na família: valor em algum tipo, fórmula própria ou na fórmula de outro parâmetro, associação a parâmetros de
  elementos (visibilidade, material, família aninhada...), rótulo de cota.

*Deletar sem uso...* deleta os selecionados (sem seleção: os visíveis com os filtros atuais) que a verificação
achou sem uso, depois de mostrar a lista para confirmar. Os que estão em uso nunca são deletados. Para auditar os
duplicados fora do MetroShared: filtre a coluna Padrão, clique em Verificar uso e em Deletar sem uso.

Cuidados que a ferramenta toma:

- No projeto, deletar remove o vínculo de parâmetro de projeto, como o **Remover** do diálogo Parâmetros de projeto.
  O registro do GUID fica no arquivo, porque alguma família carregada pode usá-lo.
- Parâmetros que existem só por causa de famílias ou etiquetas carregadas não são deletados daqui: apague nas famílias.
- Em famílias de anotação (etiquetas, carimbos) nada é deletado, porque a API não mostra os rótulos de texto.
- Tudo numa transação: Ctrl+Z no Revit desfaz depois de fechar a janela.

# MetroSP 0.2.3

Revit 2025, 2026 e 2027.

## Exportar IFC: cada arquivo é indicado pelo usuário

Sai a pasta padrão (e o botão "Criar pasta padrão"). A janela agora pede, cada um com o seu **Procurar...**:

1. o **setup de exportação** (.json);
2. o **.txt de property sets**;
3. a **tabela de mapeamento de parâmetros** (.txt, opcional).

Os arquivos podem estar em qualquer pasta: computador, rede ou Forma pelo Desktop Connector. Ao escolher
o setup, os .txt indicados nele são preenchidos quando achados nesta máquina, e dá para trocar qualquer um.
As três escolhas ficam lembradas para a próxima exportação. A conferência dos parâmetros contra o modelo continua.

# MetroSP 0.2.2

Revit 2025, 2026 e 2027.

## Novo: janela de progresso nas operações longas

Uma janela mostra a etapa atual, a barra com "N de total", o item em andamento, o tempo decorrido
com estimativa do que falta e um histórico do que já foi feito. Ela continua se atualizando mesmo
com o Revit ocupado.

- **Renomear Compartilhado:** as 4 etapas (renomear dentro das famílias, guardar valores, recarregar,
  restaurar), com o nome de cada família aberta, aninhadas incluídas. Tem botão **Cancelar**: o que já foi
  feito é desfeito e o projeto fica como estava.
- **Verificar Classificação:** análise elemento a elemento e as famílias abertas para ler fórmulas.
- **Remover fórmula da família:** uma família por vez, com **Cancelar** (as que já foram recarregadas ficam).
- **Gravar classificação** (Aplicar, Verificar e Consistência): aparece a partir de 200 elementos, incluindo
  o empréstimo de elementos no worksharing.
- **Exportar IFC:** indica que a exportação está em andamento (o exportador do Revit não informa a porcentagem).

# MetroSP 0.2.1

Revit 2025, 2026 e 2027.

## Correção: Renomear Compartilhado

Na 0.2.0, o comando dizia que tinha renomeado, mas o projeto continuava mostrando o nome antigo.
O projeto (e cada família) guarda um único registro do nome por GUID, e recarregar as famílias
renomeadas não mudava esse registro.

- O nome antigo agora é liberado no projeto e nas famílias antes de o GUID voltar com o nome novo.
- Projeto e famílias são renomeados numa única operação: famílias renomeadas, registro antigo apagado,
  famílias recarregadas, vínculo refeito e valores e tabelas restaurados.
- No final o nome é conferido. Se o projeto não mostrar o nome novo, tudo é desfeito e a mensagem diz o motivo
  (por exemplo, uma família in-place ou protegida que tem o parâmetro). Não aparece mais "renomeado" sem ter renomeado.
- Rodar de novo num modelo em que as famílias já foram renomeadas, mas o projeto não, agora resolve.

# MetroSP 0.2.0

Revit 2025, 2026 e 2027.

## Novo: Exportar IFC (painel IFC / IDS)

Exporta o IFC com o setup do exportador (.json) e o mapeamento de property sets (.txt) guardados
fora do modelo, para toda a equipe usar a mesma configuração.

- **Origem dos arquivos:** projeto do Autodesk Forma, lido pelo Desktop Connector, ou uma pasta no computador ou na rede.
- **Pasta padrão no Forma**, igual em todo projeto: `Project Files\BIM\Configuracoes IFC\` com `Setups\`,
  `PropertySets\` e `Mapeamentos\`. Escolhendo `Civil.json`, o `Civil.txt` de cada subpasta entra sozinho.
  O botão **Criar pasta padrão** monta a estrutura num projeto novo. O caminho pode ser alterado na janela.
- **Projeto na nuvem:** se o modelo aberto está no Forma, o projeto dele já vem selecionado.
- **Conferência antes de exportar:** lê o .txt e avisa quais parâmetros do mapeamento não existem no modelo.
- **Vista:** exporta o modelo inteiro ou só o que está visível numa vista 3D. Pasta de saída e nome do arquivo ficam lembrados.
- **Caminho do .txt:** o caminho gravado no .json, que é o da máquina de quem salvou, é corrigido sozinho procurando o arquivo pelo nome.

Limites: vínculos não são exportados; a classificação e o endereço do projeto usados são os salvos no modelo;
o modelo de mapeamento guardado no .rvt (Revit 2025+) é ignorado, para valer o .txt.

## Também nesta versão

- **Renomear Compartilhado:** renomeia o parâmetro também em todas as famílias carregadas, inclusive as
  aninhadas, mantendo o GUID e os valores.
- **Verificar Classificação:** remove a fórmula da família quando ela impede gravar a classificação.
- **Ribbon:** painel com as logos.
- **Janelas de lista:** filtros por coluna, seleção por categoria e "aplicar só as selecionadas".

## Instalar

Feche o Revit, extraia o .zip e rode `Instalar.bat`. Ele instala em todas as versões do Revit
encontradas no computador. Para desinstalar: `Instalar.bat -Desinstalar`.
