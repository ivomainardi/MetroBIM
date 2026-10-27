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

# MetroSP 0.2.2 (anterior)

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
