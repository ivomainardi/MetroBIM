# MetroSP 0.2.7

Revit 2025, 2026 e 2027.

## Exportar IFC: classes prontas no instalador

- O instalador traz os .txt de property sets (e, quando houver, o setup .json e o mapeamento) de cada classe.
  Na janela, basta escolher a **Classe** e exportar.
- Continua dando para trocar qualquer arquivo pelo **Procurar...** (a classe passa para "Outros arquivos").
- Novo **Editar property sets...**: mostra o .txt em tabela, com as propriedades cujo parâmetro não existe no
  modelo destacadas, sugere os parâmetros do modelo, e permite acrescentar e remover propriedades. A edição de
  uma classe instalada fica salva só para o usuário, com **Restaurar padrão** para voltar ao original.
- **Classe pelo nome do modelo:** a janela já abre com a classe indicada pelo nome do arquivo, por regras num
  `Nomenclatura.txt` ou pelo trecho do nome igual ao nome da classe.
- **Forma online:** dá para entrar com a conta Autodesk e escolher uma pasta do Forma direto pela internet, sem o
  Desktop Connector. Os arquivos dela são baixados e, a cada exportação, só o que mudou no Forma é atualizado.

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
